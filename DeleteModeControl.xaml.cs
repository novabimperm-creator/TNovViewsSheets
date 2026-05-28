using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TNovCommon;

namespace TNovViewsSheets
{
    public partial class DeleteModeControl : UserControl
    {
        private UIDocument _uidoc;
        private Document _doc;
        private ListView _sheetsListView;

        public DeleteModeControl(UIDocument uidoc, ListView sheetsListView)
        {
            InitializeComponent();
            _uidoc = uidoc;
            _doc = uidoc.Document;
            _sheetsListView = sheetsListView;
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (_sheetsListView.ItemsSource is System.ComponentModel.ICollectionView view)
            {
                _sheetsListView.SelectedItems.Clear();
                foreach (var item in view.OfType<SheetInfo>())
                {
                    _sheetsListView.SelectedItems.Add(item);
                }
            }
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            _sheetsListView.SelectedItems.Clear();
        }

        private void DeleteSheets_Click(object sender, RoutedEventArgs e)
        {
            var selected = _sheetsListView.SelectedItems.OfType<SheetInfo>().ToList();
            if (selected.Count == 0)
            {
                new InfoWindow280("Не выбрано ни одного листа для удаления.").ShowDialog();
                return;
            }

            QuestionWindowViewModel qViewModel = new QuestionWindowViewModel();
            qViewModel.headtxt = $"Вы действительно хотите удалить {selected.Count} лист(ов)?";
            var qwpfview = new QuestionWindow280(qViewModel);
            qViewModel.CloseRequest += (s, ea) => qwpfview.Close();
            bool? qok = qwpfview.ShowDialog();
            if (qok != null && qok == true) { } else return;
            int countDeleted = 0;

            using (Transaction trans = new Transaction(_doc, "Удалить листы"))
            {
                string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
                Logger.Initialize("Менеджер листов. Удаление листов", DateTime.Now, TNovVersion);

                List<string> badSheets = new List<string>();
                
                trans.Start();
                foreach (var sheetInfo in selected)
                {
                    Logger.Log($"Лист {sheetInfo.SheetNumber} {sheetInfo.Name}", 1);
                    Element sheet = _doc.GetElement(sheetInfo.Id);
                    if (sheet != null)
                    {
                        try
                        {
                            _doc.Delete(sheet.Id);
                            Logger.Log("   удален", 1);
                            countDeleted++;
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"   ошибка: {ex.Message}", 4);
                            badSheets.Add($"Лист {sheetInfo.SheetNumber} {sheetInfo.Name} ошибка: {ex.Message}");
                            continue;
                        }
                    }
                }
                trans.Commit();

                if(badSheets.Count > 0)
                {
                    string message = "Некоторые листы не были удалены:\n";
                    message += String.Join("\n", badSheets);
                    new InfoWindow400(message).ShowDialog();
                }

                Logger.Log("Завершение работы.", 5);
            }

            var mainWindow = Window.GetWindow(this) as MainWindow;
            mainWindow?.RefreshSheetList("");
            mainWindow?.LoadFilterSets();

            new InfoWindow280($"Удалено листов: {countDeleted}.").ShowDialog();
        }
    }
}