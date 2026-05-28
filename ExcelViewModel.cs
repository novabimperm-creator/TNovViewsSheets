using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace TNovViewsSheets
{
    public class ExcelViewModel : INotifyPropertyChanged
    {

        private bool _sc1 = false;
        public bool sc1
        {
            get => _sc1; set { _sc1 = value; OnPropertyChanged(); }
        }
        private bool _sc2 = true;
        public bool sc2
        {
            get => _sc2; set { _sc2 = value; OnPropertyChanged(); }
        }
        private bool _sc3 = false;
        public bool sc3
        {
            get => _sc3; set { _sc3 = value; OnPropertyChanged(); }
        }
        private bool _show = true;
        public bool show
        {
            get => _show; set { _show = value; OnPropertyChanged(); }
        }

        public event EventHandler CloseRequest;
        private void RaiseCloseRequest()
        {
            CloseRequest?.Invoke(this, EventArgs.Empty);
        }
        public event PropertyChangedEventHandler PropertyChanged;

        void OnPropertyChanged([CallerMemberName] string PropertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(PropertyName));
        }

    }
}
