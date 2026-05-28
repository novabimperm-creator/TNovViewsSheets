using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Forms = System.Windows.Forms;
using iTextSharp.text;
using iTextSharp.text.pdf;

namespace TNovViewsSheets
{
    public partial class ExportPdfControl : UserControl
    {
        private UIDocument _uidoc;
        private Autodesk.Revit.DB.Document _doc;
        private ObservableCollection<ExportableItem> _exportItems;

        public ExportPdfControl(UIDocument uidoc, ObservableCollection<ExportableItem> exportItems)
        {
            InitializeComponent();
            _uidoc = uidoc;
            _doc = uidoc.Document;
            _exportItems = exportItems;

            TbOutputFolder.Text = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new Forms.FolderBrowserDialog())
            {
                dialog.SelectedPath = TbOutputFolder.Text;
                if (dialog.ShowDialog() == Forms.DialogResult.OK)
                    TbOutputFolder.Text = dialog.SelectedPath;
            }
        }

        private void Format_Checked(object sender, RoutedEventArgs e)
        {
            if (ChkCombine == null || ExpanderOptions == null || CombineFileNamePanel == null)
                return;

            bool isPdf = RbFormatPdf.IsChecked == true;
            ChkCombine.IsEnabled = isPdf;
            ExpanderOptions.Visibility = isPdf
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
            CombineFileNamePanel.Visibility = (isPdf && ChkCombine.IsChecked == true)
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var selected = _exportItems.Where(item => item.IsSelected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Нет выбранных листов для экспорта.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string folder = TbOutputFolder.Text;
            if (string.IsNullOrEmpty(folder))
                folder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            if (RbFormatPdf.IsChecked == true)
                ExportToPdf(selected, folder);
            else
                ExportToDwg(selected, folder);
        }

        #region PDF Export (через PrintManager + iTextSharp)

        private void ExportToPdf(List<ExportableItem> selected, string folder)
        {
            bool combine = ChkCombine.IsChecked ?? false;
            string scaleOption = (CbScale.SelectedItem as ComboBoxItem)?.Content.ToString();
            bool portrait = RbPortrait.IsChecked ?? true;

            // Проверка прав на запись
            try
            {
                string testFile = Path.Combine(folder, "write_test.tmp");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Нет прав на запись в папку:\n{folder}\n\nОшибка: {ex.Message}",
                                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                if (combine)
                {
                    string finalFileName = TbCombineFileName.Text.Trim();
                    if (string.IsNullOrWhiteSpace(finalFileName))
                        finalFileName = "Combined_Export";
                    string finalPath = Path.Combine(folder, finalFileName + ".pdf");

                    List<string> tempFiles = new List<string>();
                    int successCount = 0;

                    foreach (var sheet in selected)
                    {
                        string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf");
                        bool printed = ExportSingleSheetViaPrint(sheet, tempFile, scaleOption, portrait);
                        if (printed && File.Exists(tempFile) && new FileInfo(tempFile).Length > 0)
                        {
                            tempFiles.Add(tempFile);
                            successCount++;
                        }
                    }

                    if (successCount == 0)
                    {
                        MessageBox.Show("Ни один временный PDF не был создан. Объединение невозможно.", "Ошибка");
                        return;
                    }

                    if (successCount < selected.Count)
                        MessageBox.Show($"Создано только {successCount} из {selected.Count} временных файлов. Объединяем доступные.", "Предупреждение");

                    MergePdfFiles(tempFiles, finalPath);

                    foreach (var f in tempFiles)
                    {
                        try { File.Delete(f); } catch { }
                    }

                    if (File.Exists(finalPath))
                        MessageBox.Show($"Экспорт завершён. Объединённый файл сохранён:\n{finalPath}",
                                        "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
                    else
                        MessageBox.Show($"Файл не был создан по неизвестной причине.", "Ошибка");
                }
                else
                {
                    int successCount = 0;
                    foreach (var sheet in selected)
                    {
                        string fileName = SanitizeFileName(sheet.Name);
                        string filePath = Path.Combine(folder, fileName + ".pdf");
                        bool printed = ExportSingleSheetViaPrint(sheet, filePath, scaleOption, portrait);
                        if (printed)
                            successCount++;
                    }
                    MessageBox.Show($"Экспорт завершён. Создано файлов: {successCount} из {selected.Count}.\nПапка: {folder}",
                                    "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при экспорте PDF: {ex.Message}",
                                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ExportSingleSheetViaPrint(ExportableItem sheet, string filePath, string scaleOption, bool portrait)
        {
            ViewSheet viewSheet = _doc.GetElement(sheet.Id) as ViewSheet;
            if (viewSheet == null)
            {
                MessageBox.Show($"Лист с ID {sheet.Id} не найден.", "Ошибка");
                return false;
            }

            // Активируем вид вне транзакции (это необходимо для корректной печати)
            try
            {
                if (_uidoc.ActiveView != viewSheet)
                    _uidoc.ActiveView = viewSheet;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось активировать вид: {ex.Message}", "Предупреждение");
                // Продолжаем, возможно, и так сработает
            }

            try
            {
                using (Transaction trans = new Transaction(_doc, "Экспорт листа в PDF"))
                {
                    trans.Start();

                    PrintManager printManager = _doc.PrintManager;
                    printManager.SelectNewPrintDriver("Microsoft Print to PDF");
                    printManager.PrintToFile = true;
                    printManager.PrintToFileName = filePath;
                    printManager.PrintRange = PrintRange.Current;

                    PrintSetup printSetup = printManager.PrintSetup;
                    printSetup.CurrentPrintSetting.PrintParameters.PageOrientation = portrait
                        ? PageOrientationType.Portrait
                        : PageOrientationType.Landscape;

                    // Всегда используем масштабирование по размеру страницы (Fit to Page)
                    printSetup.CurrentPrintSetting.PrintParameters.ZoomType = ZoomType.FitToPage;

                    // Если пользователь выбрал конкретный процент, применяем его
                    if (scaleOption != null && scaleOption.EndsWith("%") &&
                        int.TryParse(scaleOption.TrimEnd('%'), out int zoomPercent))
                    {
                        printSetup.CurrentPrintSetting.PrintParameters.ZoomType = ZoomType.Zoom;
                        printSetup.CurrentPrintSetting.PrintParameters.Zoom = zoomPercent;
                    }

                    printManager.Apply();
                    viewSheet.Print();

                    trans.Commit();
                }

                // Даём время на запись файла
                System.Threading.Thread.Sleep(500);

                return File.Exists(filePath) && new FileInfo(filePath).Length > 0;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("Microsoft Print to PDF") || ex.Message.Contains("driver"))
                    MessageBox.Show($"Принтер 'Microsoft Print to PDF' не доступен. Убедитесь, что он установлен.\n\nДетали: {ex.Message}", "Ошибка принтера");
                else
                    MessageBox.Show($"Ошибка при печати листа '{sheet.Name}': {ex.Message}", "Ошибка");
                return false;
            }
        }

        private void MergePdfFiles(List<string> sourceFiles, string destinationFile)
        {
            try
            {
                using (FileStream stream = new FileStream(destinationFile, FileMode.Create))
                using (iTextSharp.text.Document document = new iTextSharp.text.Document())
                using (iTextSharp.text.pdf.PdfCopy copy = new iTextSharp.text.pdf.PdfCopy(document, stream))
                {
                    document.Open();
                    foreach (string file in sourceFiles)
                    {
                        using (iTextSharp.text.pdf.PdfReader reader = new iTextSharp.text.pdf.PdfReader(file))
                        {
                            for (int i = 1; i <= reader.NumberOfPages; i++)
                            {
                                copy.AddPage(copy.GetImportedPage(reader, i));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при объединении PDF: {ex.Message}", "Ошибка");
            }
        }

        private string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        #endregion

        #region DWG Export

        private void ExportToDwg(List<ExportableItem> selected, string folder)
        {
            try
            {
                var ids = selected.Select(item => item.Id).ToList();
                var dwgOptions = new DWGExportOptions();
                _doc.Export(folder, "", ids, dwgOptions);

                MessageBox.Show($"Экспорт DWG завершён. Файлы сохранены в:\n{folder}",
                                "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при экспорте DWG: {ex.Message}",
                                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion
    }
}