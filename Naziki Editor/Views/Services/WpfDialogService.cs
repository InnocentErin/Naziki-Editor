using Microsoft.Win32;
using Naziki_Editor.Core.Abstractions;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace Naziki_Editor.Views.Services
{
    /// <summary>
    /// WPF 平台对话框服务实现，封装 MessageBox 和 OpenFileDialog/SaveFileDialog。
    /// 通过 IDialogService 接口注入，彻底解耦 View 层与 Core 层的对话框依赖。
    /// </summary>
    public class WpfDialogService : IDialogService
    {
        public void ShowMessage(string message, string title = "", DialogMessageType type = DialogMessageType.Info)
        {
            try
            {
                var icon = type switch
                {
                    DialogMessageType.Warning => MessageBoxImage.Warning,
                    DialogMessageType.Error => MessageBoxImage.Error,
                    DialogMessageType.Question => MessageBoxImage.Question,
                    _ => MessageBoxImage.Information
                };

                var button = type == DialogMessageType.Question ? MessageBoxButton.YesNo : MessageBoxButton.OK;

                if (Application.Current?.Dispatcher != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(message, string.IsNullOrEmpty(title) ? "Naziki Editor" : title, button, icon);
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WpfDialogService.ShowMessage] Error: {ex.Message}");
            }
        }

        public ConfirmResult ShowConfirm(string message, string title, DialogMessageType type = DialogMessageType.Question)
        {
            try
            {
                var icon = type switch
                {
                    DialogMessageType.Warning => MessageBoxImage.Warning,
                    DialogMessageType.Error => MessageBoxImage.Error,
                    _ => MessageBoxImage.Question
                };

                MessageBoxResult result = MessageBoxResult.Cancel;
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    result = MessageBox.Show(message, string.IsNullOrEmpty(title) ? "Naziki Editor" : title,
                        MessageBoxButton.YesNoCancel, icon);
                });

                return result switch
                {
                    MessageBoxResult.Yes => ConfirmResult.Yes,
                    MessageBoxResult.No => ConfirmResult.No,
                    _ => ConfirmResult.Cancel
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WpfDialogService.ShowConfirm] Error: {ex.Message}");
                return ConfirmResult.Cancel;
            }
        }

        public bool ShowYesNo(string message, string title)
        {
            try
            {
                MessageBoxResult result = MessageBoxResult.No;
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    result = MessageBox.Show(message, string.IsNullOrEmpty(title) ? "Naziki Editor" : title,
                        MessageBoxButton.YesNo, MessageBoxImage.Question);
                });
                return result == MessageBoxResult.Yes;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WpfDialogService.ShowYesNo] Error: {ex.Message}");
                return false;
            }
        }

        public string? ShowOpenFileDialog(string title, string filter)
        {
            try
            {
                string? filePath = null;
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    var dialog = new OpenFileDialog
                    {
                        Title = title,
                        Filter = filter
                    };
                    if (dialog.ShowDialog() == true)
                    {
                        filePath = dialog.FileName;
                    }
                });
                return filePath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WpfDialogService.ShowOpenFileDialog] Error: {ex.Message}");
                return null;
            }
        }

        public string? ShowSaveFileDialog(string title, string filter, string defaultFileName = "")
        {
            try
            {
                string? filePath = null;
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    var dialog = new SaveFileDialog
                    {
                        Title = title,
                        Filter = filter,
                        FileName = defaultFileName
                    };
                    if (dialog.ShowDialog() == true)
                    {
                        filePath = dialog.FileName;
                    }
                });
                return filePath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WpfDialogService.ShowSaveFileDialog] Error: {ex.Message}");
                return null;
            }
        }

        public Task<string?> ShowOpenFileDialogAsync(string title, string filter)
        {
            return Task.FromResult(ShowOpenFileDialog(title, filter));
        }

        public Task<string?> ShowSaveFileDialogAsync(string title, string filter, string defaultFileName)
        {
            return Task.FromResult(ShowSaveFileDialog(title, filter, defaultFileName));
        }

        public Task<bool> ShowConfirmAsync(string title, string message)
        {
            return Task.FromResult(ShowYesNo(message, title));
        }
    }
}