using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using Excel = Microsoft.Office.Interop.Excel;
using System;
using Microsoft.Office.Interop.Excel;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using System.IO;
using TNovCommon;
using System.Globalization;

namespace TNovViewsSheets
{
    
    [Transaction(TransactionMode.Manual)]
    public class Excel : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            System.IO.Directory.CreateDirectory(@"c:\\temp\");


            #region Исходные
            DateTime dateTime = DateTime.Now;
            string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            string DBCommandName = "Excel";
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


            Logger.Log("Проверяем, открыта ли спецификация",1);

            View v = doc.ActiveView;
            bool runIt = v.Title.Contains("Спецификация");


            
            if (runIt == false)
            {
                new InfoWindow280("Ошибка! Текущий открытый вид не является спецификацией.\n" +
                    "Если все же является - щелкните мышью на любую из ячеек таблицы.").ShowDialog();
                Logger.Log("Текущий вид не является спецификацией. Завершение работы",3);
                return Result.Cancelled;
            }
            
            ViewSchedule curview = (ViewSchedule)doc.ActiveView;


            ViewScheduleExportOptions opt = new ViewScheduleExportOptions()
            {
                TextQualifier = ExportTextQualifier.DoubleQuote,
                FieldDelimiter = ","
            };

            #region Диалог

            Logger.Log("Диалоговое окно",1);
            //Вьюмодель (без открытия окна)
            var viewModel = new ExcelViewModel();
            // Десериализация
            string jsonpath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "TNovClient/excel.json");
            try
            {
                viewModel = JsonConvert.DeserializeObject<ExcelViewModel>(File.ReadAllText(jsonpath));
                Logger.Log("Десериализация прошла успешно",1);
            }
            catch (Exception ex) 
            { 
                Logger.Log("Ошибка при десериализации: " + ex.Message,4);
                viewModel.show = true;
            }

            if (viewModel.show)
            {
                var wpfview = new ExcelWPF(viewModel);
                viewModel.CloseRequest += (s, e) => wpfview.Close();
                bool? ok = wpfview.ShowDialog();
                if (ok != null && ok == true) { } 
                else { Logger.Log("Запуск отменен пользователем. Завершение работы.",3); return Result.Cancelled; }
                //Сериализация
                try
                {
                    File.WriteAllText(jsonpath, JsonConvert.SerializeObject(viewModel));
                    Logger.Log("Сериализация прошла успешно",1);
                }
                catch (Exception ex) { Logger.Log("Ошибка при сериализации: " + ex.Message,4); }
            }

            int scenario = 1; if (viewModel.sc2) scenario = 2; else if(viewModel.sc3) scenario = 3;

            #endregion

            switch (scenario)
            {
                case 1:
                    #region Сценарий 1: новая книга в отдельном Excel
                    try
                    {
                        Logger.Log("Экспортируем вид в Excel",1);
                        curview.Export(@"c:\\temp\", curview.Name + ".txt", opt);
                        System.IO.FileInfo file = new System.IO.FileInfo(@"c:\temp\" + curview.Name + ".txt");

                        // запуск Excel
                        Logger.Log("Попытка запуска Excel",1);
                        Microsoft.Office.Interop.Excel.Application xlApp;
                        Microsoft.Office.Interop.Excel.Workbook xlWorkBook;
                        Microsoft.Office.Interop.Excel.Worksheet xlWorkSheet;
                        Microsoft.Office.Interop.Excel.Worksheet xlWorkSheetAllDim;
                        Microsoft.Office.Interop.Excel.QueryTable xlQuery;
                        xlApp = new Microsoft.Office.Interop.Excel.Application();
                        // проверка установлен ли Excel
                        if (xlApp == null)
                        {
                            new InfoWindow280("Ошибка! MS Excel не установлен на данном компьютере.").ShowDialog();
                            Logger.Log("MS Excel не установлен на данном компьютере. Завершение работы.",3);
                            return Result.Cancelled;
                        }

                        Logger.Log("Создаем книгу Excel",1);

                        // define an object to represent default value
                        object default_value = System.Reflection.Missing.Value; // object = object type
                                                                                // create new workbook, which by default contains at least 1 worksheet
                        xlWorkBook = xlApp.Workbooks.Add(default_value);
                        // initialize 2 worksheet variables, all referring to Sheet1 for the time being
                        xlWorkSheetAllDim = (Microsoft.Office.Interop.Excel.Worksheet)xlWorkBook.Worksheets.get_Item(1);
                        xlWorkSheet = (Microsoft.Office.Interop.Excel.Worksheet)xlWorkBook.Worksheets.get_Item(1);

                        Logger.Log("Разворачиваем окно Excel",1);

                        // maximize workbook window
                        xlApp.ActiveWindow.WindowState = Microsoft.Office.Interop.Excel.XlWindowState.xlMaximized;
                        // show menu bars
                        xlApp.Visible = true;

                        Logger.Log("Импорт текстового файла", 1);

                        // 1. Определяем количество колонок (по заголовку CSV)
                        string[] headerFields = File.ReadAllLines(@"c:\temp\" + curview.Name + ".txt")[0].Split(',');
                        int columnCount = headerFields.Length;

                        // 2. Массив FieldInfo: для каждой колонки тип = текст (2)
                        int[,] fieldInfo = new int[columnCount, 2];
                        for (int i = 0; i < columnCount; i++)
                        {
                            fieldInfo[i, 0] = i + 1;                              // номер столбца (1‑based)
                            fieldInfo[i, 1] = (int)XlColumnDataType.xlTextFormat; // xlTextFormat = 2
                        }

                        // 3. Открываем CSV во временной книге с принудительным текстовым форматом и кодировкой UTF-8
                        xlApp.Workbooks.OpenText(
                            Filename: @"c:\temp\" + curview.Name + ".txt",
                            Origin: 65001,                                       // UTF-8
                            StartRow: 1,
                            DataType: XlTextParsingType.xlDelimited,
                            TextQualifier: XlTextQualifier.xlTextQualifierDoubleQuote,
                            Comma: true,
                            FieldInfo: fieldInfo);

                        Workbook tempBook = xlApp.ActiveWorkbook;   // получаем только что открытую книгу

                        // 4. Копируем данные с временного листа на целевой лист
                        Worksheet tempSheet = tempBook.Worksheets[1];
                        Range usedRange = tempSheet.UsedRange;
                        usedRange.Copy(xlWorkSheet.Range["A1"]);

                        // 5. Закрываем временную книгу без сохранения и освобождаем COM‑объекты
                        tempBook.Close(false);
                        Marshal.ReleaseComObject(tempSheet);
                        Marshal.ReleaseComObject(tempBook);

                        // ===== НОВЫЙ БЛОК: Преобразование текстовых чисел в настоящие числа =====
                        Range dataRange = xlWorkSheet.UsedRange;
                        int rows = dataRange.Rows.Count;
                        int cols = dataRange.Columns.Count;

                        // Начинаем со 2-й строки, чтобы не трогать заголовки
                        for (int r = 2; r <= rows; r++)
                        {
                            for (int c = 1; c <= cols; c++)
                            {
                                Range cell = dataRange.Cells[r, c];
                                if (cell.Value2 != null)
                                {
                                    string cellText = cell.Text.ToString().Trim();
                                    if (!string.IsNullOrEmpty(cellText))
                                    {
                                        // Парсим строку как число с точкой-разделителем
                                        if (double.TryParse(cellText, NumberStyles.Any, CultureInfo.InvariantCulture, out double number))
                                        {
                                            cell.NumberFormat = "General";   // сброс формата, чтобы отображалось как число
                                            cell.Value2 = number;            // присваиваем настоящее число
                                        }
                                    }
                                }
                            }
                        }
                        // =====================================================================

                        Logger.Log("Удаляем текстовый файл", 1);
                        System.IO.File.Delete(@"c:\temp\" + curview.Name + ".txt");
                    }
                    catch (Exception ex) { Logger.Log("Ошибка:" + ex.Message,4); }
                    #endregion
                    break;
                case 2:
                    #region Сценарий 2: Новая книга в открытом Excel
                    try
                    {
                        Logger.Log("Экспортируем вид в Excel",1);
                        curview.Export(@"c:\\temp\", curview.Name + ".txt", opt);
                        System.IO.FileInfo file = new System.IO.FileInfo(@"c:\temp\" + curview.Name + ".txt");

                        // запуск Excel
                        Logger.Log("Попытка запуска / получения активного экземпляра Excel",1);
                        Microsoft.Office.Interop.Excel.Application xlApp;
                        Microsoft.Office.Interop.Excel.Workbook xlWorkBook;
                        Microsoft.Office.Interop.Excel.Worksheet xlWorkSheet;
                        Microsoft.Office.Interop.Excel.Worksheet xlWorkSheetAllDim;
                        Microsoft.Office.Interop.Excel.QueryTable xlQuery;
                        try
                        {
                            // Получить активный экземпляр Excel
                            xlApp = (Microsoft.Office.Interop.Excel.Application)Marshal.GetActiveObject("Excel.Application");
                            // define an object to represent default value
                            object default_value = System.Reflection.Missing.Value; // object = object type
                                                                                    // create new workbook, which by default contains at least 1 worksheet
                            xlWorkBook = xlApp.Workbooks.Add(default_value);
                        }
                        catch (COMException)
                        {
                            xlApp = new Microsoft.Office.Interop.Excel.Application();
                            // проверка установлен ли Excel
                            if (xlApp == null)
                            {
                                new InfoWindow280("Ошибка! MS Excel не установлен на данном компьютере.").ShowDialog();
                                Logger.Log("MS Excel не установлен на данном компьютере. Завершение работы.",3);
                                return Result.Cancelled;
                            }
                            // define an object to represent default value
                            object default_value = System.Reflection.Missing.Value; // object = object type
                                                                                    // create new workbook, which by default contains at least 1 worksheet
                            xlWorkBook = xlApp.Workbooks.Add(default_value);

                        }

                        // initialize 2 worksheet variables, all referring to Sheet1 for the time being
                        xlWorkSheetAllDim = (Microsoft.Office.Interop.Excel.Worksheet)xlWorkBook.Worksheets.get_Item(1);
                        xlWorkSheet = (Microsoft.Office.Interop.Excel.Worksheet)xlWorkBook.Worksheets.get_Item(1);

                        Logger.Log("Разворачиваем окно Excel",1);

                        // maximize workbook window
                        xlApp.ActiveWindow.WindowState = Microsoft.Office.Interop.Excel.XlWindowState.xlMaximized;
                        // show menu bars
                        xlApp.Visible = true;

                        Logger.Log("Импорт текстового файла", 1);

                        // 1. Определяем количество колонок (по заголовку CSV)
                        string[] headerFields = File.ReadAllLines(@"c:\temp\" + curview.Name + ".txt")[0].Split(',');
                        int columnCount = headerFields.Length;

                        // 2. Массив FieldInfo: для каждой колонки тип = текст (2)
                        int[,] fieldInfo = new int[columnCount, 2];
                        for (int i = 0; i < columnCount; i++)
                        {
                            fieldInfo[i, 0] = i + 1;                              // номер столбца (1‑based)
                            fieldInfo[i, 1] = (int)XlColumnDataType.xlTextFormat; // xlTextFormat = 2
                        }

                        // 3. Открываем CSV во временной книге с принудительным текстовым форматом и кодировкой UTF-8
                        xlApp.Workbooks.OpenText(
                            Filename: @"c:\temp\" + curview.Name + ".txt",
                            Origin: 65001,                                       // UTF-8
                            StartRow: 1,
                            DataType: XlTextParsingType.xlDelimited,
                            TextQualifier: XlTextQualifier.xlTextQualifierDoubleQuote,
                            Comma: true,
                            FieldInfo: fieldInfo);

                        Workbook tempBook = xlApp.ActiveWorkbook;   // получаем только что открытую книгу

                        // 4. Копируем данные с временного листа на целевой лист
                        Worksheet tempSheet = tempBook.Worksheets[1];
                        Range usedRange = tempSheet.UsedRange;
                        usedRange.Copy(xlWorkSheet.Range["A1"]);

                        // 5. Закрываем временную книгу без сохранения и освобождаем COM‑объекты
                        tempBook.Close(false);
                        Marshal.ReleaseComObject(tempSheet);
                        Marshal.ReleaseComObject(tempBook);

                        // ===== НОВЫЙ БЛОК: Преобразование текстовых чисел в настоящие числа =====
                        Range dataRange = xlWorkSheet.UsedRange;
                        int rows = dataRange.Rows.Count;
                        int cols = dataRange.Columns.Count;

                        // Начинаем со 2-й строки, чтобы не трогать заголовки
                        for (int r = 2; r <= rows; r++)
                        {
                            for (int c = 1; c <= cols; c++)
                            {
                                Range cell = dataRange.Cells[r, c];
                                if (cell.Value2 != null)
                                {
                                    string cellText = cell.Text.ToString().Trim();
                                    if (!string.IsNullOrEmpty(cellText))
                                    {
                                        // Парсим строку как число с точкой-разделителем
                                        if (double.TryParse(cellText, NumberStyles.Any, CultureInfo.InvariantCulture, out double number))
                                        {
                                            cell.NumberFormat = "General";   // сброс формата, чтобы отображалось как число
                                            cell.Value2 = number;            // присваиваем настоящее число
                                        }
                                    }
                                }
                            }
                        }
                        // =====================================================================

                        Logger.Log("Удаляем текстовый файл", 1);
                        System.IO.File.Delete(@"c:\temp\" + curview.Name + ".txt");


                    }
                    catch (Exception ex) { Logger.Log("Ошибка:" + ex.Message, 4); }
                    #endregion
                    break;
                case 3:
                    #region Сценарий 3: Новая страница в открытой книге Excel
                    try
                    {
                        Logger.Log("Экспортируем вид в Excel",1);
                        curview.Export(@"c:\\temp\", curview.Name + ".txt", opt);
                        System.IO.FileInfo file = new System.IO.FileInfo(@"c:\temp\" + curview.Name + ".txt");

                        // запуск Excel
                        Logger.Log("Попытка запуска / получения активного экземпляра Excel",1);
                        Microsoft.Office.Interop.Excel.Application xlApp;
                        Microsoft.Office.Interop.Excel.Workbook xlWorkBook;
                        Microsoft.Office.Interop.Excel.Worksheet xlWorkSheet;
                        Microsoft.Office.Interop.Excel.Worksheet xlWorkSheetAllDim;
                        Microsoft.Office.Interop.Excel.QueryTable xlQuery;
                        int sheetNum = 1;
                        try
                        {
                            // Получить активный экземпляр Excel
                            xlApp = (Microsoft.Office.Interop.Excel.Application)Marshal.GetActiveObject("Excel.Application");
                            xlWorkBook = xlApp.ActiveWorkbook;
                            if (xlWorkBook != null)
                            {
                                // Добавить новый лист в конец книги
                                xlWorkSheet = (Microsoft.Office.Interop.Excel.Worksheet)xlWorkBook.Worksheets.Add(
                                    After: xlWorkBook.Sheets[xlWorkBook.Sheets.Count]);
                                sheetNum = xlWorkBook.Sheets.Count;
                                xlWorkSheet.Name = "Лист" + sheetNum.ToString();
                            }
                        }
                        catch (COMException)
                        {
                            xlApp = new Microsoft.Office.Interop.Excel.Application();
                            // проверка установлен ли Excel
                            if (xlApp == null)
                            {
                                new InfoWindow280("Ошибка! MS Excel не установлен на данном компьютере.").ShowDialog();
                                Logger.Log("MS Excel не установлен на данном компьютере. Завершение работы.",3);
                                return Result.Cancelled;
                            }
                            // define an object to represent default value
                            object default_value = System.Reflection.Missing.Value; // object = object type
                                                                                    // create new workbook, which by default contains at least 1 worksheet
                            xlWorkBook = xlApp.Workbooks.Add(default_value);

                        }



                        Logger.Log("Получаем страницу Excel",1);
                        // initialize 2 worksheet variables, all referring to Sheet1 for the time being
                        xlWorkSheetAllDim = (Microsoft.Office.Interop.Excel.Worksheet)xlWorkBook.Worksheets.get_Item(sheetNum);
                        xlWorkSheet = (Microsoft.Office.Interop.Excel.Worksheet)xlWorkBook.Worksheets.get_Item(sheetNum);


                        Logger.Log("Разворачиваем окно Excel", 1);

                        // maximize workbook window
                        xlApp.ActiveWindow.WindowState = Microsoft.Office.Interop.Excel.XlWindowState.xlMaximized;
                        // show menu bars
                        xlApp.Visible = true;


                        Logger.Log("Импорт текстового файла", 1);

                        // 1. Определяем количество колонок (по заголовку CSV)
                        string[] headerFields = File.ReadAllLines(@"c:\temp\" + curview.Name + ".txt")[0].Split(',');
                        int columnCount = headerFields.Length;

                        // 2. Массив FieldInfo: для каждой колонки тип = текст (2)
                        int[,] fieldInfo = new int[columnCount, 2];
                        for (int i = 0; i < columnCount; i++)
                        {
                            fieldInfo[i, 0] = i + 1;                              // номер столбца (1‑based)
                            fieldInfo[i, 1] = (int)XlColumnDataType.xlTextFormat; // xlTextFormat = 2
                        }

                        // 3. Открываем CSV во временной книге с принудительным текстовым форматом и кодировкой UTF-8
                        xlApp.Workbooks.OpenText(
                            Filename: @"c:\temp\" + curview.Name + ".txt",
                            Origin: 65001,                                       // UTF-8
                            StartRow: 1,
                            DataType: XlTextParsingType.xlDelimited,
                            TextQualifier: XlTextQualifier.xlTextQualifierDoubleQuote,
                            Comma: true,
                            FieldInfo: fieldInfo);

                        Workbook tempBook = xlApp.ActiveWorkbook;   // получаем только что открытую книгу

                        // 4. Копируем данные с временного листа на целевой лист
                        Worksheet tempSheet = tempBook.Worksheets[1];
                        Range usedRange = tempSheet.UsedRange;
                        usedRange.Copy(xlWorkSheet.Range["A1"]);

                        // 5. Закрываем временную книгу без сохранения и освобождаем COM‑объекты
                        tempBook.Close(false);
                        Marshal.ReleaseComObject(tempSheet);
                        Marshal.ReleaseComObject(tempBook);

                        // ===== НОВЫЙ БЛОК: Преобразование текстовых чисел в настоящие числа =====
                        Range dataRange = xlWorkSheet.UsedRange;
                        int rows = dataRange.Rows.Count;
                        int cols = dataRange.Columns.Count;

                        // Начинаем со 2-й строки, чтобы не трогать заголовки
                        for (int r = 2; r <= rows; r++)
                        {
                            for (int c = 1; c <= cols; c++)
                            {
                                Range cell = dataRange.Cells[r, c];
                                if (cell.Value2 != null)
                                {
                                    string cellText = cell.Text.ToString().Trim();
                                    if (!string.IsNullOrEmpty(cellText))
                                    {
                                        // Парсим строку как число с точкой-разделителем
                                        if (double.TryParse(cellText, NumberStyles.Any, CultureInfo.InvariantCulture, out double number))
                                        {
                                            cell.NumberFormat = "General";   // сброс формата, чтобы отображалось как число
                                            cell.Value2 = number;            // присваиваем настоящее число
                                        }
                                    }
                                }
                            }
                        }
                        // =====================================================================

                        Logger.Log("Удаляем текстовый файл", 1);
                        System.IO.File.Delete(@"c:\temp\" + curview.Name + ".txt");
                    }
                    catch (Exception ex) { Logger.Log("Ошибка:" + ex.Message,4); }
                    #endregion
                    break;
            }

            

            

            Logger.Log("Завершение работы.",5);

            return Result.Succeeded;
        }
            private int xlRowLast(Microsoft.Office.Interop.Excel.Worksheet w_s)
        {
            // return last used row number of worksheet
            return w_s.Cells.SpecialCells(Microsoft.Office.Interop.Excel.XlCellType.xlCellTypeLastCell, Type.Missing).Row;
        }

    }



}   
