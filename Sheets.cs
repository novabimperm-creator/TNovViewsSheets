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

            // Регистрируем запуск: usage-лог на шаре и автоконтекст панели справки.
            // До этого логировалась только устаревшая SheetsOld, поэтому актуальный
            // «Менеджер листов» не попадал ни в статистику, ни в справку.
            string version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            TNovConfigLoad.LoadConfig("Менеджер листов", version);

            // Создаём и показываем WPF-окно
            var window = new MainWindow(uidoc);
            window.ShowDialog();

            return Result.Succeeded;
        }
    }
}