using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using OfficeOpenXml;
using Forms = System.Windows.Forms;

namespace TNovViewsSheets
{
    public partial class ImportExcelControl : UserControl
    {
        private UIDocument _uidoc;
        private Document _doc;
        private string _selectedSheetName = "";
        private List<ExcelWorksheetInfo> _worksheets = new List<ExcelWorksheetInfo>();

        public ImportExcelControl(UIDocument uidoc)
        {
            InitializeComponent();
            _uidoc = uidoc;
            _doc = uidoc.Document;
        }

        private void BrowseFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Forms.OpenFileDialog
            {
                Filter = "Excel Files|*.xlsx;*.xls",
                Title = "Выберите файл Excel"
            };
            if (dialog.ShowDialog() == Forms.DialogResult.OK)
            {
                TbExcelFile.Text = dialog.FileName;
                LoadWorksheets(dialog.FileName);
            }
        }

        private void LoadWorksheets(string filePath)
        {
            try
            {
                using (var package = new ExcelPackage(new FileInfo(filePath)))
                {
                    _worksheets.Clear();
                    foreach (var ws in package.Workbook.Worksheets)
                    {
                        _worksheets.Add(new ExcelWorksheetInfo { Name = ws.Name });
                    }
                    CbExcelSheets.ItemsSource = _worksheets;
                    CbExcelSheets.DisplayMemberPath = "Name";
                    if (_worksheets.Count > 0)
                        CbExcelSheets.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Ошибка при чтении файла: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CbExcelSheets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CbExcelSheets.SelectedItem != null)
            {
                var selected = CbExcelSheets.SelectedItem as ExcelWorksheetInfo;
                if (selected != null)
                    _selectedSheetName = selected.Name;
            }
        }

        private string GetUniqueViewName(string baseName)
        {
            var existingNames = new FilteredElementCollector(_doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Select(v => v.Name)
                .ToHashSet();

            if (!existingNames.Contains(baseName))
                return baseName;

            int suffix = 1;
            string candidate;
            do
            {
                candidate = $"{baseName} {suffix}";
                suffix++;
            } while (existingNames.Contains(candidate));
            return candidate;
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            string file = TbExcelFile.Text;
            if (string.IsNullOrEmpty(file) || !File.Exists(file))
            {
                System.Windows.MessageBox.Show("Выберите существующий файл Excel.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(_selectedSheetName))
            {
                System.Windows.MessageBox.Show("Выберите лист для импорта.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            double scale;
            if (!double.TryParse(TbScale.Text, out scale) || scale <= 0)
                scale = 1.0;

            List<List<string>> data = ReadExcelSheetMatrix(_selectedSheetName);
            if (data == null || data.Count == 0)
            {
                System.Windows.MessageBox.Show("Не удалось прочитать данные из листа.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            FilteredElementCollector textTypeCollector = new FilteredElementCollector(_doc).OfClass(typeof(TextNoteType));
            TextNoteType textType = textTypeCollector.FirstOrDefault() as TextNoteType;
            if (textType == null)
            {
                System.Windows.MessageBox.Show("В проекте нет ни одного типа текста. Создайте текстовый стиль и повторите импорт.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ElementId textTypeId = textType.Id;

            ViewFamilyType draftingViewType = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.Drafting);
            if (draftingViewType == null)
            {
                System.Windows.MessageBox.Show("В проекте нет типа чертёжного вида.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string viewName = TbViewName.Text.Trim();
            if (string.IsNullOrEmpty(viewName))
                viewName = _selectedSheetName;
            viewName = GetUniqueViewName(viewName);

            using (Transaction trans = new Transaction(_doc, "Импорт из Excel"))
            {
                trans.Start();

                ViewDrafting view = ViewDrafting.Create(_doc, draftingViewType.Id);
                view.Name = viewName;
                view.Scale = (int)scale;

                _doc.Regenerate();

                CreateTableOnView(view, data, scale, textTypeId);

                trans.Commit();
            }

            System.Windows.MessageBox.Show("Импорт завершён. Таблица создана на чертёжном виде.", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private List<List<string>> ReadExcelSheetMatrix(string sheetName)
        {
            var result = new List<List<string>>();
            string file = TbExcelFile.Text;
            if (string.IsNullOrEmpty(file) || !File.Exists(file))
                return result;

            using (var package = new ExcelPackage(new FileInfo(file)))
            {
                var worksheet = package.Workbook.Worksheets[sheetName];
                if (worksheet == null)
                    return result;

                int row = 1;
                while (true)
                {
                    var firstCell = worksheet.Cells[row, 1];
                    bool hasValue = firstCell.Value != null || !string.IsNullOrEmpty(firstCell.Text);
                    if (!hasValue)
                        break;

                    var rowValues = new List<string>();
                    int col = 1;
                    while (true)
                    {
                        var cell = worksheet.Cells[row, col];
                        bool cellHasValue = cell.Value != null || !string.IsNullOrEmpty(cell.Text);
                        if (!cellHasValue)
                            break;
                        rowValues.Add(cell.Text);
                        col++;
                    }
                    result.Add(rowValues);
                    row++;
                }
            }
            return result;
        }

        private void CreateTableOnView(ViewDrafting view, List<List<string>> data, double scale, ElementId textTypeId)
        {
            double startX = 1.0;
            double startY = 10.0;
            double baseCellWidth = 0.5;
            double baseCellHeight = 0.2;
            double baseTextSize = 0.125;

            double cellWidth = baseCellWidth * scale;
            double cellHeight = baseCellHeight * scale;
            double textSize = baseTextSize * scale;

            int maxCols = data.Max(row => row.Count);

            for (int i = 0; i < data.Count; i++)
            {
                for (int j = 0; j < maxCols; j++)
                {
                    string cellText = (j < data[i].Count) ? data[i][j] : "";
                    double x = startX + j * cellWidth;
                    double y = startY - i * cellHeight;

                    XYZ center = new XYZ(x + cellWidth / 2, y - cellHeight / 2, 0);

                    TextNote note = TextNote.Create(_doc, view.Id, center, cellText, textTypeId);
                    if (note != null)
                    {
                        Parameter sizeParam = note.get_Parameter(BuiltInParameter.TEXT_SIZE);
                        if (sizeParam != null && !sizeParam.IsReadOnly)
                            sizeParam.Set(textSize);
                    }

                    // Линии сетки
                    if (i == 0)
                    {
                        Line topLine = Line.CreateBound(new XYZ(x, y, 0), new XYZ(x + cellWidth, y, 0));
                        _doc.Create.NewDetailCurve(view, topLine);
                    }
                    if (j == 0)
                    {
                        Line leftLine = Line.CreateBound(new XYZ(x, y, 0), new XYZ(x, y - cellHeight, 0));
                        _doc.Create.NewDetailCurve(view, leftLine);
                    }
                    Line rightLine = Line.CreateBound(new XYZ(x + cellWidth, y, 0), new XYZ(x + cellWidth, y - cellHeight, 0));
                    _doc.Create.NewDetailCurve(view, rightLine);
                    Line bottomLine = Line.CreateBound(new XYZ(x, y - cellHeight, 0), new XYZ(x + cellWidth, y - cellHeight, 0));
                    _doc.Create.NewDetailCurve(view, bottomLine);
                }
            }
        }
    }

    public class ExcelWorksheetInfo
    {
        public string Name { get; set; }
    }
}