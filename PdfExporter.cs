using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using TNovCommon;

namespace TNovViewsSheets
{
    /// <summary>
    /// Exports selected sheets to a single consolidated PDF using Revit's
    /// built-in PDFExportOptions (Revit 2022+). No AutoCAD round-trip — Combine=true
    /// produces one multi-page PDF directly.
    /// </summary>
    public class PdfExporter
    {
        /// <summary>
        /// Writes a single consolidated PDF and returns the produced file path.
        /// </summary>
        /// <param name="outputPdfPath">Full path of the desired PDF file (.pdf).</param>
        /// <param name="fitToSheetSize">
        /// true → each sheet uses its own title-block paper size (PaperFormat.Default).
        /// false → fall back to ISO A4 for every sheet.
        /// </param>
        public string Export(Document doc, IList<SheetItem> sheets, string outputPdfPath, bool fitToSheetSize, 
            bool presQuality, bool rastr, bool color)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (sheets == null || sheets.Count == 0)
                throw new ArgumentException("No sheets to export.", nameof(sheets));
            if (string.IsNullOrWhiteSpace(outputPdfPath))
                throw new ArgumentException("Output path is required.", nameof(outputPdfPath));

            string folder = Path.GetDirectoryName(outputPdfPath);
            string baseName = Path.GetFileNameWithoutExtension(outputPdfPath);
            if (string.IsNullOrEmpty(folder))
                throw new ArgumentException("Output path has no directory.", nameof(outputPdfPath));
            Directory.CreateDirectory(folder);

            var ids = sheets
                .Where(s => !s.IsPlaceholder)
                .Select(s => s.ElementId)
                .ToList();

            if (ids.Count == 0)
                throw new InvalidOperationException("All selected sheets are placeholders.");

            Logger.Log($"PDF: start fitToSheet={fitToSheetSize} sheets={ids.Count}");

            // Try the "let Revit pick per-sheet paper" path first — this is the
            // only way to get a multi-page PDF where each sheet uses its own
            // title-block paper size, matching what File → Export → PDF does
            // interactively. Auto orientation has been observed to return FALSE
            // on Revit 2022, so we sweep through several combinations and stop
            // on the first that succeeds. PickBestPaperFormat enumerates sheets
            // and is lazy — only evaluated if the three Default attempts fail.
            bool ok = false;
            ExportPaperFormat usedFmt = default;
            PageOrientationType usedOrient = default;
            foreach (var (fmt, orient) in EnumerateAttempts(doc, sheets, fitToSheetSize))
            {
                if (TryExport(doc, ids, folder, baseName, fmt, orient, presQuality,rastr,color))
                {
                    ok = true;
                    usedFmt = fmt;
                    usedOrient = orient;
                    break;
                }
            }

            if (!ok)
                throw new InvalidOperationException(
                    $"Revit PDF export returned false for every attempted format/" +
                    $"orientation combination. Листов: {ids.Count}, BaseName: {baseName}, " +
                    $"Папка: {folder}");

            Logger.Log($"PDF: succeeded with {usedFmt}/{usedOrient}");

            string produced = Path.Combine(folder, baseName + ".pdf");
            if (!File.Exists(produced))
                throw new FileNotFoundException("PDF was not produced at expected path.", produced);

            return produced;
        }

        private static IEnumerable<(ExportPaperFormat Fmt, PageOrientationType Orient)> EnumerateAttempts(
            Document doc, IList<SheetItem> sheets, bool fitToSheetSize)
        {
            if (!fitToSheetSize)
            {
                yield return (ExportPaperFormat.ISO_A4, PageOrientationType.Portrait);
                yield break;
            }
            yield return (ExportPaperFormat.Default, PageOrientationType.Auto);
            yield return (ExportPaperFormat.Default, PageOrientationType.Landscape);
            yield return (ExportPaperFormat.Default, PageOrientationType.Portrait);
            // Historic single-format fallback (computed only if the three Default
            // attempts above all returned FALSE — saves N collector iterations).
            yield return PickBestPaperFormat(doc, sheets);
        }

        private static bool TryExport(Document doc, IList<ElementId> ids, string folder,
            string baseName, ExportPaperFormat fmt, PageOrientationType orient,
            bool presQuality, bool rastr, bool color)
        {
            // Reference planes are hidden; scope boxes, view tags and crop
            // boundaries stay visible because those categories sometimes carry
            // title-block composition, and dropping them reflows the sheet.
            ColorDepthType colorDepthType = ColorDepthType.BlackLine;
            if (color) colorDepthType = ColorDepthType.Color;
            RasterQualityType rasterQualityType = RasterQualityType.High;
            if(presQuality) rasterQualityType = RasterQualityType.Presentation;

            var opts = new PDFExportOptions
            {
                Combine = true,
                FileName = baseName,
                PaperFormat = fmt,
                PaperOrientation = orient,
                PaperPlacement = PaperPlacementType.Center,
                ZoomType = ZoomType.FitToPage,
                HideScopeBoxes = true,
                HideReferencePlane = true,
                HideUnreferencedViewTags = false,
                HideCropBoundaries = true,
                StopOnError = false,
                ColorDepth = colorDepthType,
                RasterQuality = rasterQualityType,
                AlwaysUseRaster = rastr
            };
            bool ok = doc.Export(folder, ids, opts);
            Logger.Log($"PDF: doc.Export({fmt},{orient}) -> {(ok ? "OK" : "FALSE")}");
            return ok;
        }

        // Standard ISO formats in ascending size — looked up to find the smallest
        // paper that contains a given sheet. Title-block geometry usually has a
        // 1–2 mm border outside the cut line, hence the small tolerance below.
        private static readonly (ExportPaperFormat Format, double LongMm, double ShortMm)[] IsoFormats =
        {
            (ExportPaperFormat.ISO_A4,  297.0, 210.0),
            (ExportPaperFormat.ISO_A3,  420.0, 297.0),
            (ExportPaperFormat.ISO_A2,  594.0, 420.0),
            (ExportPaperFormat.ISO_A1,  841.0, 594.0),
            (ExportPaperFormat.ISO_A0, 1189.0, 841.0),
        };

        private static (ExportPaperFormat fmt, PageOrientationType orient) PickBestPaperFormat(
            Document doc, IList<SheetItem> sheets)
        {
            int bestIdx = -1;
            bool bestIsLandscape = true;
            foreach (var s in sheets)
            {
                if (s.IsPlaceholder) continue;
                (double w, double h) = ReadTitleBlockSize(doc, s.ElementId);
                if (w <= 0 || h <= 0) continue;

                int idx = SmallestFormatIndex(w, h);
                Logger.Log($"PDF: sheet {s.SheetNumber} titleblock {w:0.0}x{h:0.0} mm -> idx {idx}");
                if (idx > bestIdx)
                {
                    bestIdx = idx;
                    bestIsLandscape = w > h;
                }
            }
            if (bestIdx < 0)
                return (ExportPaperFormat.ISO_A4, PageOrientationType.Landscape);
            return (IsoFormats[bestIdx].Format,
                bestIsLandscape ? PageOrientationType.Landscape : PageOrientationType.Portrait);
        }

        private static int SmallestFormatIndex(double widthMm, double heightMm)
        {
            double longSide = Math.Max(widthMm, heightMm);
            double shortSide = Math.Min(widthMm, heightMm);
            const double tol = 2.0;
            for (int i = 0; i < IsoFormats.Length; i++)
            {
                if (IsoFormats[i].LongMm + tol >= longSide &&
                    IsoFormats[i].ShortMm + tol >= shortSide)
                    return i;
            }
            return IsoFormats.Length - 1; // fall back to the largest we know (A0)
        }

        private static (double widthMm, double heightMm) ReadTitleBlockSize(Document doc, ElementId sheetId)
        {
            // SHEET_WIDTH / SHEET_HEIGHT live on the title-block FamilyInstance,
            // not on the ViewSheet itself. Values are in internal feet.
            var titleBlocks = new FilteredElementCollector(doc, sheetId)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>();

            foreach (var tb in titleBlocks)
            {
                Parameter pw = tb.get_Parameter(BuiltInParameter.SHEET_WIDTH);
                Parameter ph = tb.get_Parameter(BuiltInParameter.SHEET_HEIGHT);
                if (pw != null && ph != null && pw.HasValue && ph.HasValue)
                {
                    return (pw.AsDouble() * 304.8, ph.AsDouble() * 304.8);
                }
            }
            return (0, 0);
        }
    }
}
