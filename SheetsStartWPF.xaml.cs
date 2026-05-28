using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace TNovViewsSheets
{
    /// <summary>
    /// Логика взаимодействия для SheetsStartWPF.xaml
    /// </summary>
    public partial class SheetsStartWPF : Window
    {
        public SheetsStartWPF(SheetsStartViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            this.SizeToContent = SizeToContent.Height;
        }
        private void acceptButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            this.Close(); // закрытие окна
        }

        private void escButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            this.Close(); // закрытие окна
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {

            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }

        private void Border_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {

        }
    }
}
