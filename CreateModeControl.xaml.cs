using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using TNovCommon;

namespace TNovViewsSheets
{
    public partial class CreateModeControl : UserControl
    {
        private UIDocument _uidoc;
        private Document _doc;
        private ObservableCollection<SheetInfo> _sheets;
        private List<TitleBlockItem> _titleBlockItems;
        private List<FormatBlockItem> _formatBlockItems;
        private List<OrientationBlockItem> _orientationBlockItems;
        Guid NSheetNumberParamGuid = new Guid("b6e73342-b6cd-42c5-86c5-64b04b5b88de"); //N_Ш.НомерЛиста
        Guid adskSheetSetParamGuid = new Guid("e1b06433-f527-403c-8986-af9a01e6be7f"); //A_Комплект чертежей


        public CreateModeControl(UIDocument uidoc, ObservableCollection<SheetInfo> sheets, List<TitleBlockItem> titleBlockItems,
            List<FormatBlockItem> formatBlockItems, List<OrientationBlockItem> orientationBlockItems)
        {
            InitializeComponent();
            _uidoc = uidoc;
            _doc = uidoc.Document;
            _sheets = sheets;
            _titleBlockItems = titleBlockItems;
            _formatBlockItems = formatBlockItems;
            _orientationBlockItems = orientationBlockItems;

            CbTitleBlocks.ItemsSource = _titleBlockItems;
            if (_titleBlockItems.Any())
                CbTitleBlocks.SelectedIndex = 0;
            CbFormat.ItemsSource = _formatBlockItems;
            if (_formatBlockItems.Any())
                CbFormat.SelectedIndex = 3;
            CbOrientation.ItemsSource = _orientationBlockItems;
            if (_orientationBlockItems.Any())
                CbOrientation.SelectedIndex = 1;

            LoadSheetSets();

            // Подписка на изменение выбора в комбобоксе
            CbSheetSets.SelectionChanged += CbSheetSets_SelectionChanged;

            // Подписка на TextChanged внутреннего TextBox (для IsEditable режима)
            CbSheetSets.Loaded += (s, e) =>
            {
                // Явно указываем System.Windows.Controls.TextBox
                var textBox = CbSheetSets.Template.FindName("PART_EditableTextBox", CbSheetSets) as System.Windows.Controls.TextBox;
                if (textBox != null)
                {
                    textBox.TextChanged += CbSheetSets_TextChanged;
                }
            };
        }

        private void LoadSheetSets()
        {
            var allSheets = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>();

            var sheetSetValues = allSheets
                .Select(s => s.get_Parameter(adskSheetSetParamGuid)?.AsString())
                .Where(v => !string.IsNullOrEmpty(v))
                .Distinct()
                .OrderBy(v => v)
                .ToList();

            CbSheetSets.ItemsSource = sheetSetValues;
        }

        private void CbSheetSets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateFilter();
        }

        private void CbSheetSets_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateFilter();
        }

        private void UpdateFilter()
        {
            var mainWindow = Window.GetWindow(this) as MainWindow;
            if (mainWindow != null)
            {
                mainWindow.SetSheetFilter(CbSheetSets.Text);
            }
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

            foreach (var sheet in sheets)
            {
                string sSet = "без комплекта";
                if (sSetParamExist) sSet = sheet.get_Parameter(adskSheetSetParamGuid).AsString();
                if (sSet == "----" || sSet == null || sSet.Length == 0) sSet = "без комплекта"; //continue; 
                Logger.Log("   Лист " + sheet.get_Parameter(BuiltInParameter.SHEET_NUMBER) + " " + sheet.Name, 2);
                TNovSheet tNovSheet = GetTNovSheet(sheet, adskSheetSetParamGuid, sSetParamExist);

                tNovSheets.Add(tNovSheet);
            }
            
            if (selectedSet.Length == 0)
            {
                tNovSheets = tNovSheets.Where(t => t.TNovSheetSet == selectedSet).ToList();
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

        private void CreateSheets_Click(object sender, RoutedEventArgs e)
        {
            Guid adskSheetSetParamGuid = new Guid("e1b06433-f527-403c-8986-af9a01e6be7f"); //A_Комплект чертежей
            Guid NSheetNumberParamGuid = new Guid("b6e73342-b6cd-42c5-86c5-64b04b5b88de"); //N_Ш.НомерЛиста

            var selectedItem = CbTitleBlocks.SelectedItem as TitleBlockItem;
            if (selectedItem == null)
            {
                new InfoWindow280("Выберите семейство штампа.").ShowDialog();
                return;
            }
            FamilySymbol titleBlockType = selectedItem.Symbol;
            var selectedSet = CbSheetSets.Text; if (selectedSet == null) selectedSet = "";
            var selectedFormat = CbFormat.SelectedItem as FormatBlockItem;
            if (selectedFormat == null)
            {
                new InfoWindow280("Выберите формат листа.").ShowDialog();
                return;
            }
            int sheetFormat = selectedFormat.Format;
            var selectedOrientation = CbOrientation.SelectedItem as OrientationBlockItem;
            if (selectedOrientation == null)
            {
                new InfoWindow280("Выберите ориентацию листа.").ShowDialog();
                return;
            }
            int sheetOrientation = selectedOrientation.VerticalOrientation;
            if (!int.TryParse(TbSheetCount.Text, out int count) || count <= 0)
            {
                new InfoWindow280("Введите корректное положительное число листов.").ShowDialog();
                return;
            }

            string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();

            Logger.Initialize("Менеджер листов. Создание листов", DateTime.Now, TNovVersion);

            int nextNum = GetNextSheetNumber(selectedSet);

            using (Transaction trans = new Transaction(_doc, "Создать листы"))
            {
                trans.Start();
                for (int i = 0; i < count; i++)
                {
                    ViewSheet newSheet = ViewSheet.Create(_doc, titleBlockType.Id);
                    try
                    {
                        newSheet.SheetNumber = (nextNum + i).ToString();
                    }
                    catch (Exception ex) //на всякий случай
                    {
                        Logger.Log($"Ошибка: {ex.Message}. Пробуем назначить номер 9999", 4);
                        nextNum = 1000;
                        newSheet.SheetNumber = (nextNum + i).ToString();
                    }
                    Logger.Log($"Создан лист {(nextNum + i).ToString()}", 1);
                    newSheet.Name = $"Лист {(nextNum + i).ToString()}";

                    if (Param.ParamExistByGuid(adskSheetSetParamGuid, newSheet))
                    {
                        Parameter param = newSheet.get_Parameter(adskSheetSetParamGuid);
                        if (!param.IsReadOnly) param.Set(selectedSet);
                        Logger.Log($"   Назначен комплект {selectedSet}", 2);
                    }
                    else Logger.Log("Параметр A_Комплект чертежей отсутствует.", 1);

                    if (Param.ParamExistByGuid(NSheetNumberParamGuid, newSheet))
                    {
                        Parameter param = newSheet.get_Parameter(NSheetNumberParamGuid);
                        if (!param.IsReadOnly) param.Set((nextNum + i).ToString());
                        Logger.Log($"   Назначен Ш.НомерЛиста {(nextNum + i).ToString()}", 2);
                    }
                    else Logger.Log("Параметр Ш.НомерЛиста отсутствует.", 1);

                    //заполняем параметры штампа
                    ICollection<FamilyInstance> titleBlocks = GetTitleBlocksOnSheet(newSheet);
                    if (titleBlocks.Count > 0)
                    {
                        foreach (var titleBlock in titleBlocks)
                        {
                            Element elem = _doc.GetElement(titleBlock.Id);
                            if (Param.ParamExist("Книжный", elem) && elem.LookupParameter("Книжный").IsReadOnly == false)
                            {
                                elem.LookupParameter("Книжный").Set(selectedOrientation.VerticalOrientation);
                            }
                            if (Param.ParamExist("А", elem) && elem.LookupParameter("А").IsReadOnly == false)
                            {
                                elem.LookupParameter("А").Set(selectedFormat.Format);
                            }
                        }
                    }
                    
                }
                trans.Commit();
            }
            Logger.Log("Завершение работы", 5);

            var mainWindow = Window.GetWindow(this) as MainWindow;
            mainWindow?.RefreshSheetList(selectedSet);
            mainWindow?.LoadFilterSets();

            new InfoWindow280($"Создано листов: {count}.").ShowDialog();
        }
    }
}