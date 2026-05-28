using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace TNovViewsSheets
{
    /// <summary>
    /// Single source of truth for DWG export options used by both the
    /// AutoCAD-based MultiLayout pipeline (SheetExporter) and the Revit-native
    /// TiledModelSpace merger (RevitNativeMerger). Centralising the flags
    /// prevents the two paths from drifting — for instance, an earlier copy
    /// in RevitNativeMerger was missing TargetUnit.Millimeter, which made
    /// merged DWGs show dimension text in scaled feet (1700mm rendered as
    /// "518160") while MultiLayout output was correct.
    /// </summary>
    internal static class DwgExportOptionsFactory
    {
        public static DWGExportOptions Create(Document doc, string setupName, ExportColorMode colors)
        {
            DWGExportOptions opts = null;
            if (!string.IsNullOrWhiteSpace(setupName))
            {
                try { opts = DWGExportOptions.GetPredefinedOptions(doc, setupName); }
                catch { /* setup may not exist on this doc; fall through to defaults */ }
            }
            if (opts == null) opts = new DWGExportOptions();

            // MergedViews=true bakes each viewport's content into the sheet DWG
            // itself — without it Revit emits per-view xref-DWGs that break
            // both the AutoCAD merge and the Import round-trip downstream.
            opts.MergedViews = true;
            opts.SharedCoords = false;
            opts.Colors = colors;
            // Without an explicit unit, a Foot-based predefined setup yields
            // DIMLFAC=304.8 and dimension text shows decimal feet.
            opts.TargetUnit = ExportUnit.Millimeter;
            opts.HideScopeBox = false;
            // Reference planes are hidden in production output. Scope boxes
            // and unreferenced view tags stay visible: those categories
            // sometimes carry title-block geometry, and dropping them
            // reflows the visible composition of the sheet.
            opts.HideReferencePlane = true;
            opts.HideUnreferenceViewTags = false;
            return opts;
        }

        /// <summary>
        /// Revit may mangle the requested base filename (length, illegal chars,
        /// or the export setup's own naming rules), so after `doc.Export`
        /// returns true the file isn't always at `outDir\baseName.dwg`. Returns
        /// the actual DWG most recently written by Revit, optionally excluding
        /// files already accounted for by previous iterations.
        /// </summary>
        public static string ResolveProducedFile(string outDir, string baseName,
                                                 System.Collections.Generic.IEnumerable<string> exclude = null)
        {
            string direct = Path.Combine(outDir, baseName + ".dwg");
            if (File.Exists(direct)) return direct;

            var candidates = Directory.GetFiles(outDir, "*.dwg");
            if (exclude != null) candidates = candidates.Except(exclude).ToArray();
            return candidates
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
    }
}
