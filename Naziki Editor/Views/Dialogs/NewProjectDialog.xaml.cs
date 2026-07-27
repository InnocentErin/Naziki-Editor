using System.Windows;

namespace Naziki_Editor.Views.Dialogs
{
    public partial class NewProjectDialog : Window
    {
        public string ProjectName => TxtProjectName.Text.Trim();
        public bool ImportChart => ChkImportChart.IsChecked == true;
        public bool ImportAudio => ChkImportAudio.IsChecked == true;

        public NewProjectDialog()
        {
            InitializeComponent();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtProjectName.Text))
            {
                MessageBox.Show("请输入项目名称！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
            Close();
        }
    }
}