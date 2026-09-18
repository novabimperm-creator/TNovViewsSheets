using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System;
using System.IO;
using TNovCommon;

namespace TNovViewsSheets
{
    [Transaction(TransactionMode.Manual)]
    public class Changes : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            DateTime dateTime = DateTime.Now;
            string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            string DBCommandName = "Изменения";
            if (RevitAPI.UiApplication == null) { RevitAPI.Initialize(commandData); }
            UIDocument uidoc = RevitAPI.UiDocument;
            Document doc = RevitAPI.Document;
            UIApplication uiApp = RevitAPI.UiApplication;
            Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application;
            string docName = doc.Title.ToString(); docName = docName.Replace(",", " ");
            string userName = rvtApp.Username; userName = userName.Replace(",", "");
            string docNameUserName = "_" + userName; docName = docName.Replace(docNameUserName, "");
            docName = docName.Replace(",", "");

            TNovConfigLoad.LoadConfig(DBCommandName, TNovVersion);
            Logger.Initialize(DBCommandName, dateTime, TNovVersion);

            var viewModel0 = new AppVersionViewModel();
            string jsonpath0 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "TNovClient/TNovSettings.json");
            viewModel0 = JsonConvert.DeserializeObject<AppVersionViewModel>(File.ReadAllText(jsonpath0));
            if (viewModel0.extendedLogs)
            {
                var qViewModel = new QuestionWindowViewModel();
                qViewModel.headtxt = "Включены расширенные логи. " +
                    "Плагин будет работать медленнее, но соберет больше данных. " +
                    "Выключить расширенные логи для ускорения работы?";
                var qwpfview = new QuestionWindow280(qViewModel);
                qViewModel.CloseRequest += (s, e) => qwpfview.Close();
                bool? qok = qwpfview.ShowDialog();
                if (qok != null && qok == true) { Logger.TurnOffExtendedLogs(); } else Logger.Log("Расширенные логи вкл", 2);
            }

            var viewModel = new ChangesViewModel();
            bool forProject = true;
            json js = new json(in DBCommandName, in forProject, out bool canserialize, out string jsonpath);
            if (canserialize)
            {
                try
                {
                    viewModel = JsonConvert.DeserializeObject<ChangesViewModel>(File.ReadAllText(jsonpath)) ?? viewModel;
                    Logger.Log("Десериализация прошла успешно", 1);
                }
                catch (Exception ex)
                {
                    Logger.Log("Ошибка при десериализации: " + ex.Message, 4);
                }
            }
            viewModel.StoragePath = jsonpath;
            viewModel.EnsureCollections();

            Logger.Log("Открытие менеджера изменений", 1);
            var window = new ChangesManagerWindow(uidoc, viewModel);
            bool? ok = window.ShowDialog();
            if (ok != true)
            {
                Logger.Log("Менеджер закрыт.", 5);
            }

            try
            {
                window.Settings.StoragePath = jsonpath;
                window.Settings.SaveToProjects();
                Logger.Log("Сериализация прошла успешно", 1);
            }
            catch (Exception ex) { Logger.Log("Ошибка при сериализации: " + ex.Message, 4); }

            Logger.Log("Завершение работы.", 5);
            return Result.Succeeded;
        }
    }
}
