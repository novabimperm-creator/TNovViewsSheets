using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TNovCommon;

namespace TNovViewsSheets
{
    public partial class ChangesSheetsControl : UserControl
    {
        readonly ChangesManagerViewModel _vm;

        public ChangesSheetsControl(ChangesManagerViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;
        }

        ChangesManagerWindow Host => Window.GetWindow(this) as ChangesManagerWindow;

        bool Confirm(string text)
        {
            var q = new QuestionWindowViewModel { headtxt = text };
            var w = new QuestionWindow280(q);
            q.CloseRequest += (s, e) => w.Close();
            return w.ShowDialog() == true;
        }

        void ApplyStamp_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.HasWorkingSet)
            {
                new InfoWindow280("Сначала выберите комплект.").ShowDialog();
                return;
            }
            if (_vm.SelectedRevision == null)
            {
                new InfoWindow280("Выберите изменение.").ShowDialog();
                return;
            }

            var setSheets = _vm.FilteredSheets.ToList();
            if (setSheets.Count == 0)
            {
                new InfoWindow280("В комплекте нет листов.").ShowDialog();
                return;
            }

            int willRemove = setSheets.Count(s => !s.IsSelected
                && s.Revisions.Any(r => r.RevisionId == _vm.SelectedRevision.Id && r.IsOnSheet));
            if (willRemove > 0
                && !Confirm("Снять изменение с " + willRemove + " листов без галочки? Облака этого изменения на них будут удалены."))
            {
                return;
            }

            Host?.Report(ChangesService.ApplyCheckedChange(_vm.Doc, setSheets, _vm.SelectedRevision.Id, _vm.Clouds));
        }

        void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (ChangeSheetRow row in _vm.FilteredSheets)
                row.IsSelected = true;
        }

        void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (ChangeSheetRow row in _vm.FilteredSheets)
                row.IsSelected = false;
        }

        void ContentCombo_DropDownOpened(object sender, EventArgs e)
        {
            _vm.RefreshContentSuggestions();
        }

        void ListCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_vm.IsReloading) return;
            var combo = sender as ComboBox;
            if (combo == null || !combo.IsKeyboardFocusWithin) return;
            Dispatcher.BeginInvoke(new System.Action(() => _vm.RebuildVedomost()));
        }
    }
}
