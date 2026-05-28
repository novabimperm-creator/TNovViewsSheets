using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using OfficeOpenXml;
using Newtonsoft.Json;
using Microsoft.VisualBasic;

namespace TNovViewsSheets
{
    public class ScheduleSet
    {
        public string Name { get; set; }
        public List<string> ScheduleIds { get; set; } = new List<string>();
    }

    public class ExportConfig
    {
        public string Name { get; set; }
        public string OutputFolder { get; set; }
        public bool SeparateFiles { get; set; }
        public string FilePrefix { get; set; }
        public string FileName { get; set; }
        public bool ExportHeader { get; set; }
        public bool KeepFormatting { get; set; }
    }

    public partial class ExportSchedulesControl : UserControl
    {
        private UIDocument _uidoc;
        private Document _doc;
        private ObservableCollection<ScheduleInfo> _schedules;
        private List<ScheduleSet> _sets = new List<ScheduleSet>();
        private List<ExportConfig> _configs = new List<ExportConfig>();
        private string _configPath;

        public ExportSchedulesControl(UIDocument uidoc, ObservableCollection<ScheduleInfo> schedules)
        {
            InitializeComponent();
            _uidoc = uidoc;
            _doc = uidoc.Document;
            _schedules = schedules;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "SheetManager");
            Directory.CreateDirectory(folder);
            _configPath = Path.Combine(folder, "export_config.json");

            LoadData();
        }

        private void LoadData()
        {
            if (File.Exists(_configPath))
            {
                try
                {
                    string json = File.ReadAllText(_configPath);
                    var data = JsonConvert.DeserializeAnonymousType(json, new { Sets = new List<ScheduleSet>(), Configs = new List<ExportConfig>() });
                    if (data != null)
                    {
                        _sets = data.Sets ?? new List<ScheduleSet>();
                        _configs = data.Configs ?? new List<ExportConfig>();
                    }
                }
                catch { }
            }

            CbScheduleSets.ItemsSource = _sets;
            CbScheduleSets.DisplayMemberPath = "Name";
            if (_sets.Any()) CbScheduleSets.SelectedIndex = 0;

            CbConfigs.ItemsSource = _configs;
            CbConfigs.DisplayMemberPath = "Name";
            if (_configs.Any()) CbConfigs.SelectedIndex = 0;
        }

        private void SaveData()
        {
            var data = new { Sets = _sets, Configs = _configs };
            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(_configPath, json);
        }

        // Вспомогательный метод для удаления существующего файла
        private void EnsureFileNotExists(string filePath)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        // Работа с наборами
        private void AddSet_Click(object sender, RoutedEventArgs e)
        {
            string name = Interaction.InputBox("Введите имя нового набора:", "Создание набора", "Новый набор");
            if (string.IsNullOrWhiteSpace(name)) return;
            if (_sets.Any(s => s.Name == name))
            {
                MessageBox.Show("Набор с таким именем уже существует.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _sets.Add(new ScheduleSet { Name = name });
            SaveData();
            CbScheduleSets.Items.Refresh();
            CbScheduleSets.SelectedItem = _sets.Last();
        }

        private void RenameSet_Click(object sender, RoutedEventArgs e)
        {
            if (CbScheduleSets.SelectedItem == null) return;
            var set = CbScheduleSets.SelectedItem as ScheduleSet;
            string newName = Interaction.InputBox("Введите новое имя набора:", "Переименование", set.Name);
            if (string.IsNullOrWhiteSpace(newName) || newName == set.Name) return;
            if (_sets.Any(s => s.Name == newName))
            {
                MessageBox.Show("Набор с таким именем уже существует.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            set.Name = newName;
            SaveData();
            CbScheduleSets.Items.Refresh();
        }

        private void DeleteSet_Click(object sender, RoutedEventArgs e)
        {
            if (CbScheduleSets.SelectedItem == null) return;
            var set = CbScheduleSets.SelectedItem as ScheduleSet;
            if (MessageBox.Show($"Удалить набор '{set.Name}'?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _sets.Remove(set);
                SaveData();
                CbScheduleSets.Items.Refresh();
            }
        }

        private void SaveSet_Click(object sender, RoutedEventArgs e)
        {
            if (CbScheduleSets.SelectedItem == null) return;
            var set = CbScheduleSets.SelectedItem as ScheduleSet;
            set.ScheduleIds = _schedules.Where(s => s.IsSelected).Select(s => s.Id.ToString()).ToList();
            SaveData();
            MessageBox.Show("Набор сохранён.", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CbScheduleSets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CbScheduleSets.SelectedItem == null) return;
            var set = CbScheduleSets.SelectedItem as ScheduleSet;
            foreach (var s in _schedules) s.IsSelected = false;
            if (set.ScheduleIds != null)
            {
                var ids = new HashSet<ElementId>();
                foreach (string idStr in set.ScheduleIds)
                {
                    if (int.TryParse(idStr, out int intId))
                        ids.Add(new ElementId(intId));
                }
                foreach (var s in _schedules)
                    if (ids.Contains(s.Id))
                        s.IsSelected = true;
            }
            (Window.GetWindow(this) as MainWindow)?.UpdateSelectedSchedulesCount();
        }

        // Работа с конфигурациями
        private void AddConfig_Click(object sender, RoutedEventArgs e)
        {
            string name = Interaction.InputBox("Введите имя новой конфигурации:", "Создание конфигурации", "Конфигурация 1");
            if (string.IsNullOrWhiteSpace(name)) return;
            if (_configs.Any(c => c.Name == name))
            {
                MessageBox.Show("Конфигурация с таким именем уже существует.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var newConfig = new ExportConfig
            {
                Name = name,
                OutputFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                SeparateFiles = false,
                FilePrefix = "",
                FileName = "export",
                ExportHeader = true,
                KeepFormatting = true
            };
            _configs.Add(newConfig);
            SaveData();
            CbConfigs.Items.Refresh();
            CbConfigs.SelectedItem = newConfig;
        }

        private void RenameConfig_Click(object sender, RoutedEventArgs e)
        {
            if (CbConfigs.SelectedItem == null) return;
            var config = CbConfigs.SelectedItem as ExportConfig;
            string newName = Interaction.InputBox("Введите новое имя конфигурации:", "Переименование", config.Name);
            if (string.IsNullOrWhiteSpace(newName) || newName == config.Name) return;
            if (_configs.Any(c => c.Name == newName))
            {
                MessageBox.Show("Конфигурация с таким именем уже существует.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            config.Name = newName;
            SaveData();
            CbConfigs.Items.Refresh();
        }

        private void DeleteConfig_Click(object sender, RoutedEventArgs e)
        {
            if (CbConfigs.SelectedItem == null) return;
            var config = CbConfigs.SelectedItem as ExportConfig;
            if (MessageBox.Show($"Удалить конфигурацию '{config.Name}'?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _configs.Remove(config);
                SaveData();
                CbConfigs.Items.Refresh();
            }
        }

        private void SaveConfig_Click(object sender, RoutedEventArgs e)
        {
            if (CbConfigs.SelectedItem == null) return;
            var config = CbConfigs.SelectedItem as ExportConfig;
            config.OutputFolder = TbOutputFolder.Text;
            config.SeparateFiles = ChkSeparateFiles.IsChecked ?? false;
            config.FilePrefix = TbFilePrefix.Text;
            config.FileName = TbFileName.Text;
            config.ExportHeader = ChkExportHeader.IsChecked ?? false;
            config.KeepFormatting = ChkKeepFormatting.IsChecked ?? false;
            SaveData();
            MessageBox.Show("Конфигурация сохранена.", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CbConfigs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CbConfigs.SelectedItem == null) return;
            var config = CbConfigs.SelectedItem as ExportConfig;
            TbOutputFolder.Text = config.OutputFolder;
            ChkSeparateFiles.IsChecked = config.SeparateFiles;
            TbFilePrefix.Text = config.FilePrefix;
            TbFileName.Text = config.FileName;
            ChkExportHeader.IsChecked = config.ExportHeader;
            ChkKeepFormatting.IsChecked = config.KeepFormatting;
        }

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.SelectedPath = TbOutputFolder.Text;
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    TbOutputFolder.Text = dialog.SelectedPath;
            }
        }

        // Экспорт
        private void ExportCurrent_Click(object sender, RoutedEventArgs e)
        {
            var selected = _schedules.Where(s => s.IsSelected).FirstOrDefault();
            if (selected == null)
            {
                MessageBox.Show("Нет выбранных спецификаций.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ExportSingle(selected);
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var selected = _schedules.Where(s => s.IsSelected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Нет выбранных спецификаций.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ExportMultiple(selected);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = Window.GetWindow(this) as MainWindow;
            mainWindow?.BtnStart.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        private void ExportSingle(ScheduleInfo schedule)
        {
            var config = CbConfigs.SelectedItem as ExportConfig ?? new ExportConfig { OutputFolder = TbOutputFolder.Text };
            string folder = config.OutputFolder;
            if (string.IsNullOrEmpty(folder)) folder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            string fileName;
            if (!string.IsNullOrWhiteSpace(TbFileName.Text))
                fileName = TbFileName.Text;
            else
                fileName = schedule.Name;

            foreach (char c in Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(c, '_');

            string fullPath = Path.Combine(folder, fileName + ".xlsx");
            EnsureFileNotExists(fullPath);
            ExportScheduleToExcel(schedule, fullPath, config.ExportHeader, config.KeepFormatting);
            MessageBox.Show($"Экспорт завершён:\n{fullPath}", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ExportMultiple(List<ScheduleInfo> schedules)
        {
            var config = CbConfigs.SelectedItem as ExportConfig ?? new ExportConfig { OutputFolder = TbOutputFolder.Text, SeparateFiles = true };
            string folder = config.OutputFolder;
            if (string.IsNullOrEmpty(folder)) folder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            if (config.SeparateFiles)
            {
                string prefix = config.FilePrefix;
                foreach (var s in schedules)
                {
                    string fileName = string.IsNullOrEmpty(prefix) ? s.Name : prefix + s.Name;
                    foreach (char c in Path.GetInvalidFileNameChars()) fileName = fileName.Replace(c, '_');
                    string fullPath = Path.Combine(folder, fileName + ".xlsx");
                    EnsureFileNotExists(fullPath);
                    ExportScheduleToExcel(s, fullPath, config.ExportHeader, config.KeepFormatting);
                }
                MessageBox.Show($"Экспортировано {schedules.Count} файлов в папку:\n{folder}", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                string fileName = config.FileName;
                if (string.IsNullOrEmpty(fileName)) fileName = "Export";
                foreach (char c in Path.GetInvalidFileNameChars()) fileName = fileName.Replace(c, '_');
                string fullPath = Path.Combine(folder, fileName + ".xlsx");
                EnsureFileNotExists(fullPath);

                using (var package = new ExcelPackage(new FileInfo(fullPath)))
                {
                    foreach (var s in schedules)
                    {
                        var table = GetScheduleData(s);
                        if (table == null) continue;

                        string baseSheetName = s.Name.Length > 31 ? s.Name.Substring(0, 31) : s.Name;
                        string sheetName = GetUniqueSheetName(package, baseSheetName);
                        var worksheet = package.Workbook.Worksheets.Add(sheetName);

                        for (int i = 0; i < table.Rows.Count; i++)
                            for (int j = 0; j < table.Columns.Count; j++)
                                worksheet.Cells[i + 1, j + 1].Value = table.Rows[i][j];
                        
                        worksheet.Cells.AutoFitColumns();
                    }
                    package.Save();
                }
                MessageBox.Show($"Экспорт завершён в файл:\n{fullPath}", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private string GetUniqueSheetName(ExcelPackage package, string baseName)
        {
            var existingSheets = package.Workbook.Worksheets.Select(ws => ws.Name).ToHashSet();
            if (!existingSheets.Contains(baseName))
                return baseName;

            int suffix = 1;
            string candidate;
            do
            {
                candidate = $"{baseName}_{suffix}";
                suffix++;
            } while (existingSheets.Contains(candidate));
            return candidate;
        }

        // Обновлённый метод: читает все строки без обработки заголовков
        private System.Data.DataTable GetScheduleData(ScheduleInfo schedule)
        {
            var viewSchedule = _doc.GetElement(schedule.Id) as ViewSchedule;
            if (viewSchedule == null) return null;

            string tempFile = Path.GetTempFileName() + ".txt";
            try
            {
                var options = new ViewScheduleExportOptions(); // настройки по умолчанию
                viewSchedule.Export(Path.GetDirectoryName(tempFile), Path.GetFileName(tempFile), options);
            }
            catch
            {
                return null;
            }

            var table = new System.Data.DataTable();
            using (var reader = new StreamReader(tempFile, Encoding.Default))
            {
                string line;
                bool firstRow = true;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split('\t');
                    if (firstRow)
                    {
                        // Создаём столбцы с именами Col1, Col2, ... чтобы избежать дубликатов
                        for (int i = 0; i < parts.Length; i++)
                        {
                            table.Columns.Add($"Col{i + 1}");
                        }
                        firstRow = false;
                    }
                    var row = table.NewRow();
                    for (int i = 0; i < parts.Length && i < table.Columns.Count; i++)
                    {
                        row[i] = parts[i];
                    }
                    table.Rows.Add(row);
                }
            }
            try { File.Delete(tempFile); } catch { }
            return table;
        }

        private void ExportScheduleToExcel(ScheduleInfo schedule, string filePath, bool includeHeader, bool keepFormatting)
        {
            var table = GetScheduleData(schedule);
            if (table == null) return;

            EnsureFileNotExists(filePath);
            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                var worksheet = package.Workbook.Worksheets.Add("Sheet1");
                // Записываем все строки таблицы
                for (int i = 0; i < table.Rows.Count; i++)
                    for (int j = 0; j < table.Columns.Count; j++)
                        worksheet.Cells[i + 1, j + 1].Value = table.Rows[i][j];

                // Параметры includeHeader и keepFormatting игнорируются, так как данные уже содержат все строки.
                worksheet.Cells.AutoFitColumns();
                package.Save();
            }
        }
    }
}