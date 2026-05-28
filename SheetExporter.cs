using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace TNovViewsSheets
{
    /// <summary>
    /// Exports each selected sheet to a separate DWG file in a target folder
    /// using Revit's built-in DWG export.
    /// </summary>
    public class SheetExporter
    {
        /// <summary>
        /// Returns the list of produced DWG file paths, in the same order as the input sheets.
        /// </summary>
        public List<string> ExportSheets(Document doc, IList<SheetItem> sheets,
                                         string exportSetupName, string outputDirectory,
                                         ExportColorMode colors = ExportColorMode.TrueColor)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (sheets == null || sheets.Count == 0) return new List<string>();
            Directory.CreateDirectory(outputDirectory);

            DWGExportOptions opts = DwgExportOptionsFactory.Create(doc, exportSetupName, colors);

            var producedFiles = new List<string>(sheets.Count);
            int counter = 0;

            foreach (SheetItem item in sheets)
            {
                if (item.IsPlaceholder) continue;

                counter++;
                // Use a deterministic, ordered filename so the merger can preserve sheet order.
                string baseName = $"{counter:D3}_{Sanitize(item.SheetNumber)}_{Sanitize(item.SheetName)}";
                if (baseName.Length > 80) baseName = baseName.Substring(0, 80);

                var ids = new List<ElementId> { item.ElementId };

                bool ok = doc.Export(outputDirectory, baseName, ids, opts);
                if (!ok)
                    throw new InvalidOperationException(
                        $"Revit failed to export sheet '{item.SheetNumber} - {item.SheetName}'.");

                string expected = DwgExportOptionsFactory.ResolveProducedFile(
                    outputDirectory, baseName, producedFiles);
                if (expected == null)
                    throw new FileNotFoundException(
                        $"Expected DWG not found after export of sheet '{item.SheetNumber}'.");

                producedFiles.Add(expected);
            }

            return producedFiles;
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Sheet";
            // Replace anything that's not alnum/dash/underscore.
            string clean = Regex.Replace(s, @"[^A-Za-z0-9\-_]+", "_").Trim('_');
            return string.IsNullOrEmpty(clean) ? "Sheet" : clean;
        }
    }
}
