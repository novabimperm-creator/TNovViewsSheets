using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Binding = System.Windows.Data.Binding;
using MessageBox = System.Windows.MessageBox;
using WpfVisibility = System.Windows.Visibility;

namespace TNovViewsSheets
{
    public partial class SheetSelectionWindow : Window
    {
        private const string AllStagesLabel = "(все)";
        private const string NoStageLabel = "(без стадии)";

        private readonly List<SheetItem> _allSheets;
        private readonly Document _doc;
        private readonly Dictionary<ElementId, BitmapSource> _previewCache =
            new Dictionary<ElementId, BitmapSource>();
        private string _lastSortColumn;
        private ListSortDirection _lastSortDirection = ListSortDirection.Ascending;

        private bool _previewExpanded;
        private GridLength _savedSheetListWidth;
        private GridLength _savedSplitterWidth;
        private GridLength _savedPreviewWidth;
        private double _savedSheetListMinWidth;
        private double _savedPreviewMinWidth;

        public ExportOptions Result { get; private set; }

        public SheetSelectionWindow(Document doc, List<SheetItem> sheets, IEnumerable<string> exportSetupNames)
        {
            InitializeComponent();
            _doc = doc;
            _allSheets = sheets ?? new List<SheetItem>();

            SheetsList.ItemsSource = _allSheets;

            // React to checkbox changes to update the count label
            foreach (var s in _allSheets)
                s.PropertyChanged += SheetItem_PropertyChanged;

            // Populate export setup combo
            ExportSetupCombo.Items.Add("<Default DWG export setup>");
            foreach (var name in exportSetupNames ?? Enumerable.Empty<string>())
                ExportSetupCombo.Items.Add(name);
            ExportSetupCombo.SelectedIndex = 0;

            PopulateStageCombo();

            UpdateCountLabel();
            ApplySort("SheetNumber", ListSortDirection.Ascending);
        }

        private void PreviewGroup_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return;
            TogglePreviewExpanded();
            e.Handled = true;
        }

        // Hide the sheet list + splitter and stretch the preview across the whole
        // body. A second double-click restores the saved column widths.
        private void TogglePreviewExpanded()
        {
            if (!_previewExpanded)
            {
                _savedSheetListWidth = SheetListColumn.Width;
                _savedSplitterWidth = SplitterColumn.Width;
                _savedPreviewWidth = PreviewColumn.Width;
                _savedSheetListMinWidth = SheetListColumn.MinWidth;
                _savedPreviewMinWidth = PreviewColumn.MinWidth;

                SheetListColumn.MinWidth = 0;
                PreviewColumn.MinWidth = 0;
                SheetListColumn.Width = new GridLength(0);
                SplitterColumn.Width = new GridLength(0);
                PreviewColumn.Width = new GridLength(1, GridUnitType.Star);

                PreviewSplitter.Visibility = WpfVisibility.Collapsed;
                SheetListGroup.Visibility = WpfVisibility.Collapsed;
                _previewExpanded = true;
            }
            else
            {
                SheetListColumn.Width = _savedSheetListWidth;
                SplitterColumn.Width = _savedSplitterWidth;
                PreviewColumn.Width = _savedPreviewWidth;
                SheetListColumn.MinWidth = _savedSheetListMinWidth;
                PreviewColumn.MinWidth = _savedPreviewMinWidth;

                PreviewSplitter.Visibility = WpfVisibility.Visible;
                SheetListGroup.Visibility = WpfVisibility.Visible;
                _previewExpanded = false;
            }
        }

        private void PopulateStageCombo()
        {
            StageCombo.Items.Add(AllStagesLabel);

            var hasBlank = _allSheets.Any(s => string.IsNullOrWhiteSpace(s.Stage));
            var stages = _allSheets
                .Select(s => s.Stage)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .OrderBy(s => s, NaturalStringComparer.Instance);

            foreach (var st in stages)
                StageCombo.Items.Add(st);

            if (hasBlank)
                StageCombo.Items.Add(NoStageLabel);

            StageCombo.SelectedIndex = 0;
        }

        private void SheetItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SheetItem.IsSelected))
                UpdateCountLabel();
        }

        private void UpdateCountLabel()
        {
            int total = _allSheets.Count;
            int chosen = _allSheets.Count(s => s.IsSelected);
            int visible = VisibleItems().Count();
            CountLabel.Text = visible == total
                ? $"выбрано {chosen} из {total}"
                : $"выбрано {chosen} из {total} (показано {visible})";
        }

        private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void StageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            ICollectionView view = CollectionViewSource.GetDefaultView(SheetsList.ItemsSource);
            if (view == null) return;

            string q = FilterBox.Text?.Trim() ?? string.Empty;
            string stage = StageCombo.SelectedItem as string;
            bool stageActive = !string.IsNullOrEmpty(stage) && stage != AllStagesLabel;

            if (!stageActive && string.IsNullOrEmpty(q))
            {
                view.Filter = null;
                UpdateCountLabel();
                return;
            }

            view.Filter = obj =>
            {
                if (!(obj is SheetItem si)) return false;

                if (stageActive)
                {
                    string itemStage = string.IsNullOrWhiteSpace(si.Stage) ? NoStageLabel : si.Stage;
                    if (!string.Equals(itemStage, stage, StringComparison.Ordinal))
                        return false;
                }

                if (!string.IsNullOrEmpty(q))
                {
                    bool match = si.SheetNumber.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                              || si.SheetName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                              || (si.Stage?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!match) return false;
                }
                return true;
            };
            UpdateCountLabel();
        }

        private IEnumerable<SheetItem> VisibleItems()
        {
            ICollectionView view = CollectionViewSource.GetDefaultView(SheetsList.ItemsSource);
            if (view == null) return _allSheets;
            return view.Cast<SheetItem>();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var s in VisibleItems()) s.IsSelected = true;
        }

        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var s in VisibleItems()) s.IsSelected = false;
        }

        private void HeaderCheckBox_Click(object sender, RoutedEventArgs e)
        {
            bool target = HeaderCheckBox.IsChecked == true;
            foreach (var s in VisibleItems()) s.IsSelected = target;
        }

        private void CloseHeaderButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SheetsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // SelectionMode=Extended → SelectedItem is the most-recently focused row.
            // We render the preview for that one; multi-select for the export is
            // driven by the IsSelected checkbox, independent of row focus.
            UpdatePreview(SheetsList.SelectedItem as SheetItem);
        }

        private void UpdatePreview(SheetItem item)
        {
            if (item == null || item.IsPlaceholder)
            {
                ApplyPreview(null, item == null
                    ? "Выберите лист, чтобы увидеть превью"
                    : "У placeholder-листа нет содержимого");
                return;
            }

            if (_previewCache.TryGetValue(item.ElementId, out var cached))
            {
                ApplyPreview(cached, null);
                return;
            }

            // Revit's ExportImage rasterizes the sheet — can take 1–3 s on the
            // first call. Show a placeholder text, let WPF repaint, then do the
            // heavy work on the next dispatcher tick so the user sees feedback
            // instead of a frozen window.
            ApplyPreview(null, "Загрузка превью…");

            ElementId targetId = item.ElementId;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                // User may have moved on while we were queued.
                if (!(SheetsList.SelectedItem is SheetItem current) || current.ElementId != targetId)
                    return;

                BitmapSource src = RenderSheetPreview(item);
                if (src != null) _previewCache[targetId] = src;
                ApplyPreview(src, src == null ? "Не удалось получить превью" : null);
            }));
        }

        private void ApplyPreview(BitmapSource src, string hint)
        {
            PreviewImage.Source = src;
            if (src == null)
            {
                PreviewHint.Text = hint ?? string.Empty;
                PreviewHint.Visibility = WpfVisibility.Visible;
            }
            else
            {
                PreviewHint.Visibility = WpfVisibility.Collapsed;
            }
        }

        private BitmapSource RenderSheetPreview(SheetItem item)
        {
            // Use an isolated temp folder per call — Revit suffixes the FilePath
            // with the view name, so we can't predict the final filename. Globbing
            // the folder afterwards is the only reliable way to find the PNG.
            string tmpDir = Path.Combine(Path.GetTempPath(),
                "rsheet_preview_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tmpDir);

            try
            {
                var opts = new ImageExportOptions
                {
                    ExportRange = ExportRange.SetOfViews,
                    FilePath = Path.Combine(tmpDir, "p.png"),
                    HLRandWFViewsFileType = ImageFileType.PNG,
                    ShadowViewsFileType = ImageFileType.PNG,
                    ImageResolution = ImageResolution.DPI_72,
                    PixelSize = 900,
                    FitDirection = FitDirectionType.Horizontal,
                    ZoomType = ZoomFitType.FitToPage,
                };
                opts.SetViewsAndSheets(new List<ElementId> { item.ElementId });
                _doc.ExportImage(opts);

                string produced = Directory.GetFiles(tmpDir, "*.png").FirstOrDefault();
                if (produced == null) return null;
                return LoadAndFreeze(produced);
            }
            catch
            {
                return null;
            }
            finally
            {
                try { Directory.Delete(tmpDir, true); } catch { /* best-effort */ }
            }
        }

        private static BitmapSource LoadAndFreeze(string path)
        {
            using (var fs = File.OpenRead(path))
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.StreamSource = fs;
                img.EndInit();
                img.Freeze();
                return img;
            }
        }

        private void OnHeaderClick(object sender, RoutedEventArgs e)
        {
            if (!(e.OriginalSource is GridViewColumnHeader header)) return;
            if (header.Role == GridViewColumnHeaderRole.Padding) return;
            if (!(header.Column?.DisplayMemberBinding is Binding binding)) return;

            string path = binding.Path?.Path;
            if (string.IsNullOrEmpty(path)) return;

            var direction = (path == _lastSortColumn && _lastSortDirection == ListSortDirection.Ascending)
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

            ApplySort(path, direction);
        }

        private void ApplySort(string property, ListSortDirection direction)
        {
            // ListCollectionView.CustomSort is needed because the default
            // SortDescription compares strings lexicographically, which puts
            // "10" before "2" — useless for sheet numbers. NaturalStringComparer
            // handles digit runs as numbers ("A-2" < "A-10").
            if (!(CollectionViewSource.GetDefaultView(SheetsList.ItemsSource) is ListCollectionView view))
                return;
            view.CustomSort = new SheetColumnComparer(property, direction);
            _lastSortColumn = property;
            _lastSortDirection = direction;
        }

        private class SheetColumnComparer : IComparer
        {
            private readonly string _property;
            private readonly ListSortDirection _direction;

            public SheetColumnComparer(string property, ListSortDirection direction)
            {
                _property = property;
                _direction = direction;
            }

            public int Compare(object x, object y)
            {
                if (!(x is SheetItem a) || !(y is SheetItem b)) return 0;
                int cmp = NaturalStringComparer.Instance.Compare(GetValue(a), GetValue(b));
                return _direction == ListSortDirection.Ascending ? cmp : -cmp;
            }

            private string GetValue(SheetItem s)
            {
                switch (_property)
                {
                    case nameof(SheetItem.SheetNumber): return s.SheetNumber;
                    case nameof(SheetItem.SheetName): return s.SheetName;
                    case nameof(SheetItem.Stage): return s.Stage;
                    case nameof(SheetItem.IsPlaceholder): return s.IsPlaceholder ? "1" : "0";
                    default: return string.Empty;
                }
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var chosen = _allSheets.Where(s => s.IsSelected && !s.IsPlaceholder).ToList();
            int placeholderSelected = _allSheets.Count(s => s.IsSelected && s.IsPlaceholder);

            if (chosen.Count == 0)
            {
                MessageBox.Show(this,
                    placeholderSelected > 0
                        ? "Выбраны только placeholder-листы — они не экспортируются."
                        : "Выберите хотя бы один лист для экспорта.",
                    "Экспорт листов",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool dwg = FormatDwg.IsChecked == true;
            bool pdf = FormatPdf.IsChecked == true;
            if (!dwg && !pdf)
            {
                MessageBox.Show(this,
                    "Выберите хотя бы один формат: DWG или PDF.",
                    "Экспорт листов",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string filter, defaultExt;
            if (dwg && pdf)
            {
                // Both formats — dialog just picks a base name + folder; we append
                // both extensions ourselves. Use a wide-open filter so the typed
                // name isn't auto-suffixed with one specific extension.
                filter = "DWG + PDF (имя без расширения)|*.*";
                defaultExt = string.Empty;
            }
            else if (dwg)
            {
                filter = "AutoCAD Drawing (*.dwg)|*.dwg";
                defaultExt = ".dwg";
            }
            else
            {
                filter = "PDF Document (*.pdf)|*.pdf";
                defaultExt = ".pdf";
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Сохранить как…",
                Filter = filter,
                FileName = SuggestBaseName(_doc),
                DefaultExt = defaultExt,
                AddExtension = !string.IsNullOrEmpty(defaultExt)
            };
            if (dlg.ShowDialog(this) != true) return;

            string setup = ExportSetupCombo.SelectedIndex > 0
                ? ExportSetupCombo.SelectedItem?.ToString()
                : null;

            ExportColorMode colors = ExportColorMode.TrueColor;
            if (ColorModeCombo.SelectedItem is ComboBoxItem cbi
                && cbi.Tag is string tag
                && Enum.TryParse(tag, out ExportColorMode parsed))
            {
                colors = parsed;
            }

            string folder = Path.GetDirectoryName(dlg.FileName) ?? string.Empty;
            string nameNoExt = Path.GetFileNameWithoutExtension(dlg.FileName);
            string basePath = Path.Combine(folder, nameNoExt);

            Result = new ExportOptions
            {
                SelectedSheets = chosen,
                ExportDwg = dwg,
                ExportPdf = pdf,
                Mode = ModeTiled.IsChecked == true ? ExportMode.TiledModelSpace : ExportMode.MultiLayout,
                OutputBasePath = basePath,
                DwgExportSetupName = setup,
                Colors = colors,
                PdfFitToSheetSize = PdfFitToSheet.IsChecked == true,
                PdfColor = (bool)PdfColor.IsChecked,
                PdfQuality = (bool)PdfQuality.IsChecked,
                PdfRastr = (bool)PdfRastr.IsChecked
            };

            DialogResult = true;
            Close();
        }

        private static string SuggestBaseName(Document doc)
        {
            string baseName = doc?.Title ?? "Project";
            string noExt = Path.GetFileNameWithoutExtension(baseName);
            if (string.IsNullOrWhiteSpace(noExt)) noExt = "Project";
            return noExt + "_Sheets";
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            string commandText = @"https://portal.talan.group/knowledge/proektirovanie/eksportpdfidwgizrevit/";
            var proc = new System.Diagnostics.Process();
            proc.StartInfo.FileName = commandText;
            proc.StartInfo.UseShellExecute = true;
            proc.Start();
        }

        private void Border_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {

        }
    }
}
