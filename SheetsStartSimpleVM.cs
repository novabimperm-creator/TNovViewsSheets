using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace TNovViewsSheets
{
    public class SheetsStartSimpleVM : INotifyPropertyChanged
    {

        private bool _addPrefixes = false;
        public bool addPrefixes
        {
            get => _addPrefixes; set { _addPrefixes = value; OnPropertyChanged(); }
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
