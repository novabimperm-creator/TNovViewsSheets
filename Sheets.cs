using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TNovCommon;

namespace TNovViewsSheets
{
    [Transaction(TransactionMode.Manual)]
    public class Sheets : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            if (RevitAPI.UiApplication == null) { RevitAPI.Initialize(commandData); }

            // Создаём и показываем WPF-окно
            var window = new MainWindow(uidoc);
            window.ShowDialog();

            return Result.Succeeded;
        }
    }
}