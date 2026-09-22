using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.UI;
using TNovCommon;

namespace TNovViewsSheets
{
    public partial class ChangesManagerWindow : Window
    {
        readonly ChangesManagerViewModel _vm;
        readonly ChangesRevisionsControl _revisionsControl;
        readonly ChangesSheetsControl _sheetsControl;
        readonly ChangesVedomostControl _vedomostControl;

        public ChangesManagerWindow(UIDocument uidoc, ChangesViewModel settings)
        {
            InitializeComponent();
            _vm = new ChangesManagerViewModel(uidoc, settings);
            DataContext = _vm;
            _revisionsControl = new ChangesRevisionsControl(_vm);
            _sheetsControl = new ChangesSheetsControl(_vm);
            _vedomostControl = new ChangesVedomostControl(_vm);
            _vm.Reload();
            if (_vm.NumberingJustSwitched && _vm.UnissuedCount > 0)
                _vm.Status = "Нумерация изменений переключена на «в рамках проекта». " + _vm.UnissueMessage;
            else if (_vm.NumberingJustSwitched)
                _vm.Status = "Нумерация изменений переключена на «в рамках проекта».";
            else if (_vm.UnissuedCount > 0)
                _vm.Status = _vm.UnissueMessage;
            else if (!_vm.HasWorkingSet)
                _vm.Status = "Для работы с листами и ведомостью выберите комплект, затем изменение.";
            else if (!_vm.HasContentParameter)
                _vm.Status = "Параметр «Изм.Содержание изменения» не найден на листах. Добавьте его в шаблон.";
            else if (!_vm.HasGroupParameter)
                _vm.Status = "Параметр «Изм.Группа листов» не найден на листах. Добавьте его в шаблон, чтобы сохранять группы ведомости.";
            FunctionContent.Content = _revisionsControl;
            HighlightButton(BtnRevisions);
        }

        public ChangesViewModel Settings
        {
            get
            {
                _vm.PersistCurrentChecks();
                return _vm.Settings;
            }
        }

        internal void Report(ChangesOperationResult result, bool reload = true)
        {
            if (result == null) return;
            _vm.Status = result.Message ?? string.Empty;
            if (!result.Success)
            {
                new InfoWindow400(result.Message ?? "Ошибка").ShowDialog();
                return;
            }
            if (reload) _vm.Reload();
            if (!string.IsNullOrWhiteSpace(result.Message))
                new InfoWindow280(result.Message).ShowDialog();
        }

        void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            HelpLinks.ShowHelp("Изменения");
        }

        void BtnRevisions_Click(object sender, RoutedEventArgs e)
        {
            FunctionContent.Content = _revisionsControl;
            HighlightButton(BtnRevisions);
        }

        void BtnSheets_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.HasWorkingSet)
                _vm.Status = "Сначала выберите комплект, затем изменение.";
            FunctionContent.Content = _sheetsControl;
            HighlightButton(BtnSheets);
        }

        void BtnVedomost_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.HasWorkingSet)
                _vm.Status = "Сначала выберите комплект, затем изменение.";
            else
                _vm.RebuildVedomost();
            FunctionContent.Content = _vedomostControl;
            HighlightButton(BtnVedomost);
        }

        void HighlightButton(Button activeButton)
        {
            var buttons = new[] { BtnRevisions, BtnSheets, BtnVedomost };
            var selectedBrush = (SolidColorBrush)FindResource("SelectedBrush");
            foreach (var btn in buttons)
            {
                btn.Background = Brushes.Transparent;
                btn.ClearValue(Button.BorderBrushProperty);
                btn.ClearValue(Button.BorderThicknessProperty);
            }
            activeButton.Background = selectedBrush;
        }
    }
}
