using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace TNovViewsSheets
{
    public class sheetChange
    {
        public int number;
        public string description;
        public int cloudcount;
        public bool newlist;
    }

    public class ChangesOperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int Count { get; set; }

        public static ChangesOperationResult Ok(string message = "", int count = 0) =>
            new ChangesOperationResult { Success = true, Message = message, Count = count };

        public static ChangesOperationResult Fail(string message) =>
            new ChangesOperationResult { Success = false, Message = message };
    }

    public class StampLinePreview
    {
        public string CountText { get; set; }
        public string SheetText { get; set; }
    }

    public class SheetRevisionInfo : INotifyPropertyChanged
    {
        private ChangeStampKind _kind;
        private bool _isOnSheet;

        public ElementId RevisionId { get; set; }
        public string NumberText { get; set; }
        public int Sequence { get; set; }
        public string Description { get; set; }
        public string IssuedBy { get; set; }
        public int CloudCount { get; set; }
        public bool IsIssued { get; set; }

        public ChangeStampKind Kind
        {
            get => _kind;
            set { if (_kind == value) return; _kind = value; KindOverridden = true; OnPropertyChanged(); OnPropertyChanged(nameof(KindText)); }
        }

        public bool KindOverridden { get; set; }

        public void SetKind(ChangeStampKind kind, bool overridden)
        {
            _kind = kind;
            KindOverridden = overridden;
            OnPropertyChanged(nameof(Kind));
            OnPropertyChanged(nameof(KindText));
        }

        public bool IsOnSheet
        {
            get => _isOnSheet;
            set { if (_isOnSheet == value) return; _isOnSheet = value; OnPropertyChanged(); }
        }

        public string KindText => KindToText(Kind, CloudCount);

        public static string KindToText(ChangeStampKind kind, int cloudCount)
        {
            switch (kind)
            {
                case ChangeStampKind.New: return "Нов.";
                case ChangeStampKind.Replace: return "Зам.";
                default: return cloudCount > 0 ? cloudCount.ToString() : "частичное";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ChangeSheetRow : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _group;
        private string _content;
        private string _countText;
        private string _listText;

        public ElementId Id { get; set; }
        public string UniqueId { get; set; }
        public string SheetNumber { get; set; }
        public string Name { get; set; }
        public string SheetSet { get; set; }
        public string DisplayNumber { get; set; }
        public int? NumericNumber { get; set; }
        public bool IsAnnulled { get; set; }
        public string AnnulledText => IsAnnulled ? "да" : "";
        public string NotePreview { get; set; }
        public string StampPreview { get; set; }
        public List<StampLinePreview> StampLines { get; set; } = new List<StampLinePreview>();
        public List<SheetRevisionInfo> Revisions { get; set; } = new List<SheetRevisionInfo>();
        public bool ListOverridden { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
        }

        public string Group
        {
            get => _group;
            set { if (_group == value) return; _group = value; OnPropertyChanged(); }
        }

        public string Content
        {
            get => _content;
            set { if (_content == value) return; _content = value; OnPropertyChanged(); }
        }

        public string CountText
        {
            get => _countText;
            set { if (_countText == value) return; _countText = value; OnPropertyChanged(); }
        }

        public string ListText
        {
            get => _listText;
            set
            {
                if (_listText == value) return;
                _listText = value;
                ListOverridden = true;
                OnPropertyChanged();
            }
        }

        public void SetListText(string value, bool overridden)
        {
            _listText = value;
            ListOverridden = overridden;
            OnPropertyChanged(nameof(ListText));
        }

        public string RevisionsSummary
        {
            get
            {
                var parts = Revisions
                    .Where(r => r.IsOnSheet)
                    .OrderBy(r => r.Sequence)
                    .Select(r => r.NumberText + ":" + SheetRevisionInfo.KindToText(r.Kind, r.CloudCount));
                return string.Join(", ", parts);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        internal void RaiseRevisionsChanged()
        {
            OnPropertyChanged(nameof(RevisionsSummary));
            OnPropertyChanged(nameof(StampPreview));
            OnPropertyChanged(nameof(NotePreview));
        }
        void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class RevisionRow : INotifyPropertyChanged
    {
        private string _description;
        private string _revisionDate;
        private string _issuedTo;
        private string _issuedBy;
        private RevisionVisibility _visibility;

        public ElementId Id { get; set; }
        public string UniqueId { get; set; }
        public string NumberText { get; set; }
        public int Sequence { get; set; }
        public bool Issued { get; set; }
        public string IssuedText => Issued ? "да" : "";
        public string DisplayNumber =>
            !string.IsNullOrWhiteSpace(IssuedBy) ? IssuedBy.Trim()
            : (!string.IsNullOrWhiteSpace(Description) ? Description.Trim() : "без номера");
        public string ListCaption => DisplayNumber;
        public int SheetCount { get; set; }
        public int CloudCount { get; set; }
        public string SetsText { get; set; }

        public string Description
        {
            get => _description;
            set
            {
                if (_description == value) return;
                _description = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayNumber));
                OnPropertyChanged(nameof(ListCaption));
            }
        }

        public string RevisionDate
        {
            get => _revisionDate;
            set { if (_revisionDate == value) return; _revisionDate = value; OnPropertyChanged(); }
        }

        public string IssuedTo
        {
            get => _issuedTo;
            set { if (_issuedTo == value) return; _issuedTo = value; OnPropertyChanged(); }
        }

        public string IssuedBy
        {
            get => _issuedBy;
            set
            {
                if (_issuedBy == value) return;
                _issuedBy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayNumber));
                OnPropertyChanged(nameof(ListCaption));
            }
        }

        public RevisionVisibility Visibility
        {
            get => _visibility;
            set { if (_visibility == value) return; _visibility = value; OnPropertyChanged(); OnPropertyChanged(nameof(VisibilityText)); }
        }

        public string VisibilityText => ChangesService.VisibilityCaption(Visibility);

        public event PropertyChangedEventHandler PropertyChanged;
        void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class VedomostRow
    {
        public string RevisionNumber { get; set; }
        public int Sequence { get; set; }
        public string SheetNumbers { get; set; }
        public string CountText { get; set; }
        public string SheetKind { get; set; }
        public string Description { get; set; }
        public string Group { get; set; }
        public string SheetSet { get; set; }
        public int SheetCount { get; set; }
    }

    public class VisibilityOption
    {
        public RevisionVisibility Value { get; set; }
        public string Name { get; set; }
    }

    public class KindOption
    {
        public ChangeStampKind Value { get; set; }
        public string Name { get; set; }
    }
}
