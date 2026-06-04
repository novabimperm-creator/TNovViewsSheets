using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using TNovCommon;

namespace TNovViewsSheets
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportSheetsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            #region Исходные
            DateTime dateTime = DateTime.Now;
            string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            string DBCommandName = "Экспорт листов";
            //подключение приложения и документа
            if (RevitAPI.UiApplication == null) { RevitAPI.Initialize(commandData); }
            UIDocument uidoc = RevitAPI.UiDocument; Document doc = RevitAPI.Document;
            UIApplication uiApp = RevitAPI.UiApplication; Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application;
            string docName = doc.Title.ToString(); docName = docName.Replace(",", " ");
            string userName = rvtApp.Username; userName = userName.Replace(",", "");
            string docNameUserName = "_" + userName; docName = docName.Replace(docNameUserName, "");
            docName = docName.Replace(",", "");
            #endregion

            TNovConfig config = TNovConfigLoad.LoadConfig(DBCommandName, TNovVersion);

            #region Настройки логов
            // создание log - файла
            Logger.Initialize(DBCommandName, dateTime, TNovVersion);

            var viewModel0 = new AppVersionViewModel();

            string jsonpath0 = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "TNovClient/TNovSettings.json");
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
            #endregion


            if (uidoc == null)
            {
                message = "No active document.";
                Logger.Log("Завершение работы с ошибкой: " + message, 4);
                return Result.Failed;
            }

            try
            {
                #region Сбор элементов
                // 1. Collect all sheets in the project (skip placeholders for export but show them in the list).
                var sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .OrderBy(s => s.SheetNumber, NaturalStringComparer.Instance)
                    .Select(s => new SheetItem(s))
                    .ToList();

                if (sheets.Count == 0)
                {
                    TaskDialog.Show("Sheets To DWG", "This project contains no sheets.");
                    return Result.Cancelled;
                }

                // 2. Collect available DWG export setup names.
                var setupNames = new List<string>();
                try
                {
                    setupNames.AddRange(BaseExportOptions.GetPredefinedSetupNames(doc));
                }
                catch
                {
                    // Older API surface; ignore.
                }
                #endregion

                #region Диалог
                // 3. Show selection dialog.
                var window = new SheetSelectionWindow(doc, sheets, setupNames);
                new WindowInteropHelper(window) { Owner = uiApp.MainWindowHandle };

                bool? dialog = window.ShowDialog();
                if (dialog != true || window.Result == null)
                    return Result.Cancelled;

                ExportOptions options = window.Result;
                var produced = new List<string>();

                Logger.Log($"Export: pdf={options.ExportPdf} dwg={options.ExportDwg} " +
                              $"sheets={options.SelectedSheets.Count} -> {options.OutputBasePath}");
                #endregion

                #region Экспорт
                // Снимаем активное выделение перед экспортом: Revit запекает
                // selection highlight выделенных элементов в PDF/печать. Если
                // пользователь случайно выделил лист/элементы перед запуском
                // плагина — подсветка попадала в готовый PDF.
                uidoc.Selection.SetElementIds(new List<ElementId>());
                uidoc.RefreshActiveView();

                // PDF first — no external dependency, fast, and if AutoCAD is missing
                // we at least leave the user with the PDF.

                string pdfPath = options.OutputBasePath + ".pdf";
                if (options.ExportPdf)
                {
                    var pdfExporter = new PdfExporter();
                    string actual = pdfExporter.Export(doc, options.SelectedSheets, pdfPath,
                        options.PdfFitToSheetSize, options.PdfQuality, options.PdfRastr, options.PdfColor);
                    produced.Add(actual);
                }

                if (options.ExportDwg)
                {
                    string dwgResult = RunDwgPipeline(doc, options);
                    if (dwgResult != null) produced.Add(dwgResult);
                }
                #endregion

                if (produced.Count == 0)
                {
                    message = "Не удалось создать ни одного файла.";
                    Logger.Log("Завершение работы с ошибкой: " + message, 4);
                    return Result.Failed;
                }
                string folderPath = Path.GetDirectoryName(options.OutputBasePath);
                Process.Start("explorer.exe", folderPath);
                new InfoWindow400("Готово:\n" + string.Join("\n", produced)).ShowDialog();

                Logger.Log("Завершение работы.", 5);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                new InfoWindow400("Экспорт листов — ошибка: " + ex.Message).ShowDialog();
                Logger.Log("Завершение работы с ошибкой: " +ex.Message, 4);
                return Result.Failed;
            }
        }

        /// <summary>
        /// Returns the produced DWG path on success, or null if AutoCAD is
        /// unavailable (caller continues — PDF may still have been produced).
        /// </summary>
        private static string RunDwgPipeline(Document doc, ExportOptions options)
        {
            string dwgPath = options.OutputBasePath + ".dwg";

            // TiledModelSpace runs entirely inside Revit: copy each source
            // sheet's elements onto the first ("target") sheet at a grid
            // offset, recreate viewports via Viewport.Create, export the
            // target sheet, then RollBack the in-memory changes. AutoCAD is
            // not involved.
            if (options.Mode == ExportMode.TiledModelSpace)
            {
                var native = new RevitNativeMerger();
                return native.Merge(doc, options.SelectedSheets, dwgPath,
                    options.DwgExportSetupName, options.Colors);
            }

            // MultiLayout still needs AutoCAD — only AutoCAD-files can carry
            // multiple Layout tabs.
            string tempDir = Path.Combine(Path.GetTempPath(),
                "RevitSheetsToDwg_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tempDir);

            try
            {
                var exporter = new SheetExporter();
                List<string> dwgFiles = exporter.ExportSheets(doc, options.SelectedSheets,
                    options.DwgExportSetupName, tempDir, options.Colors);

                if (dwgFiles.Count == 0)
                    throw new InvalidOperationException(
                        "Revit DWG-экспорт не создал файлов. Проверьте DWG export setup.");

                var merger = new AutoCadMerger();
                if (!merger.IsAutoCadAvailable())
                {
                    TaskDialog.Show("Экспорт листов",
                        "AutoCAD не установлен — DWG-слияние пропущено.\n\n" +
                        "Per-sheet DWG оставлены здесь:\n" + tempDir + "\n\n" +
                        "Установите AutoCAD и повторите, либо объедините файлы вручную.\n\n" +
                        "Подсказка: переключите режим на «Tiled Model Space» — он работает без AutoCAD.");
                    tempDir = null; // keep the temp dir for the user
                    return null;
                }

                merger.Merge(dwgFiles, dwgPath, options.Mode);
                return dwgPath;
            }
            finally
            {
                if (tempDir != null && Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { /* best-effort */ }
                }
            }
        }
    }
}
