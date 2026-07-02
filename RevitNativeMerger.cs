using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using TNovCommon;

namespace TNovViewsSheets
{
    /// <summary>
    /// BimStep-style sheet merger: combines N sheets into one DWG entirely
    /// inside Revit, without launching AutoCAD.
    ///
    /// Algorithm: open a Manual transaction; for every sheet after the first
    /// ("target"), copy its title-block + annotations onto the target at a
    /// grid offset, and recreate every Viewport via Viewport.Create (the
    /// source viewport must be deleted first because a non-legend View can
    /// live on only one sheet at a time — that destruction is undone by the
    /// final Transaction.RollBack). Final DWG export of the target sheet,
    /// now carrying all sheets' content, is the result.
    ///
    /// Why not Import-roundtrip: doc.Import of a Revit-exported DWG back onto
    /// a sheet brings paper-space content (title block, annotations) but the
    /// viewport rectangles come in EMPTY — viewport objects round-trip but
    /// don't get re-attached to model-space content. So the final DWG ends
    /// up with frames-only and no view drawings. That was tried and visibly
    /// failed (only the target sheet had geometry; the other 8 frames were
    /// blank).
    /// </summary>
    public class RevitNativeMerger
    {
        public string Merge(Document doc, IList<SheetItem> sheets, string outputPath,
                    string dwgExportSetupName, ExportColorMode colors)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            var real = sheets.Where(s => !s.IsPlaceholder && s.Sheet != null).ToList();
            if (real.Count == 0)
                throw new InvalidOperationException("Нет листов для экспорта (все placeholder).");

            DWGExportOptions opts = DwgExportOptionsFactory.Create(doc, dwgExportSetupName, colors);
            string outDir = Path.GetDirectoryName(outputPath);
            string outName = Path.GetFileNameWithoutExtension(outputPath);
            if (string.IsNullOrEmpty(outDir))
                throw new ArgumentException("OutputPath has no directory.", nameof(outputPath));
            Directory.CreateDirectory(outDir);

            Logger.Log($"native-merge: {real.Count} sheet(s) -> {outputPath}");

            if (real.Count == 1)
            {
                if (!doc.Export(outDir, outName, new List<ElementId> { real[0].Sheet.Id }, opts))
                    throw new InvalidOperationException($"Revit не смог экспортировать лист {real[0].SheetNumber}.");
                Logger.Log("native-merge: single sheet, direct export done");
                return outputPath;
            }

            using (var tx = new Transaction(doc, "RevitSheetsToDwg: merge sheets"))
            {
                tx.Start();
                try
                {
                    ViewSheet target = real[0].Sheet;

                    // ---------- 1. Собрать bounding box всех листов ----------
                    var sheetBBs = new List<(SheetItem Item, BoundingBoxXYZ BB)>();
                    foreach (var item in real)
                    {
                        BoundingBoxXYZ bb = GetTitleBlockBB(doc, item.Sheet);
                        if (bb != null)
                            sheetBBs.Add((item, bb));
                        else
                            Logger.Log($"native-merge: SKIP {item.SheetNumber} (no title block bb)");
                    }
                    
                    if (sheetBBs.Count == 0)
                        throw new InvalidOperationException("Ни на одном листе не найдена основная надпись.");

                    // ---------- 2. Фиксированная сетка 120×120 мм + зазор ----------
                    const double fixedCellW = 2.4;
                    const double fixedCellH = 2.4;
                    const double gap = 1;
                    double cellW = fixedCellW + gap;
                    double cellH = fixedCellH + gap;

                    int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(sheetBBs.Count)));
                    int rows = (int)Math.Ceiling(sheetBBs.Count / (double)cols);

                    Logger.Log($"native-merge: grid {cols}x{rows}, cell {cellW:F1}x{cellH:F1} mm");

                    // ---------- 3. Разместить ВСЕ листы (включая первый) с началом в (0,0) ----------
                    double originX = 0.0;
                    double originY = 0.0;

                    for (int i = 0; i < sheetBBs.Count; i++)
                    {
                        var (item, bb) = sheetBBs[i];
                        int row = i / cols;
                        int col = i % cols;

                        // Левый нижний угол ячейки
                        double targetX = originX + col * cellW;
                        double targetY = originY - row * cellH; // верхняя строка — y=0

                        // Смещение для перемещения листа в ячейку
                        XYZ offset = new XYZ(targetX - bb.Min.X, targetY - bb.Min.Y, 0);

                        Logger.Log($"native-merge: place {item.SheetNumber} row={row} col={col} offset=({offset.X:F1},{offset.Y:F1})",2);

                        // Копируем все листы, включая первый (target), с нужным смещением
                        CopySheetOntoTarget(doc, item.Sheet, target, offset, item.SheetNumber);
                    }

                    doc.Regenerate();

                    if (!doc.Export(outDir, outName, new List<ElementId> { target.Id }, opts))
                        throw new InvalidOperationException("Финальный DWG-экспорт собранного листа не удался.");
                    Logger.Log($"native-merge: final export OK -> {outputPath}");
                }
                finally
                {
                    tx.RollBack();
                }
            }

            string actual = DwgExportOptionsFactory.ResolveProducedFile(outDir, outName);
            return actual ?? outputPath;
        }

        /// <summary>
        /// Copies title block + annotations from src sheet to target sheet,
        /// then recreates each viewport via Viewport.Create at the offset
        /// position. Source viewports are deleted (to free their views — a
        /// view can only be on one sheet at a time), but only inside our
        /// rollback transaction, so the user never sees that change.
        /// </summary>
        private static void CopySheetOntoTarget(Document doc, ViewSheet src, ViewSheet target,
                                                XYZ offset, string srcNumber)
        {
            // 1) Copy every NON-VIEWPORT element on the source sheet to target.
            //    This carries the title block, sheet-level annotations, schedule
            //    instances, generic-model lines drawn directly on the sheet, etc.
            var nonViewportIds = new FilteredElementCollector(doc, src.Id)
                .WhereElementIsNotElementType()
                .Where(e => e != null && !(e is Viewport) && e.Id != src.Id)
                .Select(e => e.Id)
                .ToList();

            if (nonViewportIds.Count > 0)
            {
                ICollection<ElementId> copied = null;
                try
                {
                    copied = ElementTransformUtils.CopyElements(src, nonViewportIds, target,
                        Transform.Identity, new CopyPasteOptions());
                }
                catch (Exception ex)
                {
                    Logger.Log($"native-merge[{srcNumber}]: CopyElements bulk failed: {ex.Message}");
                }

                if (copied != null && copied.Count > 0)
                {
                    // Some elements (typically title blocks) arrive pinned and
                    // would make the bulk MoveElements fail.
                    foreach (ElementId cid in copied)
                    {
                        Element c = doc.GetElement(cid);
                        if (c == null || !c.Pinned) continue;
                        try { c.Pinned = false; }
                        catch (Exception ex)
                        {
                            Logger.Log($"native-merge[{srcNumber}]: unpin {RevitApiCompat.ElementIdIntValue(cid)} failed: {ex.Message}",4);
                        }
                    }
                    try { ElementTransformUtils.MoveElements(doc, copied, offset); }
                    catch (Exception ex)
                    {
                        Logger.Log($"native-merge[{srcNumber}]: MoveElements bulk failed: {ex.Message}",4);
                    }
                    Logger.Log($"native-merge[{srcNumber}]: copied {copied.Count} non-viewport element(s)");
                }
            }

            // 2) Recreate every viewport on the target sheet at offset+boxCenter.
            //    Viewport.Create needs the View to be "free" (not on any sheet),
            //    so delete the source viewport first. View.Duplicate is needed
            //    for legends because a legend may legitimately appear on many
            //    sheets and the rule that frees a regular view doesn't apply.
            var sourceVpIds = src.GetAllViewports().ToList();
            int recreated = 0, legendDup = 0, failed = 0;
            foreach (ElementId vpId in sourceVpIds)
            {
                if (!(doc.GetElement(vpId) is Viewport vp)) continue;
                ElementId viewId = vp.ViewId;
                XYZ boxCenter = vp.GetBoxCenter();
                ElementId origTypeId = vp.GetTypeId();
                View view = doc.GetElement(viewId) as View;
                if (view == null) { failed++; continue; }
                bool isLegend = view.ViewType == ViewType.Legend;

                // Free the view by removing the original viewport (rollback restores it).
                try { doc.Delete(vpId); }
                catch (Exception ex)
                {
                    Logger.Log($"native-merge[{srcNumber}]: delete source viewport vp={RevitApiCompat.ElementIdIntValue(vpId)} failed: {ex.Message}",4);
                    failed++;
                    continue;
                }

                Viewport newVp = null;
                try
                {
                    ElementId placedViewId;
                    if (isLegend)
                    {
                        placedViewId = view.Duplicate(ViewDuplicateOption.WithDetailing);
                        legendDup++;
                    }
                    else
                    {
                        placedViewId = viewId;
                    }
                    newVp = Viewport.Create(doc, target.Id, placedViewId, boxCenter + offset);
                }
                catch (Exception ex)
                {
                    Logger.Log($"native-merge[{srcNumber}]: Viewport.Create view={RevitApiCompat.ElementIdIntValue(viewId)} legend={isLegend} failed: {ex.Message}", 4);
                    failed++;
                    continue;
                }

                if (newVp != null)
                {
                    // Preserve original viewport type (controls label visibility, etc.).
                    if (origTypeId != ElementId.InvalidElementId)
                    {
                        try { newVp.ChangeTypeId(origTypeId); }
                        catch (Exception ex)
                        {
                            Logger.Log($"native-merge[{srcNumber}]: ChangeTypeId on new viewport failed: {ex.Message}", 4);
                        }
                    }
                    recreated++;
                }
            }
            Logger.Log($"native-merge[{srcNumber}]: viewports recreated={recreated} legendDup={legendDup} failed={failed} of {sourceVpIds.Count}");
        }

        private static BoundingBoxXYZ GetTitleBlockBB(Document doc, ViewSheet sheet)
        {
            // Look for a title-block family instance owned by this sheet.
            var tbs = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .ToElements();

            foreach (Element tb in tbs)
            {
                BoundingBoxXYZ bb = tb.get_BoundingBox(sheet);
                if (bb != null) return bb;
            }

            // Fallback: union of viewport bboxes (BimStep does this).
            var viewportIds = sheet.GetAllViewports();
            if (viewportIds.Count > 0)
            {
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                bool any = false;
                foreach (ElementId vpId in viewportIds)
                {
                    if (!(doc.GetElement(vpId) is Viewport vp)) continue;
                    BoundingBoxXYZ bb = vp.get_BoundingBox(sheet);
                    if (bb == null) continue;
                    any = true;
                    if (bb.Min.X < minX) minX = bb.Min.X;
                    if (bb.Min.Y < minY) minY = bb.Min.Y;
                    if (bb.Max.X > maxX) maxX = bb.Max.X;
                    if (bb.Max.Y > maxY) maxY = bb.Max.Y;
                }
                if (any)
                {
                    return new BoundingBoxXYZ
                    {
                        Min = new XYZ(minX, minY, 0),
                        Max = new XYZ(maxX, maxY, 0),
                    };
                }
            }
            return null;
        }

    }
}
