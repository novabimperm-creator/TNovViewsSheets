using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using TNovCommon;

namespace TNovViewsSheets
{
    public class CopyModeSettings
    {
        public bool CopySchedules;
        public bool CopyLegends;
    }
    public partial class CopyModeControl : UserControl
    {
        private UIDocument _uidoc;
        private Document _doc;
        private ObservableCollection<SheetInfo> _sheets;
        private ListView _sheetsListView;
        Guid NSheetNumberParamGuid = new Guid("b6e73342-b6cd-42c5-86c5-64b04b5b88de"); //N_Ш.НомерЛиста
        Guid adskSheetSetParamGuid = new Guid("e1b06433-f527-403c-8986-af9a01e6be7f"); //A_Комплект чертежей
        string _jsonpath;

        public CopyModeControl(UIDocument uidoc, ObservableCollection<SheetInfo> sheets, ListView sheetsListView)
        {
            InitializeComponent();

            //окно
            var settings = new CopyModeSettings();
            // Десериализация
            bool forProject = false;
            json js = new json("Менеджер листов. Копирование листа", in forProject, out bool canserialize, out _jsonpath);
            if (canserialize)
            {
                settings = JsonConvert.DeserializeObject<CopyModeSettings>(File.ReadAllText(_jsonpath));
            }

            _uidoc = uidoc;
            _doc = uidoc.Document;
            _sheets = sheets;
            _sheetsListView = sheetsListView;

            copySchedules.IsChecked = settings.CopySchedules;
            copyLegends.IsChecked = settings.CopyLegends;
        }

        private void CopySheet_Click(object sender, RoutedEventArgs e)
        {
            var selectedInfo = _sheetsListView.SelectedItem as SheetInfo;
            if (selectedInfo == null)
            {
                new InfoWindow280("Выберите исходный лист в списке справа.").ShowDialog();
                return;
            }

            int nextNum = GetNextSheetNumber(selectedInfo.SheetSet);

            ViewSheet sourceSheet = _doc.GetElement(selectedInfo.Id) as ViewSheet;
            if (sourceSheet == null) return;

            string newNumber = nextNum.ToString();
            string newName = TbNewSheetName.Text.Trim();


            FilteredElementCollector collector = new FilteredElementCollector(_doc, sourceSheet.Id);
            FamilyInstance titleBlockInstance = collector
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .FirstOrDefault();

            if (titleBlockInstance == null)
            {
                new InfoWindow280("На исходном листе не найден заголовок (штамп).").ShowDialog();
                return;
            }

            ElementId titleBlockTypeId = titleBlockInstance.GetTypeId();
            FamilySymbol titleBlockType = _doc.GetElement(titleBlockTypeId) as FamilySymbol;
            if (titleBlockType == null || !titleBlockType.IsActive)
            {
                new InfoWindow280("Тип штампа исходного листа неактивен или недоступен.").ShowDialog();
                return;
            }

            //спецификации
            FilteredElementCollector scheduleInstances = new FilteredElementCollector(_doc, sourceSheet.Id)
                .OfClass(typeof(ScheduleSheetInstance));

            string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();

            Logger.Initialize("Менеджер листов. Копирование листа", DateTime.Now, TNovVersion);

            using (Transaction trans = new Transaction(_doc, "Копировать лист"))
            {
                trans.Start();
                ViewSheet newSheet = ViewSheet.Create(_doc, titleBlockTypeId);
                try
                {
                    newSheet.SheetNumber = (nextNum).ToString();
                }
                catch (Exception ex) //на всякий случай
                {
                    Logger.Log($"Ошибка: {ex.Message}. Пробуем назначить номер 9999", 4);
                    nextNum = 1000;
                    newSheet.SheetNumber = (nextNum).ToString();
                }
                Logger.Log($"Создан лист {(nextNum).ToString()}", 1);
                if (!string.IsNullOrEmpty(newName))
                    newSheet.Name = newName;
                else
                {
                    string sourceSheetName = sourceSheet.get_Parameter(BuiltInParameter.SHEET_NAME).AsString();
                    Parameter sheetNameParam = newSheet.get_Parameter(BuiltInParameter.SHEET_NAME);
                    if (sheetNameParam.IsReadOnly == false)
                    {
                        sheetNameParam.Set(sourceSheetName + " копия");
                        Logger.Log($"Назначен параметр {sheetNameParam.Definition.Name}: {sourceSheetName + " копия"}", 1);
                    }
                }
                CopyParameters(sourceSheet, newSheet, nextNum);

                if (copySchedules.IsChecked == true)
                {
                    //спецификации
                    foreach (ScheduleSheetInstance sourceSchedInst in scheduleInstances)
                    {
                        ViewSchedule sourceSchedule = _doc.GetElement(sourceSchedInst.ScheduleId) as ViewSchedule;
                        if (sourceSchedule == null) continue;

                        string sName = sourceSchedule.Name;

                        // Создаём копию вида спецификации через Duplicate
                        ElementId newScheduleId = sourceSchedule.Duplicate(ViewDuplicateOption.Duplicate);
                        ViewSchedule newSchedule = _doc.GetElement(newScheduleId) as ViewSchedule;
                        if (newSchedule == null) continue;

                        if (SpecName0.Text.Length > 0 && SpecName1.Text.Length > 0)
                        {
                            if (sName.Contains(SpecName0.Text))
                            {
                                try
                                {
                                    newSchedule.Name = sName.Replace(SpecName0.Text, SpecName1.Text);
                                    Logger.Log($"Заменено сочетание в имени: {SpecName0.Text}, {SpecName1.Text}", 1);
                                }
                                catch (ArgumentException)
                                {
                                    try
                                    {
                                        newSchedule.Name = sName.Replace(SpecName0.Text, SpecName1.Text) + " дубль";
                                        Logger.Log($"Заменено сочетание в имени: {SpecName0.Text}, {SpecName1.Text} дубль", 1);
                                    }
                                    catch (ArgumentException) { Logger.Log($"Ошибка: ArgumentException (даже с дубль)", 4); continue; }
                                }
                                catch (Exception ex) 
                                {
                                    Logger.Log($"Ошибка: {ex.Message}", 4); continue;
                                }
                            }
                            ReplaceFilterValueInSchedule(newSchedule, SpecName0.Text, SpecName1.Text);
                            Logger.Log($"Заменено сочетание в фильтре: {SpecName0.Text}, {SpecName1.Text}",1);
                        }

                        try
                        {
                            // Пытаемся разместить на листе
                            XYZ location = sourceSchedInst.Point;
                            ScheduleSheetInstance.Create(_doc, newSheet.Id, newSchedule.Id, location);
                            Logger.Log($"Спецификация \"{sourceSchedule.Name}\" размещена на листе, новое имя: {newSchedule.Name}", 1);
                        }
                        catch (Exception ex)
                        {
                            // Проверяем, что ошибка именно о "Internal schedule"
                            if (ex.Message.Contains("Internal") || ex.Message.Contains("cannot be added to sheets"))
                            {
                                // Удаляем неудавшийся дубликат вида, чтобы не засорять проект
                                _doc.Delete(newScheduleId);
                                // Можно вывести предупреждение (опционально)
                                Logger.Log(
                                    $"Спецификация \"{sourceSchedule.Name}\" является внутренней и не может быть размещена на листе. Она была пропущена.", 1);
                            }
                            else
                            {
                                // Если это другая ошибка — пробрасываем её дальше
                                throw;
                            }
                        }
                    }
                }

                if (copyLegends.IsChecked == true)
                {
                    //легенды
                    FilteredElementCollector viewports = new FilteredElementCollector(_doc, sourceSheet.Id)
                .OfClass(typeof(Viewport));
                    foreach (Viewport sourceVp in viewports)
                    {
                        View view = _doc.GetElement(sourceVp.ViewId) as View;
                        if (view == null || view.ViewType != ViewType.Legend) continue;

                        // тип ВЭ
                        ElementId viewportType = sourceVp.GetTypeId();

                        // Сохраняем положение исходного видового экрана
                        Outline outline = sourceVp.GetBoxOutline();

                        // Создаём новый видовой экран для той же легенды
                        // В качестве центра передаём середину исходного outline
                        XYZ center = (outline.MinimumPoint + outline.MaximumPoint) / 2.0;
                        Viewport newVp = Viewport.Create(_doc, newSheet.Id, view.Id, center);
                        newVp.ChangeTypeId(viewportType);

                        Logger.Log($"Скопирована легенда {view.Name}", 1);
                    }
                }
                    

                trans.Commit();
            }

            var mainWindow = Window.GetWindow(this) as MainWindow;
            mainWindow?.RefreshSheetList("");
            mainWindow?.LoadFilterSets();

            new InfoWindow280("Лист скопирован.").ShowDialog();
            Logger.Log("Завершение работы.",5);

            //Сериализация
            var settings = new CopyModeSettings() { CopySchedules = (bool)copySchedules.IsChecked, CopyLegends = (bool)copyLegends.IsChecked };
            try
            {
                File.WriteAllText(_jsonpath, JsonConvert.SerializeObject(settings));
                Logger.Log("Сериализация прошла успешно", 1);
            }
            catch (Exception ex) { Logger.Log("Ошибка при сериализации: " + ex.Message, 4); }
        }

        private void CopyParameters(Element source, Element dest, int sheetNumber)
        {
            if (Param.ParamExistByGuid(adskSheetSetParamGuid, source)) //Комплект чертежей
            {
                Parameter param = source.get_Parameter(adskSheetSetParamGuid);
                string value = param.AsString();
                if (param.HasValue)
                {
                    Parameter param2 = dest.get_Parameter(adskSheetSetParamGuid);
                    if (param2.IsReadOnly == false)
                    {
                        param2.Set(value);
                        Logger.Log($"Назначен параметр {param2.Definition.Name}: {value}", 1);
                    }
                    var viewModel = new SheetsStartSimpleVM();
                    bool forProject = true;
                    json js = new json("Менеджер листов. Комплекты и префиксы", in forProject, out bool canserialize, out string jsonpath);
                    if (canserialize)
                    {
                        viewModel = JsonConvert.DeserializeObject<SheetsStartSimpleVM>(File.ReadAllText(jsonpath));
                    }
                    if (viewModel.addPrefixes) //Номер листа - переопределение, если нужен префикс
                    {
                        Parameter param1 = source.get_Parameter(BuiltInParameter.SHEET_NUMBER);
                        param1.Set(value + " " + sheetNumber.ToString());
                    }
                }
            }
            string sourceSheetDate = source.get_Parameter(BuiltInParameter.SHEET_ISSUE_DATE).AsString();
            Parameter sheetDateParam = dest.get_Parameter(BuiltInParameter.SHEET_ISSUE_DATE);
            if (sheetDateParam.IsReadOnly == false)
            {
                sheetDateParam.Set(sourceSheetDate);
                Logger.Log($"Назначен параметр {sheetDateParam.Definition.Name}: {sourceSheetDate}", 1);
            }
            //ШНомер
            if (Param.ParamExistByGuid(NSheetNumberParamGuid, source))
            {
                Parameter param = source.get_Parameter(NSheetNumberParamGuid);
                if (param.IsReadOnly == false) 
                {
                    param.Set(sheetNumber.ToString());
                    Logger.Log($"Назначен параметр {param.Definition.Name}: {sheetNumber}", 1);
                }
            }
            //заполняем параметры штампа
            ViewSheet oldSheet = (ViewSheet)source;//исходный лист
            ICollection<FamilyInstance> titleBlocks = GetTitleBlocksOnSheet(oldSheet);

            ViewSheet newSheet = (ViewSheet)dest;//новый лист
            ICollection<FamilyInstance> titleBlocks1 = GetTitleBlocksOnSheet(newSheet);

            string[] titleBlockParams = new string[] 
            {
                "Книжный","А","Девятиграфка","1.Должность","2.Должность","3.Должность","4.Должность",
                "1.Подпись","2.Подпись","3.Подпись","4.Подпись",
                "1.Подпись.Видимость","2.Подпись.Видимость","3.Подпись.Видимость","4.Подпись.Видимость",
                "Дата утверждения листа"
            };

            if (titleBlocks.Count > 0&& titleBlocks1.Count > 0)
            {
                foreach (var titleBlock in titleBlocks)
                {
                    ElementId titleBlockTypeId = titleBlock.GetTypeId();

                    foreach (var titleBlock1 in titleBlocks1)
                    {
                        ElementId titleBlockTypeId1 = titleBlock1.GetTypeId();
                        if (titleBlockTypeId1.IntegerValue== titleBlockTypeId.IntegerValue) //сопоставление штампов по типу
                        {
                            foreach(var p in titleBlockParams)
                            {
                                if (CopyParamValue(p, _doc, titleBlock.Id, titleBlock1.Id))
                                {
                                    Logger.Log($"Параметр {p} скопирован", 1);
                                }
                                else Logger.Log($"Параметр {p} не скопирован", 4);
                            }
                            break;
                        }
                    }
                }
            }

        }
        private bool CopyParamValue(string paramname, Document doc, ElementId elem0id, ElementId elem1id)
        {
            Element elem = doc.GetElement(elem0id);
            Element elem1 = doc.GetElement(elem1id);
            if (Param.ParamExist(paramname, elem) && elem.LookupParameter(paramname).HasValue)
            {
                Parameter param = elem.LookupParameter(paramname);
                switch (param.StorageType)
                {
                    case StorageType.String:
                        string valueStr = elem.LookupParameter(paramname).AsString();
                        if (Param.ParamExist(paramname, elem1) && elem1.LookupParameter(paramname).IsReadOnly == false)
                        {
                            elem1.LookupParameter(paramname).Set(valueStr);
                            Logger.Log($"Параметр {paramname} - строка, назначено значение {valueStr}", 2);
                            return true;
                        }
                        break;
                    case StorageType.Integer:
                        int valueInt = elem.LookupParameter(paramname).AsInteger();
                        if (Param.ParamExist(paramname, elem1) && elem1.LookupParameter(paramname).IsReadOnly == false)
                        {
                            elem1.LookupParameter(paramname).Set(valueInt);
                            Logger.Log($"Параметр {paramname} - целое, назначено значение {valueInt}", 2); 
                            return true;
                        }
                        break;
                    case StorageType.ElementId:
                        ElementId valueId = elem.LookupParameter(paramname).AsElementId();
                        if (Param.ParamExist(paramname, elem1) && elem1.LookupParameter(paramname).IsReadOnly == false)
                        {
                            elem1.LookupParameter(paramname).Set(valueId);
                            Logger.Log($"Параметр {paramname} - ElementId, назначено значение {valueId.IntegerValue}", 2); 
                            return true;
                        }
                        break;
                    case StorageType.Double:
                        double valueD = elem.LookupParameter(paramname).AsDouble();
                        if (Param.ParamExist(paramname, elem1) && elem1.LookupParameter(paramname).IsReadOnly == false)
                        {
                            elem1.LookupParameter(paramname).Set(valueD);
                            Logger.Log($"Параметр {paramname} - число, назначено значение {valueD}", 2); 
                            return true;
                        }
                        break;
                }
            }
            Logger.Log($"Параметр {paramname} отсутствует у первого или второго элемента", 2);
            return false;
        }
        private ICollection<FamilyInstance> GetTitleBlocksOnSheet(ViewSheet sheet)
        {
            if (sheet == null)
                throw new ArgumentNullException(nameof(sheet));

            Document doc = sheet.Document;

            // Фильтр по элементам, принадлежащим данному листу (используем sheet.Id)
            FilteredElementCollector collector = new FilteredElementCollector(doc, sheet.Id);

            // Оставляем только экземпляры семейств категории "Основные надписи" (OST_TitleBlocks)
            ICollection<Element> titleBlockElements = collector
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .OfClass(typeof(FamilyInstance))
                .ToElements();

            // Приводим к типу FamilyInstance (все элементы этой категории — экземпляры семейств)
            return titleBlockElements.Cast<FamilyInstance>().ToList();
        }
        private int GetNextSheetNumber(string selectedSet)
        {
            List<ViewSheet> sheets = new List<ViewSheet>();
            try
            {
                sheets = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>().ToList();
            }
            catch (Exception)
            {
                return 0;
            }
            if (sheets.Count == 0) return 0;

            //Список TNovSheet
            List<TNovSheet> tNovSheets = new List<TNovSheet>();
            Element elem = _doc.GetElement(sheets.First().Id);
            bool sSetParamExist = Param.ParamExistByGuid(adskSheetSetParamGuid, elem);

            foreach (var sheet in sheets) //ИЗМЕНЕНО
            {
                string sSet = "";
                if (sSetParamExist) sSet = sheet.get_Parameter(adskSheetSetParamGuid).AsString();
                if (sSet == "----" || sSet == null || sSet.Length == 0) sSet = "";

                Logger.Log("   Лист " + sheet.get_Parameter(BuiltInParameter.SHEET_NUMBER) + " " + sheet.Name, 2);
                TNovSheet tNovSheet = GetTNovSheet(sheet, adskSheetSetParamGuid, sSetParamExist);

                if (selectedSet.Length == 0) { tNovSheets.Add(tNovSheet); }
                else
                {
                    if(sSet==selectedSet) { tNovSheets.Add(tNovSheet); }
                }
            }

            var maxSheet = GetMaxCleanNumberInt(tNovSheets);
            if (maxSheet != null)
            {
                int newNumber = maxSheet.TNovSheetNumericNumber + 1;
                return newNumber;
            }
            else
            {
                //старый код
                var numbers = sheets
                .Select(s => s.SheetNumber)
                .Where(n => int.TryParse(n, out _))
                .Select(int.Parse);
                return numbers.Any() ? numbers.Max() + 1 : 1;
            }
        }
        public static TNovSheet GetMaxCleanNumberInt(List<TNovSheet> sheets)
        {
            TNovSheet best = null;
            int maxValue = int.MinValue;

            foreach (var sheet in sheets)
            {
                if (int.TryParse(sheet.TNovSheetCleanNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                {
                    if (best == null || value > maxValue)
                    {
                        best = sheet;
                        maxValue = value;
                    }
                }
            }

            return best;
        }
        private TNovSheet GetTNovSheet(in ViewSheet sheet, in Guid sheetSetParamGuid, in bool sSetParamExist)
        {

            string sNumber = sheet.get_Parameter(BuiltInParameter.SHEET_NUMBER).AsString();
            Logger.Log("      Номер " + sNumber, 2);
            string sSet = "без комплекта";
            if (sSetParamExist) sSet = sheet.get_Parameter(sheetSetParamGuid).AsString();
            if (sSet == "----" || sSet == null || sSet.Length == 0) sSet = "без комплекта";
            Logger.Log("      Набор " + sSet, 2);


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
            Logger.Log("      num " + num.ToString(), 2);

            //Формируем TNovSheet
            TNovSheet tNovSheet = new TNovSheet()
            {
                Id = sheet.Id,
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

        /// <summary>
        /// Заменяет значение фильтра в спецификации, если оно совпадает с oldValue.
        /// </summary>
        private void ReplaceFilterValueInSchedule(ViewSchedule schedule, string oldValue, string newValue)
        {
            // Получаем определение спецификации
            ScheduleDefinition definition = schedule.Definition;
            if (definition == null) return;

            // Получаем текущие фильтры
            IList<ScheduleFilter> filters = definition.GetFilters();
            bool changed = false;

            for (int i = 0; i < filters.Count; i++)
            {
                ScheduleFilter filter = filters[i];
                if (filter.IsStringValue)
                {
                    string currentValue = filter.GetStringValue();

                
                    if (currentValue==oldValue)
                    {
                        // Создаём новый фильтр с тем же полем и типом, но новым значением
                        ScheduleFilter newFilter = new ScheduleFilter(filter.FieldId, filter.FilterType, newValue);
                        filters[i] = newFilter;
                        changed = true;
                    }
                }
                
            }

            // Если были изменения – сохраняем обновлённый список фильтров
            if (changed)
            {
                definition.SetFilters(filters);
            }
        }

    }
}