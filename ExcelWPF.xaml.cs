using System.Windows;
using TNovCommon;

namespace TNovViewsSheets
{
    /// <summary>
    /// Логика взаимодействия для ExcelWPF.xaml
    /// </summary>
    public partial class ExcelWPF : Window
    {
        public ExcelWPF(ExcelViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
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

        private void Border_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {

        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            
            string commandText = @"https://portal.talan.group/knowledge/proektirovanie/plaginyiskriptynovatsiya/";
            var proc = new System.Diagnostics.Process();
            proc.StartInfo.FileName = commandText;
            proc.StartInfo.UseShellExecute = true;
            proc.Start();
        }
    }
}
