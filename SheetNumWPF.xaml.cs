using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TNovCommon;

namespace TNovViewsSheets
{
    public partial class SheetNumWPF : Window
    {
        private readonly List<TNovSheet> _allItems = new List<TNovSheet>();
        private readonly Dictionary<string, ObservableCollection<TNovSheet>> _filteredCollections = new Dictionary<string, ObservableCollection<TNovSheet>>();
        private string _currentFilterType;
        private TNovSheet _draggedItem;
        private int _startOrder;
        private ObservableCollection<TNovSheet> _currentCollection = new ObservableCollection<TNovSheet>();

        public IEnumerable<TNovSheet> ResultCollection { get; private set; } = Array.Empty<TNovSheet>();

        public SheetNumWPF(TNovSheet[] items)
        {
            InitializeComponent();
            foreach (var item in items.OrderBy(i => i.TNovSheetNumericNumber))
                _allItems.Add(item);

            TypeComboBox.ItemsSource = _allItems.Select(i => i.TNovSheetSet).Distinct().OrderBy(t => t).ToList();
        }

        private void ApplyFilterButton_Click(object sender, RoutedEventArgs e)
        {
            if (TypeComboBox.SelectedItem == null) return;
            _currentFilterType = TypeComboBox.SelectedItem.ToString();
            ApplyFilter(_currentFilterType);
        }

        private void ApplyFilter(string type)
        {
            UpdateStartOrderFromTextBox();

            if (!_filteredCollections.ContainsKey(type))
            {
                var itemsOfType = _allItems.Where(item => item.TNovSheetSet == type).ToList();
                var collection = new ObservableCollection<TNovSheet>(itemsOfType);
                UpdateOrderForCollection(collection);
                _filteredCollections[type] = collection;
                _currentCollection = collection;
            }

            CurrentFilterText.Text = $"Текущий комплект: {type}";
            CurrentFilterText.Visibility = Visibility.Visible;
            ItemsListView.ItemsSource = _filteredCollections[type];
        }

        private void UpdateStartOrderFromTextBox()
        {
            if (int.TryParse(StartOrderTextBox.Text, out int newStartOrder))
                _startOrder = newStartOrder;
            else
            {
                _startOrder = 0;
                StartOrderTextBox.Text = "0";
            }
        }

        private void UpdateOrderForCollection(ObservableCollection<TNovSheet> collection)
        {
            for (int i = 0; i < collection.Count; i++)
                collection[i].TNovSheetNumericNumber = _startOrder + i;
        }

        private void RenumberButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentFilterType) && _filteredCollections.ContainsKey(_currentFilterType))
            {
                UpdateStartOrderFromTextBox();
                UpdateOrderForCollection(_filteredCollections[_currentFilterType]);
            }
        }

        private void TypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private void ListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentFilterType)) return;
            if (e.OriginalSource is FrameworkElement element && element.DataContext is TNovSheet item)
            {
                _draggedItem = item;
                DragDrop.DoDragDrop(ItemsListView, item, DragDropEffects.Move);
            }
        }

        private void ListView_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void ListView_Drop(object sender, DragEventArgs e)
        {
            if (_draggedItem == null || string.IsNullOrEmpty(_currentFilterType)) return;
            var targetItem = ((FrameworkElement)e.OriginalSource).DataContext as TNovSheet;
            if (targetItem == null || _draggedItem == targetItem) return;

            var collection = _filteredCollections[_currentFilterType];
            int oldIndex = collection.IndexOf(_draggedItem);
            int newIndex = collection.IndexOf(targetItem);
            if (oldIndex == -1 || newIndex == -1) return;

            collection.Move(oldIndex, newIndex);
            UpdateOrderForCollection(collection);
            _draggedItem = null;
        }

        private void acceptButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentFilterType) || !_filteredCollections.ContainsKey(_currentFilterType))
            {
                new InfoWindow280("Сначала выберите комплект и нажмите «Отобразить».").ShowDialog();
                return;
            }

            UpdateStartOrderFromTextBox();
            UpdateOrderForCollection(_currentCollection);
            ResultCollection = _filteredCollections.Values.SelectMany(c => c).ToList();
            DialogResult = true;
            Close();
        }

        private void escButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }
    }
}
