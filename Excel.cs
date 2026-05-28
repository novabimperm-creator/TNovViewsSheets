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

namespace TNovViewsSheets
{
    
    [Transaction(TransactionMode.Manual)]
    public class Excel : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            System.IO.Directory.CreateDirectory(@"c:\\temp\");
            string TNovClassName = "Excel"; DateTime dateTime = DateTime.Now; string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            //подключение приложения и документа
            if (RevitAPI.UiApplication == null) { RevitAPI.Initialize(commandData); }
            UIDocument uidoc = RevitAPI.UiDocument; Document doc = RevitAPI.Document;
            UIApplication uiApp = RevitAPI.UiApplication; Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application;
            
            //проверка подключения, запись в журнал
            if(ServerUtils.CheckConnection(TNovClassName, TNovVersion)==false) return Result.Failed;

            // создание log - файла
            Logger.Initialize(TNovClassName,dateTime,TNovVersion);
            

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

            switch (scenario)
            {
                case 1:
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

                        Logger.Log("Импорт текстового файла",1);

                        // import txt file into worksheet starting at cell at column A
                        xlQuery = xlWorkSheet.QueryTables.Add(
                            "TEXT;" + @"c:\temp\" + curview.Name + ".txt",
                            xlWorkSheet.get_Range("A1", "A" + xlRowLast(xlWorkSheet)));
                        xlWorkSheet.QueryTables[1].Name = curview.Name;
                        xlWorkSheet.QueryTables[1].FieldNames = true;
                        xlWorkSheet.QueryTables[1].RowNumbers = false;
                        xlWorkSheet.QueryTables[1].FillAdjacentFormulas = false;
                        xlWorkSheet.QueryTables[1].PreserveFormatting = true;
                        xlWorkSheet.QueryTables[1].RefreshOnFileOpen = false;
                        xlWorkSheet.QueryTables[1].RefreshStyle = XlCellInsertionMode.xlInsertDeleteCells;
                        xlWorkSheet.QueryTables[1].SavePassword = false;
                        xlWorkSheet.QueryTables[1].SaveData = true;
                        xlWorkSheet.QueryTables[1].AdjustColumnWidth = true;
                        xlWorkSheet.QueryTables[1].RefreshPeriod = 0;
                        xlWorkSheet.QueryTables[1].TextFilePromptOnRefresh = false;
                        xlWorkSheet.QueryTables[1].TextFilePlatform = 65001;
                        xlWorkSheet.QueryTables[1].TextFileStartRow = 1;
                        xlWorkSheet.QueryTables[1].TextFileParseType = XlTextParsingType.xlDelimited;
                        xlWorkSheet.QueryTables[1].TextFileTextQualifier = XlTextQualifier.xlTextQualifierDoubleQuote;
                        xlWorkSheet.QueryTables[1].TextFileConsecutiveDelimiter = false;
                        xlWorkSheet.QueryTables[1].TextFileTabDelimiter = false;
                        xlWorkSheet.QueryTables[1].TextFileSemicolonDelimiter = false;
                        xlWorkSheet.QueryTables[1].TextFileCommaDelimiter = true;
                        xlWorkSheet.QueryTables[1].TextFileSpaceDelimiter = false;
                        /*xlWorkSheet.QueryTables[1].TextFileColumnDataTypes = columnDataTypes;*/
                        xlQuery.RefreshStyle = Microsoft.Office.Interop.Excel.XlCellInsertionMode.xlInsertEntireRows;
                        xlQuery.Refresh(false); // false means refresh but not return until refresh is finished 
                        xlQuery.Delete(); // delete the query

                        Logger.Log("Удаляем текстовый файл",1);

                        // delete txt file
                        System.IO.File.Delete(@"c:\temp\" + curview.Name + ".txt");
                    }
                    catch (Exception ex) { Logger.Log("Ошибка:" + ex.Message,4); }
                    break;
                case 2:
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

                        Logger.Log("Импорт текстового файла",1);

                        // import txt file into worksheet starting at cell at column A
                        xlQuery = xlWorkSheet.QueryTables.Add(
                            "TEXT;" + @"c:\temp\" + curview.Name + ".txt",
                            xlWorkSheet.get_Range("A1", "A" + xlRowLast(xlWorkSheet)));
                        xlWorkSheet.QueryTables[1].Name = curview.Name;
                        xlWorkSheet.QueryTables[1].FieldNames = true;
                        xlWorkSheet.QueryTables[1].RowNumbers = false;
                        xlWorkSheet.QueryTables[1].FillAdjacentFormulas = false;
                        xlWorkSheet.QueryTables[1].PreserveFormatting = true;
                        xlWorkSheet.QueryTables[1].RefreshOnFileOpen = false;
                        xlWorkSheet.QueryTables[1].RefreshStyle = XlCellInsertionMode.xlInsertDeleteCells;
                        xlWorkSheet.QueryTables[1].SavePassword = false;
                        xlWorkSheet.QueryTables[1].SaveData = true;
                        xlWorkSheet.QueryTables[1].AdjustColumnWidth = true;
                        xlWorkSheet.QueryTables[1].RefreshPeriod = 0;
                        xlWorkSheet.QueryTables[1].TextFilePromptOnRefresh = false;
                        xlWorkSheet.QueryTables[1].TextFilePlatform = 65001;
                        xlWorkSheet.QueryTables[1].TextFileStartRow = 1;
                        xlWorkSheet.QueryTables[1].TextFileParseType = XlTextParsingType.xlDelimited;
                        xlWorkSheet.QueryTables[1].TextFileTextQualifier = XlTextQualifier.xlTextQualifierDoubleQuote;
                        xlWorkSheet.QueryTables[1].TextFileConsecutiveDelimiter = false;
                        xlWorkSheet.QueryTables[1].TextFileTabDelimiter = false;
                        xlWorkSheet.QueryTables[1].TextFileSemicolonDelimiter = false;
                        xlWorkSheet.QueryTables[1].TextFileCommaDelimiter = true;
                        xlWorkSheet.QueryTables[1].TextFileSpaceDelimiter = false;
                        /*xlWorkSheet.QueryTables[1].TextFileColumnDataTypes = columnDataTypes;*/
                        xlQuery.RefreshStyle = Microsoft.Office.Interop.Excel.XlCellInsertionMode.xlInsertEntireRows;
                        xlQuery.Refresh(false); // false means refresh but not return until refresh is finished 
                        xlQuery.Delete(); // delete the query

                        Logger.Log("Удаляем текстовый файл",1);

                        // delete txt file
                        System.IO.File.Delete(@"c:\temp\" + curview.Name + ".txt");


                    }
                    catch (Exception ex) { Logger.Log("Ошибка:" + ex.Message, 4); }
                    break;

                case 3:
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

                        // import txt file into worksheet starting at cell at column A
                        xlQuery = xlWorkSheet.QueryTables.Add(
                            "TEXT;" + @"c:\temp\" + curview.Name + ".txt",
                            xlWorkSheet.get_Range("A1", "A" + xlRowLast(xlWorkSheet)));
                        xlWorkSheet.QueryTables[1].Name = curview.Name;
                        xlWorkSheet.QueryTables[1].FieldNames = true;
                        xlWorkSheet.QueryTables[1].RowNumbers = false;
                        xlWorkSheet.QueryTables[1].FillAdjacentFormulas = false;
                        xlWorkSheet.QueryTables[1].PreserveFormatting = true;
                        xlWorkSheet.QueryTables[1].RefreshOnFileOpen = false;
                        xlWorkSheet.QueryTables[1].RefreshStyle = XlCellInsertionMode.xlInsertDeleteCells;
                        xlWorkSheet.QueryTables[1].SavePassword = false;
                        xlWorkSheet.QueryTables[1].SaveData = true;
                        xlWorkSheet.QueryTables[1].AdjustColumnWidth = true;
                        xlWorkSheet.QueryTables[1].RefreshPeriod = 0;
                        xlWorkSheet.QueryTables[1].TextFilePromptOnRefresh = false;
                        xlWorkSheet.QueryTables[1].TextFilePlatform = 65001;
                        xlWorkSheet.QueryTables[1].TextFileStartRow = 1;
                        xlWorkSheet.QueryTables[1].TextFileParseType = XlTextParsingType.xlDelimited;
                        xlWorkSheet.QueryTables[1].TextFileTextQualifier = XlTextQualifier.xlTextQualifierDoubleQuote;
                        xlWorkSheet.QueryTables[1].TextFileConsecutiveDelimiter = false;
                        xlWorkSheet.QueryTables[1].TextFileTabDelimiter = false;
                        xlWorkSheet.QueryTables[1].TextFileSemicolonDelimiter = false;
                        xlWorkSheet.QueryTables[1].TextFileCommaDelimiter = true;
                        xlWorkSheet.QueryTables[1].TextFileSpaceDelimiter = false;
                        /*xlWorkSheet.QueryTables[1].TextFileColumnDataTypes = columnDataTypes;*/
                        xlQuery.RefreshStyle = Microsoft.Office.Interop.Excel.XlCellInsertionMode.xlInsertEntireRows;
                        xlQuery.Refresh(false); // false means refresh but not return until refresh is finished 
                        xlQuery.Delete(); // delete the query

                        xlApp.WindowState = XlWindowState.xlNormal; xlApp.Visible = true;

                        if (xlApp != null)
                            Marshal.ReleaseComObject(xlApp);
                        if (xlWorkBook != null)
                        {
                            Marshal.ReleaseComObject(xlWorkBook);
                            Marshal.ReleaseComObject(xlWorkSheet);
                        }

                        Logger.Log("Удаляем текстовый файл", 1);

                        // delete txt file
                        System.IO.File.Delete(@"c:\temp\" + curview.Name + ".txt");
                    }
                    catch (Exception ex) { Logger.Log("Ошибка:" + ex.Message,4); }
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
