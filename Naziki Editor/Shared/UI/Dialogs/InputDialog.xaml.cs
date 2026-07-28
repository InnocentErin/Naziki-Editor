using System.Windows;

namespace Naziki_Editor.Views.Dialogs
{
    /// <summary>
    /// 简单输入对话框，用于获取用户文本输入（如重命名事件 ID）。
    /// </summary>
    public partial class InputDialog : Window
    {
        public string? Result { get; private set; }

        public InputDialog(string message, string title, string defaultText = "")
        {
            InitializeComponent();
            Title = title;
            TxtMessage.Text = message;
            TxtInput.Text = defaultText;
            TxtInput.SelectAll();
            TxtInput.Focus();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            Result = TxtInput.Text;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Result = null;
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// 静态方法：显示输入对话框并返回用户输入。
        /// </summary>
        public static string? ShowInput(string message, string title, string defaultText = "", Window? owner = null)
        {
            string? result = null;

            if (Application.Current?.Dispatcher != null)
            {
                if (Application.Current.Dispatcher.CheckAccess())
                {
                    result = ShowInputInternal(message, title, defaultText, owner);
                }
                else
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        result = ShowInputInternal(message, title, defaultText, owner);
                    });
                }
            }

            return result;
        }

        private static string? ShowInputInternal(string message, string title, string defaultText, Window? owner)
        {
            var dialog = new InputDialog(message, title, defaultText)
            {
                Owner = owner ?? GetActiveWindow()
            };

            if (dialog.ShowDialog() == true)
            {
                return dialog.Result;
            }

            return null;
        }

        private static Window? GetActiveWindow()
        {
            if (Application.Current == null) return null;

            if (Application.Current.MainWindow != null &&
                Application.Current.MainWindow.IsLoaded &&
                Application.Current.MainWindow.Visibility == Visibility.Visible)
            {
                return Application.Current.MainWindow;
            }

            foreach (Window window in Application.Current.Windows)
            {
                if (window.IsLoaded && window.Visibility == Visibility.Visible)
                {
                    return window;
                }
            }

            if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsLoaded)
            {
                return Application.Current.MainWindow;
            }

            return null;
        }
    }
}