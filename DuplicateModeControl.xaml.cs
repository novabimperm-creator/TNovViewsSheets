using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TNovCommon;

namespace TNovViewsSheets
{
    public partial class DuplicateModeControl : UserControl
    {
        private UIDocument _uidoc;
        private Document _doc;
        private ObservableCollection<SheetInfo> _sheets;
        private List<TitleBlockItem> _titleBlockItems;
        private DataGrid _sheetsListView;

        public DuplicateModeControl(UIDocument uidoc, ObservableCollection<SheetInfo> sheets,
                                     List<TitleBlockItem> titleBlockItems, DataGrid sheetsListView)
        {
            InitializeComponent();
            _uidoc = uidoc;
            _doc = uidoc.Document;
            _sheets = sheets;
            _titleBlockItems = titleBlockItems;
            _sheetsListView = sheetsListView;
        }

        private int GetNextSheetNumber()
        {
            var allSheets = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>();

            var numbers = allSheets
                .Select(s => s.SheetNumber)
                .Where(n => int.TryParse(n, out _))
                .Select(int.Parse);

            return numbers.Any() ? numbers.Max() + 1 : 1;
        }

        private void DuplicateSingleSheet(ViewSheet sourceSheet, string newNumber, string newName,
                                          FamilyInstance titleBlockInstance, ElementId titleBlockTypeId)
        {
            ViewSheet newSheet = ViewSheet.Create(_doc, titleBlockTypeId);
            newSheet.SheetNumber = newNumber;
            if (!string.IsNullOrEmpty(newName))
                newSheet.Name = newName;

            ICollection<ElementId> elementsToCopy = new FilteredElementCollector(_doc, sourceSheet.Id)
                .WhereElementIsNotElementType()
                .ToElementIds()
                .ToList();

            elementsToCopy.Remove(titleBlockInstance.Id);

            if (elementsToCopy.Any())
            {
                ElementTransformUtils.CopyElements(sourceSheet, elementsToCopy, newSheet, null, null);
            }

            CopyParameters(sourceSheet, newSheet);
        }

        private void DuplicateSheets_Click(object sender, RoutedEventArgs e)
        {
            var selectedInfo = _sheetsListView.SelectedItem as SheetInfo;
            if (selectedInfo == null)
            {
                new InfoWindow280("Выберите исходный лист в списке справа.").ShowDialog();
                return;
            }

            ViewSheet sourceSheet = _doc.GetElement(selectedInfo.Id) as ViewSheet;
            if (sourceSheet == null) return;

            if (!int.TryParse(TbCopyCount.Text, out int count) || count <= 0)
            {
                new InfoWindow280("Введите корректное положительное число копий.").ShowDialog();
                return;
            }

            string startNumberText = TbStartNumber.Text.Trim();
            int startNumber;
            if (string.IsNullOrEmpty(startNumberText))
            {
                startNumber = GetNextSheetNumber();
            }
            else if (!int.TryParse(startNumberText, out startNumber))
            {
                new InfoWindow280("Начальный номер должен быть целым числом.").ShowDialog();
                return;
            }

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

            using (Transaction trans = new Transaction(_doc, "Дублировать листы"))
            {
                trans.Start();
                for (int i = 0; i < count; i++)
                {
                    int currentNumber = startNumber + i;
                    string sheetNumber = currentNumber.ToString();
                    string sheetName = $"Лист {currentNumber}";
                    DuplicateSingleSheet(sourceSheet, sheetNumber, sheetName, titleBlockInstance, titleBlockTypeId);
                }
                trans.Commit();
            }

            var mainWindow = Window.GetWindow(this) as MainWindow;
            mainWindow?.RefreshSheetList("");
            mainWindow?.LoadFilterSets();

            new InfoWindow280($"Создано дубликатов: {count}.").ShowDialog();
        }

        private void CopyParameters(Element source, Element dest)
        {
            var excludedParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Sheet Number", "Sheet Name", "Лист", "Номер листа", "Имя листа", "VIEW_NAME"
            };

            foreach (Parameter srcParam in source.Parameters)
            {
                if (srcParam.IsReadOnly) continue;
                string paramName = srcParam.Definition.Name;
                if (excludedParams.Contains(paramName)) continue;

                Parameter destParam = dest.LookupParameter(paramName);
                if (destParam == null || destParam.IsReadOnly) continue;

                switch (srcParam.StorageType)
                {
                    case StorageType.Integer: destParam.Set(srcParam.AsInteger()); break;
                    case StorageType.Double: destParam.Set(srcParam.AsDouble()); break;
                    case StorageType.String: destParam.Set(srcParam.AsString()); break;
                    case StorageType.ElementId: destParam.Set(srcParam.AsElementId()); break;
                }
            }
        }
    }
}