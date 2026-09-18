using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace TNovViewsSheets
{
    public class ChangesManagerViewModel : INotifyPropertyChanged
    {
        readonly Document _doc;
        readonly UIDocument _uidoc;
        List<RevisionCloud> _clouds = new List<RevisionCloud>();
        Dictionary<ElementId, List<RevisionCloud>> _cloudsBySheet = new Dictionary<ElementId, List<RevisionCloud>>();

        string _filterSet = string.Empty;
        RevisionRow _selectedRevision;
        ChangeSheetRow _selectedSheet;
        string _status = "Выберите раздел слева.";
        string _numberingText;
        bool _reloading;

        public ChangesManagerViewModel(UIDocument uidoc, ChangesViewModel settings)
        {
            _uidoc = uidoc;
            _doc = uidoc.Document;
            Settings = settings ?? new ChangesViewModel();
            Settings.EnsureCollections();
            if (!string.IsNullOrEmpty(Settings.lastSheetSet)
                && Settings.lastSheetSet != ChangesParams.AllSetsName)
            {
                _filterSet = Settings.lastSheetSet;
            }
        }

        public Document Doc => _doc;
        public UIDocument UiDoc => _uidoc;
        public ChangesViewModel Settings { get; }
        public IReadOnlyList<RevisionCloud> Clouds => _clouds;
        public IReadOnlyDictionary<ElementId, List<RevisionCloud>> CloudsBySheet => _cloudsBySheet;
        public bool IsReloading => _reloading;

        public ObservableCollection<RevisionRow> Revisions { get; } = new ObservableCollection<RevisionRow>();
        public ObservableCollection<RevisionRow> WorkingRevisions { get; } = new ObservableCollection<RevisionRow>();
        public ObservableCollection<ChangeSheetRow> Sheets { get; } = new ObservableCollection<ChangeSheetRow>();
        public ObservableCollection<ChangeSheetRow> FilteredSheets { get; } = new ObservableCollection<ChangeSheetRow>();
        public ObservableCollection<string> SheetSets { get; } = new ObservableCollection<string>();
        public ObservableCollection<SheetRevisionInfo> SelectedSheetRevisions { get; } = new ObservableCollection<SheetRevisionInfo>();
        public ObservableCollection<VedomostRow> Vedomost { get; } = new ObservableCollection<VedomostRow>();
        public ObservableCollection<string> ContentSuggestions { get; } = new ObservableCollection<string>();

        public IReadOnlyList<VisibilityOption> VisibilityOptions => ChangesService.VisibilityOptions;
        public IReadOnlyList<KindOption> KindOptions => ChangesService.KindOptions;
        public IReadOnlyList<string> ListTextOptions => ChangesService.ListTextOptions;
        public bool NumberingJustSwitched { get; private set; }
        public int UnissuedCount { get; private set; }
        public string UnissueMessage { get; private set; }

        public bool HasWorkingSet =>
            !string.IsNullOrEmpty(FilterSet) && FilterSet != ChangesParams.AllSetsName;

        public string FilterSet
        {
            get => _filterSet;
            set
            {
                if (_reloading && string.IsNullOrEmpty(value)) return;
                string next = value ?? string.Empty;
                if (next == ChangesParams.AllSetsName) next = string.Empty;
                if (_filterSet == next) return;
                _filterSet = next;
                Settings.lastSheetSet = _filterSet;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasWorkingSet));
                if (!_reloading)
                {
                    ApplySheetFilter();
                    RefreshWorkRevisionFields();
                    RebuildVedomost();
                }
            }
        }

        public RevisionRow SelectedRevision
        {
            get => _selectedRevision;
            set
            {
                if (ReferenceEquals(_selectedRevision, value)) return;
                if (!_reloading)
                    PersistCurrentChecks();
                _selectedRevision = value;
                OnPropertyChanged();
                if (!_reloading)
                {
                    ApplySavedChecks();
                    RefreshWorkRevisionFields();
                    RebuildVedomost();
                }
            }
        }

        public ChangeSheetRow SelectedSheet
        {
            get => _selectedSheet;
            set
            {
                _selectedSheet = value;
                OnPropertyChanged();
                RebuildSelectedSheetRevisions();
            }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public string NumberingText
        {
            get => _numberingText;
            set { _numberingText = value; OnPropertyChanged(); }
        }

        public bool HasGroupParameter
        {
            get
            {
                ChangeSheetRow row = Sheets.FirstOrDefault();
                if (row == null) return false;
                var sheet = _doc.GetElement(row.Id) as ViewSheet;
                return sheet != null && ChangesService.FindSheetGroupParameter(sheet) != null;
            }
        }

        public bool HasContentParameter
        {
            get
            {
                ChangeSheetRow row = Sheets.FirstOrDefault();
                if (row == null) return false;
                var sheet = _doc.GetElement(row.Id) as ViewSheet;
                return sheet != null && ChangesService.FindSheetContentParameter(sheet) != null;
            }
        }

        public void Reload()
        {
            _reloading = true;
            NumberingJustSwitched = ChangesService.EnsurePerProjectNumbering(_doc);
            ChangesOperationResult unissue = ChangesService.UnissueAllRevisions(_doc);
            UnissuedCount = unissue.Success ? unissue.Count : 0;
            UnissueMessage = unissue.Message ?? string.Empty;
            ElementId selectedRevId = SelectedRevision?.Id;
            ElementId selectedSheetId = SelectedSheet?.Id;
            PersistCurrentChecks();

            _clouds = ChangesService.CollectClouds(_doc);
            _cloudsBySheet = ChangesService.IndexCloudsBySheet(_doc, _clouds);
            List<ViewSheet> viewSheets = ChangesService.CollectSheets(_doc);
            List<Revision> revisions = ChangesService.CollectRevisions(_doc);

            var sheetRows = viewSheets
                .Select(s => ChangesService.BuildSheetRow(_doc, s, revisions, _cloudsBySheet, false))
                .OrderBy(s => s.SheetSet, NaturalStringComparer.Instance)
                .ThenBy(s => s.DisplayNumber, NaturalStringComparer.Instance)
                .ToList();

            foreach (ChangeSheetRow row in Sheets)
                row.PropertyChanged -= SheetRow_PropertyChanged;
            Sheets.Clear();
            foreach (ChangeSheetRow row in sheetRows)
            {
                row.PropertyChanged += SheetRow_PropertyChanged;
                Sheets.Add(row);
            }

            var revRows = revisions
                .Select(r => ChangesService.BuildRevisionRow(r, sheetRows, _cloudsBySheet))
                .OrderBy(r => r.IssuedTo ?? string.Empty, NaturalStringComparer.Instance)
                .ThenBy(r => r.IssuedBy ?? string.Empty, NaturalStringComparer.Instance)
                .ThenBy(r => r.Description ?? string.Empty, NaturalStringComparer.Instance)
                .ToList();
            Revisions.Clear();
            foreach (RevisionRow row in revRows)
                Revisions.Add(row);

            var sets = ChangesService.CollectSheetSets(viewSheets);
            SheetSets.Clear();
            foreach (string set in sets)
                SheetSets.Add(set);

            if (!string.IsNullOrEmpty(FilterSet) && !SheetSets.Contains(FilterSet))
                FilterSet = string.Empty;

            RevisionSettings settings = RevisionSettings.GetRevisionSettings(_doc);
            NumberingText = settings.RevisionNumbering == RevisionNumbering.PerProject
                ? "в рамках проекта"
                : "в рамках листа";

            ApplySheetFilter();
            SelectedRevision = Revisions.FirstOrDefault(r => selectedRevId != null && r.Id == selectedRevId)
                               ?? WorkingRevisions.FirstOrDefault()
                               ?? Revisions.FirstOrDefault();
            RefreshWorkRevisionFields();
            RebuildVedomost();

            SelectedSheet = FilteredSheets.FirstOrDefault(s => selectedSheetId != null && s.Id == selectedSheetId)
                            ?? FilteredSheets.FirstOrDefault();
            OnPropertyChanged(nameof(HasGroupParameter));
            OnPropertyChanged(nameof(HasContentParameter));
            OnPropertyChanged(nameof(HasWorkingSet));
            ApplySavedChecks();
            _reloading = false;
        }

        public void PersistCurrentChecks()
        {
            if (SelectedRevision == null || string.IsNullOrEmpty(SelectedRevision.UniqueId))
                return;
            Settings.EnsureCollections();
            Settings.checkedSheetIdsByRevision[SelectedRevision.UniqueId] =
                Sheets.Where(s => s.IsSelected && !string.IsNullOrEmpty(s.UniqueId))
                    .Select(s => s.UniqueId)
                    .ToList();
            Settings.SaveToProjects();
        }

        public void ApplySavedChecks()
        {
            if (SelectedRevision == null)
            {
                foreach (ChangeSheetRow row in Sheets)
                    row.IsSelected = false;
                return;
            }

            Settings.EnsureCollections();
            bool saved = Settings.checkedSheetIdsByRevision.TryGetValue(
                SelectedRevision.UniqueId ?? string.Empty, out List<string> ids);
            HashSet<string> selectedIds = saved
                ? new HashSet<string>(ids ?? new List<string>())
                : null;

            foreach (ChangeSheetRow row in Sheets)
            {
                if (selectedIds != null)
                    row.IsSelected = !string.IsNullOrEmpty(row.UniqueId) && selectedIds.Contains(row.UniqueId);
                else
                    row.IsSelected = HasFilledContentForRevision(row, SelectedRevision);
            }
        }

        static bool HasFilledContentForRevision(ChangeSheetRow row, RevisionRow revision)
        {
            if (row == null || revision == null) return false;
            if (string.IsNullOrWhiteSpace(row.Content)) return false;
            SheetRevisionInfo info = row.Revisions.FirstOrDefault(r => r.RevisionId == revision.Id);
            return info != null && info.IsOnSheet;
        }

        void SheetRow_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_reloading) return;
            if (!(sender is ChangeSheetRow row)) return;
            if (e.PropertyName == nameof(ChangeSheetRow.Content)
                && !string.IsNullOrWhiteSpace(row.Content)
                && !row.IsSelected)
            {
                row.IsSelected = true;
                return;
            }
            if (e.PropertyName == nameof(ChangeSheetRow.IsSelected))
                PersistCurrentChecks();
        }

        public void ApplySheetFilter()
        {
            FilteredSheets.Clear();
            if (HasWorkingSet)
            {
                foreach (ChangeSheetRow row in Sheets.Where(s => s.SheetSet == FilterSet))
                    FilteredSheets.Add(row);
                if (SelectedSheet != null && FilteredSheets.All(s => s.Id != SelectedSheet.Id))
                    SelectedSheet = FilteredSheets.FirstOrDefault();
            }
            RebuildWorkingRevisions();
            if (!_reloading && HasWorkingSet
                && (SelectedRevision == null || WorkingRevisions.All(r => r.Id != SelectedRevision.Id)))
            {
                SelectedRevision = WorkingRevisions.FirstOrDefault();
            }
        }

        void RebuildWorkingRevisions()
        {
            WorkingRevisions.Clear();
            IEnumerable<RevisionRow> query = Revisions;
            if (HasWorkingSet)
            {
                query = Revisions.Where(r =>
                    string.IsNullOrWhiteSpace(r.IssuedTo)
                    || string.Equals(r.IssuedTo.Trim(), FilterSet, StringComparison.CurrentCultureIgnoreCase));
            }
            foreach (RevisionRow row in query)
                WorkingRevisions.Add(row);
        }

        public void RefreshWorkRevisionFields()
        {
            foreach (ChangeSheetRow row in Sheets)
                ChangesService.ApplyWorkingRevisionFields(row, SelectedRevision);
            RefreshContentSuggestions();
        }

        public void RefreshContentSuggestions()
        {
            var values = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (ChangeSheetRow row in HasWorkingSet ? FilteredSheets : Sheets)
            {
                if (!string.IsNullOrWhiteSpace(row.Content))
                    values.Add(row.Content.Trim());
            }
            var ordered = values.OrderBy(v => v, NaturalStringComparer.Instance).ToList();
            if (ContentSuggestions.Count == ordered.Count
                && ContentSuggestions.Zip(ordered, (a, b) => string.Equals(a, b, StringComparison.CurrentCultureIgnoreCase)).All(x => x))
            {
                return;
            }

            for (int i = ContentSuggestions.Count - 1; i >= 0; i--)
            {
                if (!ordered.Contains(ContentSuggestions[i], StringComparer.CurrentCultureIgnoreCase))
                    ContentSuggestions.RemoveAt(i);
            }
            foreach (string value in ordered)
            {
                if (!ContentSuggestions.Contains(value, StringComparer.CurrentCultureIgnoreCase))
                    ContentSuggestions.Add(value);
            }
        }

        public void RebuildVedomost()
        {
            Vedomost.Clear();
            if (!HasWorkingSet || SelectedRevision == null) return;
            ChangesService.AssignAutoGroups(FilteredSheets.ToList());
            foreach (VedomostRow row in ChangesService.BuildVedomost(FilteredSheets, SelectedRevision, FilterSet))
                Vedomost.Add(row);
        }

        public void RebuildSelectedSheetRevisions()
        {
            SelectedSheetRevisions.Clear();
            if (SelectedSheet == null) return;
            foreach (SheetRevisionInfo info in SelectedSheet.Revisions.OrderBy(r => r.Sequence))
                SelectedSheetRevisions.Add(info);
        }

        public IList<ChangeSheetRow> GetCheckedSheets()
        {
            return FilteredSheets.Where(s => s.IsSelected && !s.IsAnnulled).ToList();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
