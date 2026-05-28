using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Visual;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using TNovCommon;

namespace TNovViewsSheets
{
    
    
    
    
    [Transaction(TransactionMode.Manual)]
    public class SheetsOld : IExternalCommand
    {
        private TNovSheet GetTNovSheet(in ViewSheet sheet, in Guid sheetSetParamGuid, in bool sSetParamExist)
        {
            
            string sNumber = sheet.get_Parameter(BuiltInParameter.SHEET_NUMBER).AsString();
            Logger.Log("      Номер "+ sNumber,2);
            string sSet = "без комплекта";
            if (sSetParamExist) sSet = sheet.get_Parameter(sheetSetParamGuid).AsString();
            if (sSet == "----" || sSet == null || sSet.Length == 0) sSet = "без комплекта";
            Logger.Log("      Набор " + sSet,2);
            

            string sSet2 = "bb";
            if (sSet.Length > 2)
            {
                if (sSet.Contains(".СО"))
                {
                    string s = sSet.Replace(".СО", "");
                    sSet2 = sSet.Replace(".СО", "");
                    sSet2 = sSet2.Remove(sSet2.Length - 1);
                    sSet2 = sSet2 + '.' + s[s.Length - 1];
                    sSet2 = sSet2 + ".СО";
                }
                else
                {
                    sSet2 = sSet.Remove(sSet.Length - 1);
                    sSet2 = sSet2 + '.' + sSet[sSet.Length - 1];
                }

            }
            Logger.Log("      sSet2 " + sSet2, 2);

            //Получаем "чистый" номер
            string cleanNumber = sNumber.Replace(sSet + "-", ""); //удаляем код комплекта с дефисом
            cleanNumber = cleanNumber.Replace(sSet, ""); //удаляем код комплекта
            cleanNumber = cleanNumber.Replace(sSet2, ""); //удаляем код комплекта - обработка случаев несоответствий
                                                          //кода и префикса в Номере листа (ОВ.2 - ОВ2 и т.п.)
            cleanNumber = cleanNumber.TrimStart(' '); //удаляем пробелы в начале
            cleanNumber = Regex.Replace(cleanNumber, @"\p{Cf}", string.Empty); //удаляем все невидимые символы из строки
            cleanNumber = cleanNumber.Replace("АР1 ", ""); cleanNumber = cleanNumber.Replace("АР.1 ", ""); //обработка несовпадений префикса и комплекта
            cleanNumber = cleanNumber.Replace("АР1-", ""); cleanNumber = cleanNumber.Replace("АР.1-", "");
            cleanNumber = cleanNumber.Replace("АР2 ", ""); cleanNumber = cleanNumber.Replace("АР.2 ", "");
            cleanNumber = cleanNumber.Replace("АР2-", ""); cleanNumber = cleanNumber.Replace("АР.2-", "");
            cleanNumber = cleanNumber.Replace("АР ", ""); cleanNumber = cleanNumber.Replace("КР ", "");
            cleanNumber = cleanNumber.Replace("АР-", ""); cleanNumber = cleanNumber.Replace("КР-", "");
            cleanNumber = cleanNumber.Replace("КЖ2 ", ""); cleanNumber = cleanNumber.Replace("КЖ.2 ", "");
            cleanNumber = cleanNumber.Replace("КЖ2-", ""); cleanNumber = cleanNumber.Replace("КЖ.2-", "");
            cleanNumber = cleanNumber.Replace("КЖ1 ", ""); cleanNumber = cleanNumber.Replace("КЖ.1 ", "");
            cleanNumber = cleanNumber.Replace("КЖ1-", ""); cleanNumber = cleanNumber.Replace("КЖ.1-", "");
            cleanNumber = cleanNumber.Replace("КЖ0 ", ""); cleanNumber = cleanNumber.Replace("КЖ.0 ", "");
            cleanNumber = cleanNumber.Replace("КЖ0-", ""); cleanNumber = cleanNumber.Replace("КЖ.0-", "");
            cleanNumber = cleanNumber.Replace("КЖ ", "");
            cleanNumber = cleanNumber.Replace("КЖ-", "");
            cleanNumber = cleanNumber.Replace("QAZ", ""); 
            Logger.Log("      cleanNumber " + cleanNumber, 2);

            int num = 9999;
            bool canRenum = int.TryParse(cleanNumber, out num);
            Logger.Log("      num " + num.ToString(),2);

            //Формируем TNovSheet
            TNovSheet tNovSheet = new TNovSheet()
            {
                TNovSheetNumber = sNumber,
                TNovSheetSet = sSet,
                TNovSheetCleanNumber = cleanNumber,
                TNovSheetNewNumber = cleanNumber,
                TNovSheetCanRenum = canRenum,
                TNovSheetNumericNumber = num,
                TNovSheetName = sheet.Name,
            };
            return tNovSheet;
        }
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            string TNovClassName = "Менеджер листов"; DateTime dateTime = DateTime.Now; string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            //подключение приложения и документа
            if (RevitAPI.UiApplication == null) { RevitAPI.Initialize(commandData); }
            UIDocument uidoc = RevitAPI.UiDocument; Document doc = RevitAPI.Document;
            UIApplication uiApp = RevitAPI.UiApplication; Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application;
            
            //проверка подключения, запись в журнал
            if(ServerUtils.CheckConnection(TNovClassName, TNovVersion)==false) return Result.Failed;

            // создание log - файла
            Logger.Initialize(TNovClassName,dateTime,TNovVersion);
            

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
                if (qok != null && qok == true) { Logger.TurnOffExtendedLogs(); } else Logger.Log( "Расширенные логи вкл", 2);
            }

            
            Logger.Log("Получаем листы",1);
            //получаем элементы

            List<ViewSheet> sheets = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Sheets)
            .WhereElementIsNotElementType()
            .Cast<ViewSheet>()
            .ToList();

            //параметры
            Guid NSheetNumberParamGuid = new Guid("b6e73342-b6cd-42c5-86c5-64b04b5b88de"); //N_Ш.НомерЛиста
            Guid adskSheetSetParamGuid = new Guid("e1b06433-f527-403c-8986-af9a01e6be7f"); //A_Комплект чертежей
            string symbol = "QAZ";
            string symbol1 = "\u202a";

            Logger.Log("Диалоговое окно", 1);
            //Стартовое окно
            var viewModel = new SheetsStartViewModel();
            // Десериализация
            bool forProject = true;
            json js = new json(in TNovClassName, in forProject, out bool canserialize, out string jsonpath);
            if (canserialize)
            {
                viewModel = JsonConvert.DeserializeObject<SheetsStartViewModel>(File.ReadAllText(jsonpath));
                Logger.Log("Десериализация прошла успешно",1);
            }
            //окно
            var wpfview = new SheetsStartWPF(viewModel);
            viewModel.CloseRequest += (s, e) => wpfview.Close();
            bool? ok = wpfview.ShowDialog();
            if (ok != null && ok == true) { }
            else { Logger.Log("Запуск отменен пользователем. Завершение работы.", 3); return Result.Cancelled; }
            //Сериализация
            try
            {
                File.WriteAllText(jsonpath, JsonConvert.SerializeObject(viewModel));
                Logger.Log("Сериализация прошла успешно",1);
            }
            catch (Exception ex) { Logger.Log("Ошибка при сериализации: " + ex.Message,4); }

            //проверка наличия листов
            if (sheets.Count == 0) 
            {
                new InfoWindow280("В данной модели отсутствуют листы!").ShowDialog();
                Logger.Log("Листы в модели отсутствуют. Завершение работы.", 3); return Result.Cancelled;
            }

            //Автовставка невид символов перед запуском основного окна
            bool addPrefix = viewModel.prefix;

            //проверяем наличие параметров
            Element elem = doc.GetElement(sheets.First().Id);
            bool sSetParamExist = Param.ParamExistByGuid(adskSheetSetParamGuid, elem);
            bool sCustomNumberParamExist = Param.ParamExistByGuid(NSheetNumberParamGuid, elem);


            Logger.Log("Создаем список элементов класса TNovSheet",1);
            
            //Список TNovSheet
            List<TNovSheet> tNovSheets = new List<TNovSheet>();

            foreach (var sheet in sheets)
            {
                string sSet = "без комплекта";
                if (sSetParamExist) sSet = sheet.get_Parameter(adskSheetSetParamGuid).AsString();
                if (sSet == "----"|| sSet == null || sSet.Length == 0) sSet = "без комплекта"; //continue; 
                Logger.Log("   Лист " + sheet.get_Parameter(BuiltInParameter.SHEET_NUMBER)+" "+sheet.Name, 2);
                TNovSheet tNovSheet = GetTNovSheet(sheet,adskSheetSetParamGuid,sSetParamExist);

                tNovSheets.Add(tNovSheet);
            }
            Logger.Log("Сортируем tNovSheets по комплекту",1);
            var tNovSheetsSortedBySet = from tNovSheet in tNovSheets 
                                        orderby tNovSheet.TNovSheetSet
                                        select tNovSheet;

            var tNovSheetSets = from tNovSheet in tNovSheetsSortedBySet //список комплектов
                                group tNovSheet by tNovSheet.TNovSheetSet;

            
            //список номеров для проверок
            List<string> newNums = new List<string>();

            using (TransactionGroup group = new TransactionGroup(RevitAPI.Document, "TNov - Менеджер листов (Комплекты)"))
            {
                group.Start();

                //транзакция 1
                using (Transaction t = new Transaction(doc))
                {
                    Logger.Log("Назначаем чистые номера с временными префиксами и суффиксами (транзакция 1)",1);
                    t.Start("TNov - Менеджер листов 1 этап");

                    int prefixCount = 0; int suffixCount = 1; //счетчик добавления префиксов/суффиксов по комплектам

                    foreach (var tNovSheetSet in tNovSheetSets)
                    {
                        Logger.Log("> Комплект " + tNovSheetSet.First().TNovSheetSet, 2);
                        if (tNovSheetSet.First().TNovSheetSet.Contains(".СО")) prefixCount++; //+префикс для СО

                        string suffix = "";
                        if (suffixCount == 1) suffix = symbol;
                        else
                        {
                            for (int i = 0; i < suffixCount; i++)
                            {
                                suffix = suffix + symbol;
                            }
                        }
                        Logger.Log("   суффикс: " + suffix, 2);

                        foreach (var tNovSheet in tNovSheetSet)
                        {
                            Logger.Log("   Лист tNovSheet " + tNovSheet.TNovSheetNumber, 2);
                            bool sheetFound = false;
                            foreach (var sheet in sheets)
                            {
                                string sNumber = sheet.get_Parameter(BuiltInParameter.SHEET_NUMBER).AsString();
                                if (tNovSheet.TNovSheetNumber == sNumber)
                                {
                                    sheetFound = true;
                                    Logger.Log("      найден лист Revit "+sheet.Id.IntegerValue.ToString()+" : " + sNumber + " "+sheet.Name, 2);
                                    string newNumber = tNovSheet.TNovSheetCleanNumber;
                                    if (prefixCount > 0)
                                    {
                                        if (prefixCount == 1)
                                        {
                                            newNumber = symbol + newNumber;
                                        }
                                        else
                                        {
                                            for (int i = 0; i < prefixCount; i++)
                                            {
                                                newNumber = symbol + newNumber;
                                            }
                                        }
                                    }
                                    
                                    

                                    if (newNumber.Contains("."))//обработка ошибки: листы ".х" улетают выше в диспетчере
                                                                //(например, 8.1 раньше чем 8)
                                    {
                                        newNumber = newNumber.Replace(".", suffix + ".");
                                    }
                                    newNumber = newNumber + suffix;
                                        
                                    
                                    if (newNums.Count > 0) //проверка существует ли такой номер
                                    {
                                        for (int i = 0; i < newNums.Count; i++)
                                        {
                                            if (newNums[i] == newNumber) 
                                            { 
                                                newNumber = "0" + newNumber;
                                            }
                                        }
                                    }

                                    Logger.Log("      целевой новый номер: " + newNumber, 2);

                                    try
                                    {
                                        Parameter sheetNumberParameter = sheet.get_Parameter(BuiltInParameter.SHEET_NUMBER);
                                        sheetNumberParameter.Set(newNumber); //Номер
                                        newNums.Add(newNumber);
                                        Logger.Log("      назначен Номер",2);
                                        if (sCustomNumberParamExist)
                                        {
                                            Parameter sheetCustomNumberParameter = sheet.get_Parameter(NSheetNumberParamGuid); //Шномер
                                            sheetCustomNumberParameter.Set(tNovSheet.TNovSheetCleanNumber);
                                            Logger.Log("      назначен Ш.Номер", 2);
                                        }
                                    }
                                    catch (Exception e) { Logger.Log("   Лист " + sNumber + " ошибка: " + e.Message,4); }
                                    break;
                                }
                                
                            }
                            if(!sheetFound) Logger.Log("      Ошибка! Не найден лист Revit", 4);
                        }


                        suffixCount++; //+суффикс для последующих комплектов
                    }

                    Logger.Log("Закрываем транзакцию 1",1);
                    t.Commit();
                }

                
                //обновленная коллекция листов
                Logger.Log("Обновляем листы", 1);

                List<ViewSheet> sheets1 = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Sheets)
                .WhereElementIsNotElementType()
                .Cast<ViewSheet>()
                .ToList();


                //транзакция 2
                using (Transaction t2 = new Transaction(doc))
                {
                    Logger.Log("Назначаем чистые номера с невидимыми префиксами и суффиксами (транзакция 2)", 1);
                    t2.Start("TNov - Менеджер листов 2 этап");


                    foreach (var sheet in sheets1)
                    {
                        Logger.Log("   лист Revit " + sheet.Id.IntegerValue.ToString() + " : " + 
                            sheet.get_Parameter(BuiltInParameter.SHEET_NUMBER).AsString() + " " + sheet.Name, 2);

                        string sSet = "без комплекта";
                        if (sSetParamExist) sSet = sheet.get_Parameter(adskSheetSetParamGuid).AsString();
                        if (sSet == "----" || sSet == null || sSet.Length == 0) sSet = "без комплекта"; //continue;

                        Parameter sheetNumberParameter = sheet.get_Parameter(BuiltInParameter.SHEET_NUMBER);
                        string sNumber = sheetNumberParameter.AsString();
                        string newNumber = sNumber.Replace(symbol, symbol1);

                        string sheetSet = "";

                        if (sSetParamExist) sheetSet = sheet.get_Parameter(adskSheetSetParamGuid).AsString();

                        if (addPrefix) newNumber = sheetSet + " " + newNumber;

                        try
                        {
                            sheetNumberParameter.Set(newNumber);
                            Logger.Log("      назначен Номер " + newNumber, 2);
                        }
                        catch (Exception e) { Logger.Log("   Лист " + sNumber + " ошибка: " + e.Message, 4); }

                    }


                    Logger.Log("Закрываем транзакцию 2", 1);
                    t2.Commit();
                }

                //обновленная коллекция листов
                Logger.Log("Обновляем листы", 1);

                List<ViewSheet> sheets2 = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Sheets)
                .WhereElementIsNotElementType()
                .Cast<ViewSheet>()
                .ToList();


                //Формирование комплектов на печать

                Logger.Log("Получаем существующие наборы на печать", 1);
                ICollection<Element> printSets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheetSet)).ToElements();

                List<string> sets = new List<string>();
                foreach (var tNovSheetSet in tNovSheetSets)
                {
                    string setName = tNovSheetSet.First().TNovSheetSet;
                    if (!setName.Contains(".СО")) sets.Add(setName);
                }

                ICollection<ElementId> printSetsToDelete = new List<ElementId>();
                foreach (var set in sets)
                {
                    foreach (var printSet in printSets)
                    {
                        if (printSet.Name == set) printSetsToDelete.Add(printSet.Id);
                    }
                }

                if (printSetsToDelete.Count > 0)
                {
                    //транзакция 3
                    using (Transaction t3 = new Transaction(doc))
                    {
                        Logger.Log("Удаляем используемые комплекты на печать (транзакция 3)", 1);
                        t3.Start("TNov - Менеджер листов 3 этап");

                        doc.Delete(printSetsToDelete);

                        Logger.Log("Закрываем транзакцию 3", 1);
                        t3.Commit();
                    }
                }

                Logger.Log("Cоздаем новые комплекты на печать", 1);

                //транзакция 4
                using (Transaction t4 = new Transaction(doc))
                {
                    Logger.Log("Добавляем новые комплекты на печать (транзакция 4)", 1);
                    t4.Start("TNov - Менеджер листов 4 этап");

                    //создаем новые комплекты на печать
                    foreach (var set in sets)
                    {
                        ViewSet viewSet = new ViewSet();
                        foreach (var sheet in sheets2)
                        {
                            string sSet = "-";
                            if (sSetParamExist) sSet = sheet.get_Parameter(adskSheetSetParamGuid).AsString();
                            if (sSet == "----" || sSet == null || sSet.Length == 0) sSet = "-"; //continue;
                            sSet = sSet.Replace(".СО", "");
                            if (sSet.Equals(set)) viewSet.Insert(sheet); //исправлено - было "начинается с"
                        }
                        PrintManager printManager = doc.PrintManager;
                        printManager.PrintRange = PrintRange.Select;
                        ViewSheetSetting viewSheetSetting = printManager.ViewSheetSetting;
                        try
                        {
                            viewSheetSetting.CurrentViewSheetSet.Views = viewSet;
                            viewSheetSetting.SaveAs(set);
                            Logger.Log("Набор успешно создан: " + set,1);
                        }
                        catch (Exception ex) { Logger.Log(set + " ошибка: " + ex.Message,4); }
                    }
                    Logger.Log("Закрываем транзакцию 4", 1);
                    t4.Commit();
                }

                group.Assimilate();
            }

            //выход из команды, если не нужен нумератор
            if (viewModel.selection == 1) { Logger.Log("Нумератор не нужен. Завершение работы.", 5); return Result.Succeeded; }

            //Нумератор

            List<ViewSheet> sheetsToNum = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Sheets)
            .WhereElementIsNotElementType()
            .Cast<ViewSheet>()
            .ToList();

            //Список TNovSheet
            List<TNovSheet> tNovSheetsToNum = new List<TNovSheet>();
            foreach (var sheet in sheetsToNum)
            {
                string sSet = "без комплекта";
                if (sSetParamExist) sSet = sheet.get_Parameter(adskSheetSetParamGuid).AsString();
                if (sSet == "----" || sSet == null || sSet.Length == 0) sSet = "без комплекта"; //continue;

                TNovSheet tNovSheet = GetTNovSheet(sheet, adskSheetSetParamGuid, sSetParamExist);

                if(tNovSheet.TNovSheetCanRenum) tNovSheetsToNum.Add(tNovSheet); //в нумератор попадут только те листы,
                                                                                //которые можно перенумеровать
            }
            TNovSheet[] tNovSheetArray = tNovSheetsToNum.ToArray(); 
                        
            //Нумератор

            //окно (ViewModel не используется)
            var wpfview1 = new SheetNumWPF(tNovSheetArray);
            bool? ok1 = wpfview1.ShowDialog();
            if (ok1 != null && ok1 == true) { } else { Logger.Log("Запуск отменен пользователем. Завершение работы.", 3); return Result.Succeeded; }

            //Обработка коллекции после окна Нумератора
            List<TNovSheet> numberedSheets = wpfview1.ResultCollection.ToList();

            //список номеров для проверок
            List<string> newNums2 = new List<string>();

            using (TransactionGroup group2 = new TransactionGroup(RevitAPI.Document, "TNov - Менеджер листов (Нумератор)"))
            {
                group2.Start();

                //транзакция 5
                using (Transaction t5 = new Transaction(doc))
                {
                    Logger.Log("Назначаем новые номера с временным суффиксом (транзакция 5)", 1);
                    t5.Start("TNov - Менеджер листов 5 этап");

                    foreach (var tNovSheet in numberedSheets)
                    {
                        //новый номер, распарсенный в строку
                        string newNumber = tNovSheet.TNovSheetNumericNumber.ToString();

                        foreach (var sheet in sheetsToNum)
                        {
                            string sNumber = sheet.get_Parameter(BuiltInParameter.SHEET_NUMBER).AsString();
                            if (tNovSheet.TNovSheetNumber == sNumber) //найден лист с тем же Номером листа
                            {
                                string sSet = "без комплекта";
                                if (sSetParamExist) sSet = sheet.get_Parameter(adskSheetSetParamGuid).AsString();

                                string sSet2 = "bb";
                                if (sSet.Length > 2)
                                {
                                    if (sSet.Contains(".СО"))
                                    {
                                        string s = sSet.Replace(".СО", "");
                                        sSet2 = sSet.Replace(".СО", "");
                                        sSet2 = sSet2.Remove(sSet2.Length - 1);
                                        sSet2 = sSet2 + '.' + s[s.Length - 1];
                                        sSet2 = sSet2 + ".СО";
                                    }
                                    else
                                    {
                                        sSet2 = sSet.Remove(sSet.Length - 1);
                                        sSet2 = sSet2 + '.' + sSet[sSet.Length - 1];
                                    }

                                }

                                //Получаем "чистый" номер
                                string cleanNumber = sNumber.Replace(sSet + "-", ""); //удаляем код комплекта с дефисом
                                cleanNumber = cleanNumber.Replace(sSet, ""); //удаляем код комплекта
                                cleanNumber = cleanNumber.Replace(sSet2, ""); //удаляем код комплекта - обработка случаев несоответствий
                                                                              //кода и префикса в Номере листа (ОВ.2 - ОВ2 и т.п.)
                                cleanNumber = cleanNumber.TrimStart(' '); //удаляем пробелы в начале
                                cleanNumber = Regex.Replace(cleanNumber, @"\p{Cf}", string.Empty); //удаляем все невидимые символы из строки

                                //в Номере заменяем "чистый" номер на новый номер, распарсенный в строку
                                string newNumberToSet = sNumber.Replace(cleanNumber, newNumber);

                                //а также добавляем к нему временный суффикс
                                newNumberToSet = newNumberToSet + "ZAQ";

                                try
                                {
                                    Parameter sheetNumberParameter = sheet.get_Parameter(BuiltInParameter.SHEET_NUMBER);
                                    sheetNumberParameter.Set(newNumberToSet); //Номер
                                    newNums2.Add(newNumberToSet);
                                    Logger.Log("   Лист " + sNumber + ": назначен Номер " + newNumberToSet, 2);
                                    if (sCustomNumberParamExist)
                                    {
                                        Parameter sheetCustomNumberParameter = sheet.get_Parameter(NSheetNumberParamGuid); //Шномер
                                        sheetCustomNumberParameter.Set(newNumber);
                                    }
                                }
                                catch (Exception e) { Logger.Log("   Лист " + sNumber + " ошибка: " + e.Message,4); }

                                break;
                            }
                        }
                    }

                    Logger.Log("Закрываем транзакцию 5", 1);
                    t5.Commit();
                }

                //обновленная коллекция листов
                Logger.Log("Обновляем листы", 1);

                List<ViewSheet> sheets5 = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Sheets)
                .WhereElementIsNotElementType()
                .Cast<ViewSheet>()
                .ToList();

                //транзакция 6
                using (Transaction t6 = new Transaction(doc))
                {
                    Logger.Log("Назначаем новые номера (транзакция 6)", 1);
                    t6.Start("TNov - Менеджер листов 6 этап");


                    foreach (var sheet in sheets5)
                    {
                        string sSet = "без комплекта";
                        if (sSetParamExist) sSet = sheet.get_Parameter(adskSheetSetParamGuid).AsString();
                        if (sSet == "----") sSet = "без комплекта"; //continue;

                        Parameter sheetNumberParameter = sheet.get_Parameter(BuiltInParameter.SHEET_NUMBER);
                        string sNumber = sheetNumberParameter.AsString();
                        string newNumber = sNumber.Replace("ZAQ", "");

                        string sheetSet = "";

                        if (sSetParamExist) sheetSet = sheet.get_Parameter(adskSheetSetParamGuid).AsString();

                        try
                        {
                            sheetNumberParameter.Set(newNumber);
                            Logger.Log("   Лист " + sNumber + ": назначен Номер " + newNumber,2);
                        }
                        catch (Exception e) { Logger.Log("   Лист " + sNumber + " ошибка: " + e.Message,4); }

                    }


                    Logger.Log("Закрываем транзакцию 6", 1);
                    t6.Commit();
                }

                group2.Assimilate();
            }


            Logger.Log("Завершение работы.",5);
            return Result.Succeeded;
        }
    }
}
