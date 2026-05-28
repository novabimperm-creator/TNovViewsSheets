using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TNovCommon;

namespace TNovViewsSheets
{
    // Класс для отображения уровня в комбобоксе
    public class LevelItem
    {
        public Level Level { get; set; }
        public string DisplayName => Level.Name;
    }

    // Класс для отображения видового шаблона в комбобоксе
    public class ViewTemplateItem
    {
        public View ViewTemplate { get; set; }
        public string DisplayName => ViewTemplate?.Name ?? "- без шаблона -";
    }

    public partial class CreateViewControl : UserControl
    {
        private UIDocument _uidoc;
        private Document _doc;
        private List<ViewFamilyTypeItem> _viewFamilyTypes;
        private List<LevelItem> _levels;
        private List<ViewTemplateItem> _viewTemplates;

        public CreateViewControl(UIDocument uidoc, List<ViewFamilyTypeItem> viewFamilyTypes)
        {
            InitializeComponent();
            _uidoc = uidoc;
            _doc = uidoc.Document;
            _viewFamilyTypes = viewFamilyTypes;

            // Загружаем типы видов
            CbViewFamilyTypes.ItemsSource = _viewFamilyTypes;
            CbViewFamilyTypes.DisplayMemberPath = "DisplayName";
            if (_viewFamilyTypes.Any())
                CbViewFamilyTypes.SelectedIndex = 0;

            // Загружаем уровни
            _levels = new FilteredElementCollector(_doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .Select(l => new LevelItem { Level = l })
                .ToList();
            CbLevels.ItemsSource = _levels;
            CbLevels.DisplayMemberPath = "DisplayName";
            if (_levels.Any())
                CbLevels.SelectedIndex = 0;

            // Загружаем видовые шаблоны
            var templates = new FilteredElementCollector(_doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate)
                .OrderBy(v => v.Name)
                .Select(v => new ViewTemplateItem { ViewTemplate = v })
                .ToList();
            // Добавляем пустой элемент для возможности не выбирать шаблон
            _viewTemplates = new List<ViewTemplateItem> { new ViewTemplateItem { ViewTemplate = null } };
            _viewTemplates.AddRange(templates);

            CbViewTemplates.ItemsSource = _viewTemplates;
            CbViewTemplates.DisplayMemberPath = "DisplayName";
            CbViewTemplates.SelectedIndex = 0; // выбираем "- без шаблона -"

            // Первоначальная настройка видимости панелей
            UpdatePanelVisibility();
        }

        private void CbViewFamilyTypes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdatePanelVisibility();
        }

        private void UpdatePanelVisibility()
        {
            var selectedItem = CbViewFamilyTypes.SelectedItem as ViewFamilyTypeItem;
            if (selectedItem == null) return;

            if (selectedItem.ViewFamilyType.ViewFamily == ViewFamily.Drafting)
            {
                DraftingPanel.Visibility = System.Windows.Visibility.Visible;
                FloorPlanPanel.Visibility = System.Windows.Visibility.Collapsed;
            }
            else if (selectedItem.ViewFamilyType.ViewFamily == ViewFamily.FloorPlan)
            {
                DraftingPanel.Visibility = System.Windows.Visibility.Collapsed;
                FloorPlanPanel.Visibility = System.Windows.Visibility.Visible;
            }
            else
            {
                DraftingPanel.Visibility = System.Windows.Visibility.Collapsed;
                FloorPlanPanel.Visibility = System.Windows.Visibility.Collapsed;
                //MessageBox.Show("Создание видов данного типа не поддерживается.", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // Формирует имя вида на основе шаблона, заменяя плейсхолдеры
        private string GenerateViewName(string template, int number, string levelName = null)
        {
            string result = template.Replace("{#}", number.ToString());
            if (levelName != null)
                result = result.Replace("{Level}", levelName);
            return result;
        }

        // Проверяет уникальность имени и при необходимости добавляет суффикс
        private string GetUniqueViewName(string baseName)
        {
            var allViews = new FilteredElementCollector(_doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Select(v => v.Name)
                .ToHashSet();

            if (!allViews.Contains(baseName))
                return baseName;

            int suffix = 1;
            string candidate;
            do
            {
                candidate = baseName + "_" + suffix;
                suffix++;
            } while (allViews.Contains(candidate));
            return candidate;
        }

        private void CreateViews_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = CbViewFamilyTypes.SelectedItem as ViewFamilyTypeItem;
            if (selectedItem == null)
            {
                new InfoWindow280("Выберите тип вида.").ShowDialog();
                return;
            }

            ViewFamilyType viewFamilyType = selectedItem.ViewFamilyType;

            if (!int.TryParse(TbViewCount.Text, out int count) || count <= 0)
            {
                new InfoWindow280("Введите корректное положительное число видов.").ShowDialog();
                return;
            }

            using (Transaction trans = new Transaction(_doc, "Создать виды"))
            {
                trans.Start();

                string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();

                Logger.Initialize("Менеджер листов. Создание листов", DateTime.Now, TNovVersion);


                if (viewFamilyType.ViewFamily == ViewFamily.Drafting)
                {
                    string template = TbDraftingNameTemplate.Text.Trim();
                    if (string.IsNullOrEmpty(template))
                    {
                        new InfoWindow280("Введите шаблон имени.").ShowDialog();
                        trans.RollBack();
                        return;
                    }

                    for (int i = 1; i <= count; i++)
                    {
                        string rawName = GenerateViewName(template, i);
                        string uniqueName = GetUniqueViewName(rawName);
                        ViewDrafting newView = ViewDrafting.Create(_doc, viewFamilyType.Id);
                        newView.Name = uniqueName;
                    }
                }
                else if (viewFamilyType.ViewFamily == ViewFamily.FloorPlan)
                {
                    var selectedLevelItem = CbLevels.SelectedItem as LevelItem;
                    if (selectedLevelItem == null)
                    {
                        new InfoWindow280("Выберите уровень.").ShowDialog();
                        trans.RollBack();
                        return;
                    }
                    Level level = selectedLevelItem.Level;

                    var selectedTemplateItem = CbViewTemplates.SelectedItem as ViewTemplateItem;
                    ElementId templateId = selectedTemplateItem?.ViewTemplate?.Id ?? ElementId.InvalidElementId;

                    string template = TbFloorPlanNameTemplate.Text.Trim();
                    if (string.IsNullOrEmpty(template))
                    {
                        new InfoWindow280("Введите шаблон имени для планов.").ShowDialog();
                        trans.RollBack();
                        return;
                    }

                    for (int i = 1; i <= count; i++)
                    {
                        string rawName = GenerateViewName(template, i, level.Name);
                        string uniqueName = GetUniqueViewName(rawName);
                        ViewPlan newView = ViewPlan.Create(_doc, viewFamilyType.Id, level.Id);
                        newView.Scale = 100;
                        if (templateId != ElementId.InvalidElementId)
                            newView.ViewTemplateId = templateId;
                        newView.Name = uniqueName;

                        Logger.Log($"Создан вид {uniqueName} на уровне {selectedLevelItem.DisplayName}", 1);
                    }
                }
                else
                {
                    //MessageBox.Show("Создание видов данного типа не поддерживается.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    trans.RollBack();
                    return;
                }

                trans.Commit();

                Logger.Log("Завершение работы.", 5);
            }

            var mainWindow = Window.GetWindow(this) as MainWindow;
            mainWindow?.RefreshViewList();

            new InfoWindow280($"Создано видов: {count}.").ShowDialog();
        }
    }
}