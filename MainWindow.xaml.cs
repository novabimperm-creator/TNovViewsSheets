using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using TNovCommon;
using static System.Windows.Forms.LinkLabel;

namespace TNovViewsSheets
{
    public enum ExportItemType
    {
        Sheet,
        Schedule
    }

    public class ExportableItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        public ElementId Id { get; set; }
        public string Name { get; set; }
        public ExportItemType Type { get; set; }
        public string TypeName => Type == ExportItemType.Sheet ? "Лист" : "Спецификация";
        public string SheetSet { get; set; }
        public string SheetNumber { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class NaturalComparer : System.Collections.IComparer
    {
        private readonly bool _descending;
        public NaturalComparer(bool descending = false) { _descending = descending; }
        public int Compare(object x, object y)
        {
            string sx = x as string; string sy = y as string;
            if (sx == null && sy == null) return 0;
            if (sx == null) return _descending ? 1 : -1;
            if (sy == null) return _descending ? -1 : 1;
            int result = NaturalCompare(sx, sy);
            return _descending ? -result : result;
        }
        private int NaturalCompare(string s1, string s2)
        {
            int i1 = 0, i2 = 0;
            while (i1 < s1.Length && i2 < s2.Length)
            {
                char c1 = s1[i1]; char c2 = s2[i2];
                if (char.IsDigit(c1) && char.IsDigit(c2))
                {
                    long num1 = 0, num2 = 0;
                    while (i1 < s1.Length && char.IsDigit(s1[i1])) { num1 = num1 * 10 + (s1[i1] - '0'); i1++; }
                    while (i2 < s2.Length && char.IsDigit(s2[i2])) { num2 = num2 * 10 + (s2[i2] - '0'); i2++; }
                    if (num1 != num2) return num1.CompareTo(num2);
                }
                else { if (c1 != c2) return c1.CompareTo(c2); i1++; i2++; }
            }
            return s1.Length.CompareTo(s2.Length);
        }
    }

    public class SheetComparer : System.Collections.IComparer
    {
        private readonly bool _setDescending; private readonly bool _numberDescending;
        public SheetComparer(bool setDescending = false, bool numberDescending = true) { _setDescending = setDescending; _numberDescending = numberDescending; }
        public int Compare(object x, object y)
        {
            var a = x as SheetInfo; var b = y as SheetInfo;
            if (a == null && b == null) return 0; if (a == null) return -1; if (b == null) return 1;
            int cmp = string.Compare(a.SheetSet, b.SheetSet, StringComparison.CurrentCulture);
            if (cmp != 0) return _setDescending ? -cmp : cmp;
            var numberComparer = new NaturalComparer(_numberDescending);
            return numberComparer.Compare(a.SheetNumber, b.SheetNumber);
        }
    }

    public class SheetInfo : INotifyPropertyChanged
    {
        private string _sheetSet;
        public ElementId Id { get; set; }
        public string SheetNumber { get; set; }
        public string Name { get; set; }
        public string SheetSet { get => _sheetSet; set { _sheetSet = value; OnPropertyChanged(nameof(SheetSet)); } }
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ViewInfo : INotifyPropertyChanged
    {
        public ElementId Id { get; set; }
        public string Name { get; set; }
        public string ViewFamily { get; set; }
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ScheduleInfo : INotifyPropertyChanged
    {
        private bool _isSelected;
        public ElementId Id { get; set; }
        public string Name { get; set; }
        public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); } }
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class TitleBlockItem
    {
        public FamilySymbol Symbol { get; set; }
        public string DisplayName => $"{Symbol.FamilyName} : {Symbol.Name}";
    }
    public class FormatBlockItem
    {
        public int Format { get; set; }
        public string DisplayName => $"А{Format.ToString()}";
    }
    public class OrientationBlockItem
    {
        public int VerticalOrientation { get; set; }
        public string DisplayName => VerticalOrientation == 1 ? "Книжная" : "Альбомная";
    }

    public class ViewFamilyTypeItem
    {
        public ViewFamilyType ViewFamilyType { get; set; }
        public string DisplayName => $"{ViewFamilyType.Name} ({ViewFamilyType.ViewFamily})";
    }

    public partial class MainWindow : Window
    {
        Guid NSheetNumberParamGuid = new Guid("b6e73342-b6cd-42c5-86c5-64b04b5b88de"); //N_Ш.НомерЛиста
        Guid adskSheetSetParamGuid = new Guid("e1b06433-f527-403c-8986-af9a01e6be7f"); //A_Комплект чертежей
        string symbol = "QAZ";
        string symbol1 = "\u202a";

        private UIDocument _uidoc;
        private Document _doc;
        private List<TitleBlockItem> _titleBlockItems;
        private List<FormatBlockItem> _formatBlockItems;
        private List<OrientationBlockItem> _orientationBlockItems;
        private List<ViewFamilyTypeItem> _viewFamilyTypeItems;
        private ObservableCollection<SheetInfo> _sheets;
        private ListCollectionView _sheetsView;
        private ObservableCollection<ViewInfo> _views;
        private ListCollectionView _viewsView;
        private ObservableCollection<ScheduleInfo> _schedules;
        private ListCollectionView _schedulesView;
        private ObservableCollection<ExportableItem> _exportItems;
        private ListCollectionView _exportView;
        private string _selectedFilterSet = "Все";
        private string _exportSearchText = "";
        private string _exportFilterSet = "Все";

        private bool _sortBySetDescending = false;
        private bool _sortByNumberDescending = true;
        private string _viewSortProperty = "Name";
        private bool _viewSortDescending = false;
        private string _scheduleSortProperty = "Name";
        private bool _scheduleSortDescending = false;
        private string _scheduleFilterText = "";

        private StartPanel _startPanel;
        private CreateModeControl _createControl;
        private CreateViewControl _createViewControl;
        private ExportPdfControl _exportPdfControl;
        private ExportSchedulesControl _exportSchedulesControl;
        private ImportExcelControl _importExcelControl;
        private CopyModeControl _copyControl;
        private DuplicateModeControl _duplicateControl;
        private DeleteModeControl _deleteControl;

        // Поля для просмотра фотографий
        private int _currentPhotoIndex = 0;

        public ICollectionView SheetsView => _sheetsView;
        public ICollectionView ViewsView => _viewsView;
        public ICollectionView SchedulesView => _schedulesView;
        public ICollectionView ExportView => _exportView;

        public MainWindow(UIDocument uidoc)
        {
            InitializeComponent();
            _uidoc = uidoc;
            _doc = uidoc.Document;

            _sheets = new ObservableCollection<SheetInfo>();
            _sheetsView = (ListCollectionView)CollectionViewSource.GetDefaultView(_sheets);
            _sheetsView.Filter = FilterSheet;
            _sheetsView.CustomSort = new SheetComparer(_sortBySetDescending, _sortByNumberDescending);

            _views = new ObservableCollection<ViewInfo>();
            _viewsView = (ListCollectionView)CollectionViewSource.GetDefaultView(_views);
            _viewsView.CustomSort = new NaturalComparer(_viewSortDescending);

            _schedules = new ObservableCollection<ScheduleInfo>();
            _schedulesView = (ListCollectionView)CollectionViewSource.GetDefaultView(_schedules);
            _schedulesView.Filter = FilterSchedule;
            _schedulesView.CustomSort = new NaturalComparer(_scheduleSortDescending);

            _exportItems = new ObservableCollection<ExportableItem>();
            _exportView = (ListCollectionView)CollectionViewSource.GetDefaultView(_exportItems);
            _exportView.Filter = ExportFilter;

            LoadTitleBlocks();
            FillFormatAndOrientationLists();
            LoadViewFamilyTypes();
            LoadFilterSets();
            RefreshSheetList("");
            RefreshViewList();
            RefreshScheduleList();

            
            _startPanel = new StartPanel(this);
            _createControl = new CreateModeControl(_uidoc, _sheets, _titleBlockItems, _formatBlockItems, _orientationBlockItems);
            _createViewControl = new CreateViewControl(_uidoc, _viewFamilyTypeItems);
            _exportPdfControl = new ExportPdfControl(_uidoc, _exportItems);
            _exportSchedulesControl = new ExportSchedulesControl(_uidoc, _schedules);
            _importExcelControl = new ImportExcelControl(_uidoc);
            _copyControl = new CopyModeControl(_uidoc, _sheets, LvSheets);
            _duplicateControl = new DuplicateModeControl(_uidoc, _sheets, _titleBlockItems, LvSheets);
            _deleteControl = new DeleteModeControl(_uidoc, LvSheets);

            FunctionContent.Content = _startPanel;
            HighlightButton(BtnStart);
            SetStartMode();
        }

        // Метод для фильтрации листов по комплекту (вызывается из CreateModeControl)
        public void SetSheetFilter(string filterSet)
        {
            _selectedFilterSet = string.IsNullOrEmpty(filterSet) ? "Все" : filterSet;
            _sheetsView?.Refresh();
        }

        
        // Обработчик кнопки просмотра фотографий
        private void BtnPhotoView_Click(object sender, RoutedEventArgs e)
        {
            Logger.Initialize("Менеджер листов. Просмотр фото", DateTime.Now, "4");
            
            _currentPhotoIndex = (_currentPhotoIndex + 1) % 4;
            int photoNumber = _currentPhotoIndex + 1;

            ShowFullScreenPhoto($"images/photo{photoNumber}.jpg");
            HighlightButton(BtnPhotoView);
        }

        // Отображение фотографии на весь экран
        private void ShowFullScreenPhoto(string resourceName)
        {
            try
            {
                double screenWidth = SystemParameters.PrimaryScreenWidth;
                double screenHeight = SystemParameters.PrimaryScreenHeight;

                // Определяем имя текущей сборки
                string assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;

                // Очищаем путь от лишних слешей
                string cleanPath = resourceName.TrimStart('/');

                // Формируем правильный pack URI с именем сборки и ;component/
                string uriString = $"pack://application:,,,/{assemblyName};component/{cleanPath}";
                Uri uri = new Uri(uriString, UriKind.Absolute);

                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                var photoWindow = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Black,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = 0,
                    Top = 0,
                    Width = screenWidth,
                    Height = screenHeight,
                    Topmost = true,
                    Content = new Image { Source = bitmap, Stretch = System.Windows.Media.Stretch.Uniform }
                };

                // Получаем главное окно Revit
                var revitWindow = GetRevitWindow();
                if (revitWindow != null)
                    photoWindow.Owner = revitWindow;

                photoWindow.MouseDown += (s, args) => photoWindow.Close();
                photoWindow.PreviewKeyDown += (s, args) =>
                {
                    if (args.Key == Key.Escape)
                        photoWindow.Close();
                };

                photoWindow.Show();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка", $"Не удалось открыть изображение: {ex.Message}");
            }
        }

        private Window GetRevitWindow()
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                return System.Windows.Interop.HwndSource.FromHwnd(process.MainWindowHandle)?.RootVisual as Window;
            }
            return null;
        }
        /// <summary>
        /// Обработчик кнопки нумератора – создаёт список TNovSheet и открывает контрол
        /// </summary>
        private void BtnSheetNumbering_Click(object sender, RoutedEventArgs e)
        {
            var items = new List<TNovSheet>();
            var allSheets = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .ToList();

            if (allSheets == null || allSheets.Count == 0)
            {
                new InfoWindow280("В проекте отсутствуют листы!").ShowDialog();
                return;
            }

            Element firstSheet = _doc.GetElement(allSheets.First().Id);
            bool sSetParamExist = Param.ParamExistByGuid(adskSheetSetParamGuid, firstSheet);
            foreach (var sheet in allSheets)
            {
                
                TNovSheet tNovSheet = GetTNovSheet(sheet, adskSheetSetParamGuid,sSetParamExist);
                if (tNovSheet.TNovSheetCanRenum)
                    items.Add(tNovSheet);
            }

            if (items.Count == 0)
            {
                MessageBox.Show("Нет листов, доступных для нумерации (не удалось определить числовой номер).",
                                "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var control = new SheetNumControl(this, _uidoc, items.ToArray());
            FunctionContent.Content = control;
            HighlightButton(BtnSheetNumbering);
            SetStartMode();
        }
        /// <summary>
        /// Применение нумерации листов (двухэтапное обновление с суффиксом ZAQ)
        /// </summary>
        public void ApplySheetNumbering(List<TNovSheet> numberedSheets)
        {
            Document doc = _doc;
            string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();

            string set = "";

            Logger.Initialize("Менеджер листов. Перенумерация", DateTime.Now, TNovVersion);

            using (TransactionGroup group = new TransactionGroup(doc, "TNov - Менеджер листов (Нумератор)"))
            {
                group.Start();

                // Транзакция 5: назначаем временные номера с суффиксом "ZAQ"
                using (Transaction t5 = new Transaction(doc))
                {
                    Logger.Log("Назначаем новые номера с временным суффиксом (транзакция 1)", 1);
                    t5.Start("TNov - Менеджер листов 1 этап (временные номера)");

                    set = numberedSheets.First().TNovSheetSet;

                    foreach (var tNovSheet in numberedSheets)
                    {
                        Element elem = doc.GetElement(tNovSheet.Id);
                        if (!(elem is ViewSheet sheet)) continue;

                        string newNumberStr = tNovSheet.TNovSheetNumericNumber.ToString();
                        string sNumber = sheet.get_Parameter(BuiltInParameter.SHEET_NUMBER).AsString();

                        string sSet = "без комплекта";
                        if (Param.ParamExistByGuid(adskSheetSetParamGuid,elem))
                            sSet = sheet.get_Parameter(adskSheetSetParamGuid).AsString();

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
                        string newNumberToSet = sNumber.Replace(cleanNumber, newNumberStr);

                        //а также добавляем к нему временный суффикс
                        newNumberToSet = newNumberToSet + "ZAQ";

                        try
                        {
                            // Устанавливаем номер с суффиксом
                            sheet.get_Parameter(BuiltInParameter.SHEET_NUMBER).Set(newNumberToSet);
                            Logger.Log("   Лист " + sNumber + ": назначен Номер " + newNumberToSet, 2);
                            // Если есть параметр "Шномер", устанавливаем его сразу (без суффикса)
                            if (Param.ParamExistByGuid(NSheetNumberParamGuid, elem))
                            {
                                Parameter sheetCustomNumberParameter = sheet.get_Parameter(NSheetNumberParamGuid); //Шномер
                                sheetCustomNumberParameter.Set(newNumberToSet);
                            }
                            Parameter customParam = sheet.get_Parameter(NSheetNumberParamGuid);
                            if (customParam != null && customParam.StorageType == StorageType.String)
                            {
                                customParam.Set(newNumberStr);
                            }
                        }
                        catch (Exception e) { Logger.Log("   Лист " + sNumber + " ошибка: " + e.Message, 4); }
                    }
                    Logger.Log("Закрываем транзакцию 1", 1);
                    t5.Commit();
                }

                // Обновляем коллекцию листов после первой транзакции
                Logger.Log("Обновляем листы", 1);

                List<ViewSheet> sheets5 = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Sheets)
                .WhereElementIsNotElementType()
                .Cast<ViewSheet>()
                .ToList();

                // Транзакция 6: убираем суффикс "ZAQ"
                using (Transaction t6 = new Transaction(doc))
                {
                    Logger.Log("Назначаем новые номера (транзакция 2)", 1);
                    t6.Start("TNov - Менеджер листов 2 этап (удаление суффикса)");

                    foreach (var sheet in sheets5)
                    {
                        Element elem = doc.GetElement(sheet.Id);
                        string sSet = "без комплекта";
                        if (Param.ParamExistByGuid(adskSheetSetParamGuid, elem)) sSet = sheet.get_Parameter(adskSheetSetParamGuid).AsString();
                        if (sSet == "----") sSet = "без комплекта"; //continue;

                        Parameter sheetNumberParameter = sheet.get_Parameter(BuiltInParameter.SHEET_NUMBER);
                        string sNumber = sheetNumberParameter.AsString();
                        string newNumber = sNumber.Replace("ZAQ", "");

                        string sheetSet = "";

                        if (Param.ParamExistByGuid(adskSheetSetParamGuid, elem)) sheetSet = sheet.get_Parameter(adskSheetSetParamGuid).AsString();

                        try
                        {
                            sheetNumberParameter.Set(newNumber);
                            Logger.Log("   Лист " + sNumber + ": назначен Номер " + newNumber, 2);
                        }
                        catch (Exception e) { Logger.Log("   Лист " + sNumber + " ошибка: " + e.Message, 4); }

                    }


                    Logger.Log("Закрываем транзакцию 2. Завершение работы.", 5);

                    t6.Commit();
                }

                group.Assimilate();
            }

            // Обновляем список листов в интерфейсе
            RefreshSheetList(set);
        }
        public void ShowStartPanel()
        {
            FunctionContent.Content = _startPanel;
            HighlightButton(BtnStart);
            SetStartMode();
        }

        private void LoadTitleBlocks()
        {
            var symbols = new FilteredElementCollector(_doc).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_TitleBlocks).Cast<FamilySymbol>().Where(s => s.IsActive).ToList();
            _titleBlockItems = symbols.Select(s => new TitleBlockItem { Symbol = s }).ToList();
        }
        private void FillFormatAndOrientationLists()
        {
            _formatBlockItems = new List<FormatBlockItem>
            {
                new FormatBlockItem {Format = 0 },new FormatBlockItem {Format = 1 },new FormatBlockItem {Format = 2 },new FormatBlockItem {Format = 3 },new FormatBlockItem {Format = 4 },
            };
            _orientationBlockItems = new List<OrientationBlockItem>
            {
                new OrientationBlockItem {VerticalOrientation = 1},new OrientationBlockItem {VerticalOrientation = 0}
            };
        }

        private void LoadViewFamilyTypes()
        {
            /*
            var types = new FilteredElementCollector(_doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().OrderBy(vft => vft.Name).ToList();
            _viewFamilyTypeItems = types.Select(vft => new ViewFamilyTypeItem { ViewFamilyType = vft }).ToList();*/
            var types = new FilteredElementCollector(_doc)
        .OfClass(typeof(ViewFamilyType))
        .Cast<ViewFamilyType>()
        .Where(vft => vft.ViewFamily == ViewFamily.FloorPlan) // фильтр только для FloorPlan
        .OrderBy(vft => vft.Name)
        .ToList();

            _viewFamilyTypeItems = types.Select(vft => new ViewFamilyTypeItem { ViewFamilyType = vft }).ToList();
        }

        // В методе LoadFilterSets:
        public void LoadFilterSets()
        {
            var allSheets = new FilteredElementCollector(_doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>();
            var sets = allSheets.Select(s => s.get_Parameter(adskSheetSetParamGuid)?.AsString()).Where(v => !string.IsNullOrEmpty(v)).Distinct().OrderBy(v => v).ToList();
            sets.Insert(0, "Все");
            CbFilterSet.ItemsSource = sets;
            CbFilterSet.SelectedIndex = 0;
        }
    // CbExportFilterSet.ItemsSource = sets;
    // CbExportFilterSet.SelectedIndex = 0;

        public void RefreshSheetList(string set)
        {
            _sheets.Clear();
            var allSheets = new FilteredElementCollector(_doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>();
            foreach (var sheet in allSheets)
            {
                var param = sheet.get_Parameter(adskSheetSetParamGuid);
                string setValue = param?.AsString() ?? "";
                _sheets.Add(new SheetInfo
                {
                    Id = sheet.Id,
                    SheetNumber = sheet.SheetNumber,
                    Name = sheet.Name,
                    SheetSet = setValue
                });
            }
            if(set.Length>0)
            {
                foreach (var item in CbFilterSet.Items)
                {
                    if (item is ComboBoxItem comboItem && comboItem.Content.ToString() == set)
                    {
                        CbFilterSet.SelectedItem = comboItem;
                        _selectedFilterSet = CbFilterSet.SelectedItem.ToString();
                        _sheetsView.Refresh();
                        break;
                    }
                }
            }
        }

        public void RefreshViewList()
        {
            _views.Clear();
            var allViews = new FilteredElementCollector(_doc).OfClass(typeof(View)).Cast<View>().Where(v => !(v is ViewSheet)).OrderBy(v => v.Name);
            foreach (var view in allViews)
                _views.Add(new ViewInfo { Id = view.Id, Name = view.Name, ViewFamily = view.ViewType.ToString() });
        }

        public void RefreshScheduleList()
        {
            _schedules.Clear();
            var allSchedules = new FilteredElementCollector(_doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>().OrderBy(v => v.Name);
            foreach (var schedule in allSchedules)
                _schedules.Add(new ScheduleInfo { Id = schedule.Id, Name = schedule.Name, IsSelected = false });
            UpdateSelectedSchedulesCount();
        }

        public void RefreshExportItemsForPdf()
        {
            _exportItems.Clear();
            foreach (var sheet in _sheets)
                _exportItems.Add(new ExportableItem
                {
                    Id = sheet.Id,
                    Name = sheet.Name,
                    Type = ExportItemType.Sheet,
                    SheetSet = sheet.SheetSet,
                    SheetNumber = sheet.SheetNumber,
                    IsSelected = false
                });
        }

        private bool FilterSheet(object obj)
        {
            if (_selectedFilterSet == "Все") return true;
            var sheet = obj as SheetInfo;
            return sheet?.SheetSet == _selectedFilterSet;
        }

        private bool FilterSchedule(object obj)
        {
            if (string.IsNullOrEmpty(_scheduleFilterText)) return true;
            var sched = obj as ScheduleInfo;
            return sched?.Name.IndexOf(_scheduleFilterText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool ExportFilter(object obj)
        {
            var item = obj as ExportableItem;
            if (item == null || item.Type != ExportItemType.Sheet) return false;
            if (_exportFilterSet != "Все" && item.SheetSet != _exportFilterSet) return false;
            if (!string.IsNullOrEmpty(_exportSearchText) && item.Name.IndexOf(_exportSearchText, StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            return true;
        }

        private void CbFilterSet_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CbFilterSet.SelectedItem != null)
            {
                _selectedFilterSet = CbFilterSet.SelectedItem.ToString();
                _sheetsView.Refresh();
            }
        }

        // Обработчики:
        private void CbExportFilterSet_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // if (CbExportFilterSet.SelectedItem != null)
            // {
            //     _exportFilterSet = CbExportFilterSet.SelectedItem.ToString();
            //     _exportView?.Refresh();
            // }
        }

        private void TbExportSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            // _exportSearchText = TbExportSearch.Text;
            // _exportView?.Refresh();
        }

        private void ExportSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _exportView.Cast<ExportableItem>())
                item.IsSelected = true;
        }

        private void ExportDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _exportView.Cast<ExportableItem>())
                item.IsSelected = false;
        }

        // Заглушки для событий сортировки и фильтрации (реализуйте при необходимости)
        private void ColumnHeader_Click(object sender, RoutedEventArgs e) { }
        private void ViewColumnHeader_Click(object sender, RoutedEventArgs e) { }
        private void ScheduleColumnHeader_Click(object sender, RoutedEventArgs e) { }
        private void ScheduleFilter_TextChanged(object sender, TextChangedEventArgs e) { }
        private void SelectAllVisibleSchedules_Click(object sender, RoutedEventArgs e) { }
        private void DeselectAllVisibleSchedules_Click(object sender, RoutedEventArgs e) { }
        public void UpdateSelectedSchedulesCount() { }

        private void SetStartMode()
        {
            RightPanel.Visibility = System.Windows.Visibility.Collapsed;
            System.Windows.Controls.Grid.SetColumnSpan(FunctionBorder, 2);
        }

        private void SetFunctionMode(bool showSheets, bool showViews, bool showSchedules, bool showExport)
        {
            RightPanel.Visibility = System.Windows.Visibility.Visible;
            System.Windows.Controls.Grid.SetColumnSpan(FunctionBorder, 1);

            SheetsPanel.Visibility = showSheets ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            ViewsPanel.Visibility = showViews ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            SchedulesPanel.Visibility = showSchedules ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            ExportPanel.Visibility = showExport ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

            if (showSheets) RefreshSheetList("");
            if (showViews) RefreshViewList();
            if (showSchedules) RefreshScheduleList();
            if (showExport)
            {
                RefreshSheetList("");
                RefreshExportItemsForPdf();
                _exportView.Refresh();
            }
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        { FunctionContent.Content = _startPanel; HighlightButton(BtnStart); SetStartMode(); }
        private void BtnCreateMode_Click(object sender, RoutedEventArgs e)
        { FunctionContent.Content = _createControl; HighlightButton(BtnCreateMode); SetFunctionMode(true,false,false,false); }
        private void BtnCreateViewMode_Click(object sender, RoutedEventArgs e)
        { FunctionContent.Content = _createViewControl; HighlightButton(BtnCreateViewMode); SetFunctionMode(false,true,false,false); }
        /*
        private void BtnExportPdfMode_Click(object sender, RoutedEventArgs e)
        {
            if (_exportPdfControl == null) _exportPdfControl = new ExportPdfControl(_uidoc, _exportItems);
            FunctionContent.Content = _exportPdfControl;
            HighlightButton(BtnExportPdfMode);
            SetFunctionMode(false,false,false,true);
        }
        private void BtnExportSchedulesMode_Click(object sender, RoutedEventArgs e)
        { FunctionContent.Content = _exportSchedulesControl; HighlightButton(BtnExportSchedulesMode);
          SetFunctionMode(false,false,true,false); }
        private void BtnImportExcelMode_Click(object sender, RoutedEventArgs e)
        {
            if (_importExcelControl == null) _importExcelControl = new ImportExcelControl(_uidoc);
            FunctionContent.Content = _importExcelControl;
            HighlightButton(BtnImportExcelMode);
            SetFunctionMode(true, false, false, false);
        }*/
        private void BtnCopyMode_Click(object sender, RoutedEventArgs e)
        { FunctionContent.Content = _copyControl; HighlightButton(BtnCopyMode); SetFunctionMode(true,false,false,false); }
        /*
        private void BtnDuplicateMode_Click(object sender, RoutedEventArgs e)
        { FunctionContent.Content = _duplicateControl; HighlightButton(BtnDuplicateMode); SetFunctionMode(true, false, false, false); }*/
        private void BtnDeleteMode_Click(object sender, RoutedEventArgs e)
        { FunctionContent.Content = _deleteControl; HighlightButton(BtnDeleteMode); SetFunctionMode(true, false, false, false); }

        // Метод подсветки активной кнопки (исправленный)
        private void HighlightButton(Button activeButton)
        {
            var buttons = new[] { BtnStart, BtnCreateMode, BtnCreateViewMode, //BtnExportPdfMode,
                                  //BtnExportSchedulesMode, BtnImportExcelMode, 
                BtnCopyMode,
                                  //BtnDuplicateMode, 
                BtnDeleteMode, BtnSheetNumbering, BtnPhotoView };

            foreach (var btn in buttons)
            {
                btn.ClearValue(Button.BackgroundProperty);
                btn.ClearValue(Button.BorderBrushProperty);
                btn.ClearValue(Button.BorderThicknessProperty);
                btn.ClearValue(Button.FontWeightProperty);
            }

            activeButton.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80));
            activeButton.BorderThickness = new Thickness(0, 0, 0, 3);
        }

        private void LvSheets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

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
        private void BtnSheetPrintSets_Click(object sender, RoutedEventArgs e)
        {
            List<ViewSheet> sheets = new List<ViewSheet>();
            //получаем элементы
            try
            {
                sheets = new FilteredElementCollector(_doc).OfCategory(BuiltInCategory.OST_Sheets)
                .WhereElementIsNotElementType()
                .Cast<ViewSheet>()
                .ToList();

            }
            catch(Exception ex)
            {
                new InfoWindow280($"Ошибка: {ex.Message}").ShowDialog();
                return;
            }

            //проверка наличия листов
            if (sheets.Count == 0)
            {
                new InfoWindow280("В данной модели отсутствуют листы!").ShowDialog();
                return;
            }

            string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();

            Logger.Initialize("Менеджер листов. Комплекты и префиксы", DateTime.Now, TNovVersion);

            //окно
            var viewModel = new SheetsStartSimpleVM();
            // Десериализация
            bool forProject = true;
            json js = new json("Менеджер листов. Комплекты и префиксы", in forProject, out bool canserialize, out string jsonpath);
            if (canserialize)
            {
                viewModel = JsonConvert.DeserializeObject<SheetsStartSimpleVM>(File.ReadAllText(jsonpath));
                Logger.Log("Десериализация прошла успешно", 1);
            }
            var wpfview = new SheetsStartSimpleWPF(viewModel);
            bool? ok = wpfview.ShowDialog();
            if (ok != null && ok == true) { }
            else { Logger.Log("Запуск отменен пользователем. Завершение работы.", 3); return; }
            //Сериализация
            try
            {
                File.WriteAllText(jsonpath, JsonConvert.SerializeObject(viewModel));
                Logger.Log("Сериализация прошла успешно", 1);
            }
            catch (Exception ex) { Logger.Log("Ошибка при сериализации: " + ex.Message, 4); }

            bool addPrefix = viewModel.addPrefixes;

            //проверяем наличие параметров
            Element elem = _doc.GetElement(sheets.First().Id);
            bool sSetParamExist = Param.ParamExistByGuid(adskSheetSetParamGuid, elem);
            bool sCustomNumberParamExist = Param.ParamExistByGuid(NSheetNumberParamGuid, elem);


            Logger.Log("Создаем список элементов класса TNovSheet", 1);

            //Список TNovSheet
            List<TNovSheet> tNovSheets = new List<TNovSheet>();

            foreach (var sheet in sheets)
            {
                string sSet = "без комплекта";
                if (sSetParamExist) sSet = sheet.get_Parameter(adskSheetSetParamGuid).AsString();
                if (sSet == "----" || sSet == null || sSet.Length == 0) sSet = "без комплекта"; //continue; 
                Logger.Log("   Лист " + sheet.get_Parameter(BuiltInParameter.SHEET_NUMBER) + " " + sheet.Name, 2);
                TNovSheet tNovSheet = GetTNovSheet(sheet, adskSheetSetParamGuid, sSetParamExist);

                tNovSheets.Add(tNovSheet);
            }
            Logger.Log("Сортируем tNovSheets по комплекту", 1);
            var tNovSheetsSortedBySet = from tNovSheet in tNovSheets
                                        orderby tNovSheet.TNovSheetSet
                                        select tNovSheet;

            var tNovSheetSets = from tNovSheet in tNovSheetsSortedBySet //список комплектов
                                group tNovSheet by tNovSheet.TNovSheetSet;


            //список номеров для проверок
            List<string> newNums = new List<string>();

            List<string> newSets = new List<string>();

            using (TransactionGroup group = new TransactionGroup(RevitAPI.Document, "TNov - Менеджер листов (Комплекты)"))
            {
                group.Start();

                //транзакция 1
                using (Transaction t = new Transaction(_doc))
                {
                    Logger.Log("Назначаем чистые номера с временными префиксами и суффиксами (транзакция 1)", 1);
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
                                    Logger.Log("      найден лист Revit " + RevitApiCompat.ElementIdIntValue(sheet.Id).ToString() + " : " + sNumber + " " + sheet.Name, 2);
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
                                        Logger.Log("      назначен Номер", 2);
                                        if (sCustomNumberParamExist)
                                        {
                                            Parameter sheetCustomNumberParameter = sheet.get_Parameter(NSheetNumberParamGuid); //Шномер
                                            sheetCustomNumberParameter.Set(tNovSheet.TNovSheetCleanNumber);
                                            Logger.Log("      назначен Ш.Номер", 2);
                                        }
                                    }
                                    catch (Exception ex) { Logger.Log("   Лист " + sNumber + " ошибка: " + ex.Message, 4); }
                                    break;
                                }

                            }
                            if (!sheetFound) Logger.Log("      Ошибка! Не найден лист Revit", 4);
                        }


                        suffixCount++; //+суффикс для последующих комплектов
                    }

                    Logger.Log("Закрываем транзакцию 1", 1);
                    t.Commit();
                }


                //обновленная коллекция листов
                Logger.Log("Обновляем листы", 1);

                List<ViewSheet> sheets1 = new FilteredElementCollector(_doc).OfCategory(BuiltInCategory.OST_Sheets)
                .WhereElementIsNotElementType()
                .Cast<ViewSheet>()
                .ToList();


                //транзакция 2
                using (Transaction t2 = new Transaction(_doc))
                {
                    Logger.Log("Назначаем чистые номера с невидимыми префиксами и суффиксами (транзакция 2)", 1);
                    t2.Start("TNov - Менеджер листов 2 этап");


                    foreach (var sheet in sheets1)
                    {
                        Logger.Log("   лист Revit " + RevitApiCompat.ElementIdIntValue(sheet.Id).ToString() + " : " +
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
                        catch (Exception ex) { Logger.Log("   Лист " + sNumber + " ошибка: " + ex.Message, 4); }

                    }


                    Logger.Log("Закрываем транзакцию 2", 1);
                    t2.Commit();
                }

                //обновленная коллекция листов
                Logger.Log("Обновляем листы", 1);

                List<ViewSheet> sheets2 = new FilteredElementCollector(_doc).OfCategory(BuiltInCategory.OST_Sheets)
                .WhereElementIsNotElementType()
                .Cast<ViewSheet>()
                .ToList();


                //Формирование комплектов на печать

                Logger.Log("Получаем существующие наборы на печать", 1);
                ICollection<Element> printSets = new FilteredElementCollector(_doc).OfClass(typeof(ViewSheetSet)).ToElements();

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
                    using (Transaction t3 = new Transaction(_doc))
                    {
                        Logger.Log("Удаляем используемые комплекты на печать (транзакция 3)", 1);
                        t3.Start("TNov - Менеджер листов 3 этап");

                        _doc.Delete(printSetsToDelete);

                        Logger.Log("Закрываем транзакцию 3", 1);
                        t3.Commit();
                    }
                }

                Logger.Log("Cоздаем новые комплекты на печать", 1);

                //транзакция 4
                using (Transaction t4 = new Transaction(_doc))
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
                        PrintManager printManager = _doc.PrintManager;
                        printManager.PrintRange = PrintRange.Select;
                        ViewSheetSetting viewSheetSetting = printManager.ViewSheetSetting;
                        try
                        {
                            viewSheetSetting.CurrentViewSheetSet.Views = viewSet;
                            viewSheetSetting.SaveAs(set);
                            newSets.Add(set);
                            Logger.Log("Набор успешно создан: " + set, 1);
                        }
                        catch (Exception ex) { Logger.Log(set + " ошибка: " + ex.Message, 4); }
                    }
                    Logger.Log("Закрываем транзакцию 4", 1);
                    t4.Commit();
                }

                group.Assimilate();
                
            }
            
            string message = $"Созданы/обновлены наборы листов:\n{String.Join("\n", newSets.ToArray())}.";
            if (viewModel.addPrefixes) message += "\nВ номера листов добавлены префиксы комплектов.";
            else message += "\nИз номеров листов убраны префиксы комплектов.";
            new InfoWindow400(message).ShowDialog();

            Logger.Log(message, 1);
            
            Logger.Log("Завершение работы.", 5);

            FunctionContent.Content = _createControl; SetFunctionMode(true, false, false, false);
        }

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {

        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow((DependencyObject)sender);
            window?.Close();
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            string commandText = @"https://portal.talan.group/knowledge/proektirovanie/listynumeratsiyaikomplektynaeksport/";
            var proc = new System.Diagnostics.Process();
            proc.StartInfo.FileName = commandText;
            proc.StartInfo.UseShellExecute = true;
            proc.Start();
        }
    }
}