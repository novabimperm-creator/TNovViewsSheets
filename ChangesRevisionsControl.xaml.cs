using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TNovCommon;

namespace TNovViewsSheets
{
    public partial class ChangesRevisionsControl : UserControl
    {
        readonly ChangesManagerViewModel _vm;

        public ChangesRevisionsControl(ChangesManagerViewModel vm)
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

        void Create_Click(object sender, RoutedEventArgs e)
        {
            Host?.Report(ChangesService.CreateRevision(_vm.Doc));
        }

        void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedRevision == null)
            {
                new InfoWindow280("Выберите изменение.").ShowDialog();
                return;
            }
            if (_vm.SelectedRevision.Issued)
            {
                new InfoWindow280("Нельзя удалить выпущенное изменение.").ShowDialog();
                return;
            }
            if (!Confirm("Удалить изменение " + _vm.SelectedRevision.DisplayNumber + "?"))
                return;
            Host?.Report(ChangesService.DeleteRevision(_vm.Doc, _vm.SelectedRevision.Id));
        }

        void MoveUp_Click(object sender, RoutedEventArgs e) => Move(-1);
        void MoveDown_Click(object sender, RoutedEventArgs e) => Move(1);

        void Move(int delta)
        {
            if (_vm.SelectedRevision == null) return;
            if (!Confirm("Изменить порядок изменений?"))
                return;
            Host?.Report(ChangesService.MoveRevision(_vm.Doc, _vm.SelectedRevision.Id, delta));
        }

        void IssuedToCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_vm.IsReloading) return;
            var combo = sender as ComboBox;
            if (combo == null || !combo.IsKeyboardFocusWithin) return;
            if (combo.DataContext is RevisionRow row)
                SaveRow(row);
        }

        void RevisionsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (_vm.IsReloading) return;
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (!(e.Row.Item is RevisionRow row)) return;
            SaveRow(row);
        }

        void SaveRow(RevisionRow row)
        {
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                ChangesOperationResult result = ChangesService.SaveRevisionFields(_vm.Doc, row);
                _vm.Status = result.Success ? "Изменение сохранено." : result.Message;
                if (!result.Success) new InfoWindow280(result.Message).ShowDialog();
                else _vm.ApplySheetFilter();
            }));
        }
    }
}
