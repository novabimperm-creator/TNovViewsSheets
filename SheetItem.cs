using System;
using System.ComponentModel;
using Autodesk.Revit.DB;

namespace TNovViewsSheets
{
    /// <summary>
    /// View-model wrapper around a Revit ViewSheet for the selection grid.
    /// </summary>
    public class SheetItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public SheetItem(ViewSheet sheet)
        {
            Sheet = sheet;
            SheetNumber = sheet.SheetNumber ?? string.Empty;
            SheetName = sheet.Name ?? string.Empty;
            IsPlaceholder = sheet.IsPlaceholder;
            ElementId = sheet.Id;
            Stage = ReadStage(sheet);
        }

        public ViewSheet Sheet { get; }
        public ElementId ElementId { get; }
        public string SheetNumber { get; }
        public string SheetName { get; }
        public string Stage { get; }
        public bool IsPlaceholder { get; }

        public string DisplayLabel => $"{SheetNumber} — {SheetName}";

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        // The "stage" / drawing-set parameter is a project-wide shared parameter
        // named "A_Комплект чертежей" in this firm's template (values like "АР",
        // "КР", "ПОФ"). It may be stored as text or as an ElementId pointing at a
        // key-schedule row — try both.
        private static string ReadStage(ViewSheet sheet)
        {
            Parameter p = sheet.get_Parameter(new Guid("e1b06433-f527-403c-8986-af9a01e6be7f"));
            if (p == null || !p.HasValue) return string.Empty;
            string value = p.AsString() ?? p.AsValueString() ?? string.Empty;
            if (value != string.Empty && value.Contains(".СО")) value = value.Replace(".СО", "");
            return value;
        }
    }
}
