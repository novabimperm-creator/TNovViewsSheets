using Autodesk.Revit.DB;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TNovViewsSheets
{
    public class TNovSheet : INotifyPropertyChanged
    {
        private ElementId _id;
        public ElementId Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        private string _TNovSheetCleanNumber;
        public string TNovSheetCleanNumber
        {
            get => _TNovSheetCleanNumber;
            set { _TNovSheetCleanNumber = value; OnPropertyChanged(); }
        }

        private string _TNovSheetNumber;
        public string TNovSheetNumber
        {
            get => _TNovSheetNumber;
            set { _TNovSheetNumber = value; OnPropertyChanged(); }
        }

        private string _TNovSheetNumberCustom;
        public string TNovSheetNumberCustom
        {
            get => _TNovSheetNumberCustom;
            set { _TNovSheetNumberCustom = value; OnPropertyChanged(); }
        }

        private string _TNovSheetName;
        public string TNovSheetName
        {
            get => _TNovSheetName;
            set { _TNovSheetName = value; OnPropertyChanged(); }
        }

        private string _TNovSheetSet;
        public string TNovSheetSet
        {
            get => _TNovSheetSet;
            set { _TNovSheetSet = value; OnPropertyChanged(); }
        }

        private string _TNovSheetNewNumber;
        public string TNovSheetNewNumber
        {
            get => _TNovSheetNewNumber;
            set { _TNovSheetNewNumber = value; OnPropertyChanged(); }
        }

        private bool _TNovSheetCanRenum;
        public bool TNovSheetCanRenum
        {
            get => _TNovSheetCanRenum;
            set { _TNovSheetCanRenum = value; OnPropertyChanged(); }
        }

        private int _TNovSheetNumericNumber;
        public int TNovSheetNumericNumber
        {
            get => _TNovSheetNumericNumber;
            set { _TNovSheetNumericNumber = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}