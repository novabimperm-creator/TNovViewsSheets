using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace TNovViewsSheets
{
    /// <summary>
    /// User choices captured by the selection dialog.
    /// </summary>
    public class ExportOptions
    {
        public List<SheetItem> SelectedSheets { get; set; } = new List<SheetItem>();

        // Output path WITHOUT extension. The pipeline appends ".dwg" / ".pdf" as
        // needed so a single user-picked basename can produce one or both formats.
        public string OutputBasePath { get; set; }

        // Format toggles. At least one must be true.
        public bool ExportDwg { get; set; } = true;
        public bool ExportPdf { get; set; }

        // DWG-only options.
        public ExportMode Mode { get; set; } = ExportMode.MultiLayout;
        public string DwgExportSetupName { get; set; } // null = use default

        // Default to TrueColor (object-style RGB) — matches the "Задано в стилях
        // объектов" radio in the Revit DWG export dialog and gives the same colors
        // the user sees in Revit views, instead of AutoCAD's 255-index palette.
        public ExportColorMode Colors { get; set; } = ExportColorMode.TrueColor;

        // PDF-only options.
        // True = each sheet keeps its own title-block paper size (ExportPaperFormat.Default).
        // False = fall back to A4.
        public bool PdfFitToSheetSize { get; set; } = true;
        public bool PdfColor { get; set; }
        public bool PdfQuality { get; set; }
        public bool PdfRastr { get; set; }
    }
}
