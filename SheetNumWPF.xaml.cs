using System.Globalization;
using System;
using System.Windows;
using System.Windows.Data;
using System.Drawing;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Controls;
using System.Collections.Generic;

namespace TNovViewsSheets
{
    /// <summary>
    /// Логика взаимодействия для SheetNumWPF.xaml
    /// </summary>

    public partial class SheetNumWPF : Window
    {
        private readonly List<TNovSheet> _allItems = new List<TNovSheet>();
        private readonly Dictionary<string, ObservableCollection<TNovSheet>> _filteredCollections = new Dictionary<string, ObservableCollection<TNovSheet>>();
        private string _currentFilterType;
        private TNovSheet _draggedItem;
        private int _startOrder = 0;

        // Публичная коллекция для доступа извне
        public ObservableCollection<TNovSheet> ResultCollection { get; private set; } = new ObservableCollection<TNovSheet>();
        // Приватная текущая коллекция
        private ObservableCollection<TNovSheet> currentCollection = new ObservableCollection<TNovSheet>();
        public SheetNumWPF(TNovSheet[] items)
        {
            InitializeComponent();
            InitializeData(items);
            ItemsListView.Visibility = Visibility.Collapsed;
        }

        private void InitializeData(TNovSheet[] items)
        {
            List<TNovSheet> itemsList = new List<TNovSheet>();
            foreach (TNovSheet item in items)
            {
                itemsList.Add(item);
            }
            itemsList = itemsList.OrderBy(i => i.TNovSheetNumericNumber).ToList();
            foreach (TNovSheet item in itemsList)
            {
                _allItems.Add(item);
            }

            // Заполняем ComboBox типами
            TypeComboBox.ItemsSource = _allItems
                .Select(i => i.TNovSheetSet)
                .Distinct()
                .OrderBy(t => t);
        }

        private void ApplyFilterButton_Click(object sender, RoutedEventArgs e)
        {
            if (TypeComboBox.SelectedItem == null) return;

            _currentFilterType = TypeComboBox.SelectedItem.ToString();
            ApplyFilter(_currentFilterType);
        }

        private void ApplyFilter(string type)
        {
            // Обновляем начальный номер из TextBox
            UpdateStartOrderFromTextBox();

            // Если коллекция для этого типа еще не создана
            if (!_filteredCollections.ContainsKey(type))
            {
                var itemsOfType = _allItems
                    .Where(item => item.TNovSheetSet == type)
                    .ToList();

                var collection = new ObservableCollection<TNovSheet>(itemsOfType);
                UpdateOrderForCollection(collection);
                _filteredCollections[type] = collection;
                currentCollection = collection;
            }

            // Показываем текущий фильтр
            CurrentFilterText.Text = $"Current Type: {type}";

            // Устанавливаем источник данных
            ItemsListView.ItemsSource = _filteredCollections[type];
            ItemsListView.Visibility = Visibility.Visible;
        }

        private void UpdateStartOrderFromTextBox()
        {
            if (int.TryParse(StartOrderTextBox.Text, out int newStartOrder))
            {
                _startOrder = newStartOrder;
            }
            else
            {
                // Если введено не число, используем 0 по умолчанию
                _startOrder = 0;
                StartOrderTextBox.Text = "0";
            }
        }

        private void UpdateOrderForCollection(ObservableCollection<TNovSheet> collection)
        {
            for (int i = 0; i < collection.Count; i++)
            {
                collection[i].TNovSheetNumericNumber = _startOrder + i;
            }
        }

        private void RenumberButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentFilterType) &&
                _filteredCollections.ContainsKey(_currentFilterType))
            {
                UpdateStartOrderFromTextBox();
                UpdateOrderForCollection(_filteredCollections[_currentFilterType]);
            }
        }

        private void TypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Сбрасываем отображение при выборе нового типа
            ItemsListView.Visibility = Visibility.Collapsed;
            CurrentFilterText.Text = "";
        }

        #region Drag and Drop
        private void ListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentFilterType)) return;

            if (e.OriginalSource is FrameworkElement element &&
                element.DataContext is TNovSheet item)
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

            currentCollection = _filteredCollections[_currentFilterType];
            int oldIndex = currentCollection.IndexOf(_draggedItem);
            int newIndex = currentCollection.IndexOf(targetItem);

            if (oldIndex == -1 || newIndex == -1) return;

            currentCollection.Move(oldIndex, newIndex);
            UpdateOrderForCollection(currentCollection);

            _draggedItem = null;
        }
        #endregion

        private void acceptButton_Click(object sender, RoutedEventArgs e)
        {
            // Обновляем публичную коллекцию перед закрытием
            if (!string.IsNullOrEmpty(_currentFilterType) &&
        _filteredCollections.ContainsKey(_currentFilterType))
            {
                UpdateStartOrderFromTextBox();
                UpdateOrderForCollection(currentCollection);
            }
            ResultCollection = new ObservableCollection<TNovSheet>(currentCollection);

            DialogResult = true;
            this.Close(); // закрытие окна
        }

        private void escButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            this.Close(); // закрытие окна
        }

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {

        }
    }
}
