using Microsoft.Win32;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Views.Dialogs;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;

namespace Naziki_Editor.Views.Services
{
    /// <summary>
    /// WPF 平台对话框服务实现，封装自定义 ErrorDialog 和 OpenFileDialog/SaveFileDialog。
    /// 通过 IDialogService 接口注入，彻底解耦 View 层与 Core 层的对话框依赖。
    /// </summary>
    public class WpfDialogService : IDialogService
    {
        #region 消息 / 错误 / 确认对话框

        public void ShowMessage(string message, string title = "", DialogMessageType type = DialogMessageType.Info)
        {
            string iconType = type switch
            {
                DialogMessageType.Warning => "warning",
                DialogMessageType.Error => "error",
                DialogMessageType.Question => "question",
                _ => "info"
            };

            ExecuteOnUIThread(() =>
            {
                ErrorDialog.ShowMessage(message,
                    string.IsNullOrEmpty(title) ? "Naziki Editor" : title,
                    iconType);
            }, nameof(ShowMessage));
        }

        public void ShowErrorDialog(string message, string title = "错误", string? errorDetails = null)
        {
            ExecuteOnUIThread(() =>
            {
                ErrorDialog.ShowError(message,
                    string.IsNullOrEmpty(title) ? "Naziki Editor" : title,
                    errorDetails);
            }, nameof(ShowErrorDialog));
        }

        public ConfirmResult ShowConfirm(string message, string title, DialogMessageType type = DialogMessageType.Question)
        {
            string iconType = type switch
            {
                DialogMessageType.Warning => "warning",
                DialogMessageType.Error => "error",
                _ => "question"
            };

            ErrorDialogResult result = ErrorDialogResult.Cancel;

            ExecuteOnUIThread(() =>
            {
                result = ErrorDialog.ShowConfirm(message,
                    string.IsNullOrEmpty(title) ? "Naziki Editor" : title,
                    iconType);
            }, nameof(ShowConfirm));

            return result switch
            {
                ErrorDialogResult.Yes => ConfirmResult.Yes,
                ErrorDialogResult.No => ConfirmResult.No,
                _ => ConfirmResult.Cancel
            };
        }

        public bool ShowYesNo(string message, string title)
        {
            bool result = false;

            ExecuteOnUIThread(() =>
            {
                result = ErrorDialog.ShowYesNo(message,
                    string.IsNullOrEmpty(title) ? "Naziki Editor" : title);
            }, nameof(ShowYesNo));

            return result;
        }

        #endregion

        #region 文件对话框

        public string? ShowOpenFileDialog(string title, string filter)
        {
            string? filePath = null;

            ExecuteOnUIThread(() =>
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
            }, nameof(ShowOpenFileDialog));

            return filePath;
        }

        public string? ShowSaveFileDialog(string title, string filter, string defaultFileName = "")
        {
            string? filePath = null;

            ExecuteOnUIThread(() =>
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
            }, nameof(ShowSaveFileDialog));

            return filePath;
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

        #endregion

        #region 输入对话框

        public string? ShowInput(string message, string title, string defaultText = "")
        {
            string? result = null;

            ExecuteOnUIThread(() =>
            {
                result = InputDialog.ShowInput(message,
                    string.IsNullOrEmpty(title) ? "Naziki Editor" : title,
                    defaultText);
            }, nameof(ShowInput));

            return result;
        }

        #endregion

        #region 内部辅助

        /// <summary>
        /// 在 UI 线程上安全执行 Action。
        /// 如果已在 UI 线程，直接执行；否则通过 Dispatcher 调度。
        /// 异常会记录到 Trace 并重新抛出，确保问题可追踪。
        /// </summary>
        private static void ExecuteOnUIThread(Action action, string callerName)
        {
            try
            {
                if (Application.Current?.Dispatcher == null)
                {
                    Trace.TraceError($"[WpfDialogService.{callerName}] 错误：Application.Current 为 null，无法调度到 UI 线程。");
                    return;
                }

                if (Application.Current.Dispatcher.CheckAccess())
                {
                    // 已在 UI 线程，直接执行
                    action();
                }
                else
                {
                    // 非 UI 线程，调度到 UI 线程
                    Application.Current.Dispatcher.Invoke(action);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"[WpfDialogService.{callerName}] 未捕获异常：{ex}");
                // 重新抛出，让调用方感知异常
                throw;
            }
        }

        #endregion
    }
}