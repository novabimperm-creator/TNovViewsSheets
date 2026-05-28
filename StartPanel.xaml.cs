using System.Windows;
using System.Windows.Controls;

namespace TNovViewsSheets
{
    public partial class StartPanel : UserControl
    {
        private MainWindow _mainWindow;
        public StartPanel(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
        }
        private void GoToCreate_Click(object sender, RoutedEventArgs e) => _mainWindow.BtnCreateMode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        private void GoToCreateView_Click(object sender, RoutedEventArgs e) => _mainWindow.BtnCreateViewMode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        //private void GoToExportPdf_Click(object sender, RoutedEventArgs e) => _mainWindow.BtnExportPdfMode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        //private void GoToExportSchedules_Click(object sender, RoutedEventArgs e) => _mainWindow.BtnExportSchedulesMode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        //private void GoToImportExcel_Click(object sender, RoutedEventArgs e) => _mainWindow.BtnImportExcelMode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        private void GoToCopy_Click(object sender, RoutedEventArgs e) => _mainWindow.BtnCopyMode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        //private void GoToDuplicate_Click(object sender, RoutedEventArgs e) => _mainWindow.BtnDuplicateMode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        private void GoToDelete_Click(object sender, RoutedEventArgs e) => _mainWindow.BtnDeleteMode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        private void GoToSheetNumbering_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.BtnSheetNumbering.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        private void GoToSheetPrintSets_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.BtnSheetPrintSets.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
    }
}