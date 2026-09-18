using System.Windows;
using System.Windows.Controls;
using TNovCommon;

namespace TNovViewsSheets
{
    public partial class ChangesVedomostControl : UserControl
    {
        readonly ChangesManagerViewModel _vm;

        public ChangesVedomostControl(ChangesManagerViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;
        }

        void Refresh_Click(object sender, RoutedEventArgs e)
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
            _vm.RefreshWorkRevisionFields();
            _vm.RebuildVedomost();
            _vm.Status = "Ведомость обновлена, строк: " + _vm.Vedomost.Count;
        }
    }
}
