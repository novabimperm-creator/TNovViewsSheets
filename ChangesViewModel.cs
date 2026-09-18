using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace TNovViewsSheets
{
    public class ChangesViewModel : INotifyPropertyChanged
    {
        private ChangesScope _scope = ChangesScope.All;
        private bool _purge;
        private string _lastSheetSet;

        public ChangesScope Scope
        {
            get => _scope;
            set
            {
                if (_scope == value) return;
                _scope = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(all));
                OnPropertyChanged(nameof(visible));
                OnPropertyChanged(nameof(selected));
            }
        }

        public bool all
        {
            get => _scope == ChangesScope.All;
            set { if (value) Scope = ChangesScope.All; }
        }

        public bool visible
        {
            get => _scope == ChangesScope.Visible;
            set { if (value) Scope = ChangesScope.Visible; }
        }

        public bool selected
        {
            get => _scope == ChangesScope.Selected;
            set { if (value) Scope = ChangesScope.Selected; }
        }

        public bool purge
        {
            get => _purge;
            set { if (_purge == value) return; _purge = value; OnPropertyChanged(); }
        }

        public string lastSheetSet
        {
            get => _lastSheetSet;
            set { if (_lastSheetSet == value) return; _lastSheetSet = value; OnPropertyChanged(); }
        }

        public Dictionary<string, List<string>> checkedSheetIdsByRevision { get; set; }
            = new Dictionary<string, List<string>>();

        [JsonIgnore]
        public string StoragePath { get; set; }

        public void EnsureCollections()
        {
            if (checkedSheetIdsByRevision == null)
                checkedSheetIdsByRevision = new Dictionary<string, List<string>>();
        }

        public void SaveToProjects()
        {
            if (string.IsNullOrEmpty(StoragePath)) return;
            try
            {
                EnsureCollections();
                File.WriteAllText(StoragePath, JsonConvert.SerializeObject(this));
            }
            catch
            {
            }
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
