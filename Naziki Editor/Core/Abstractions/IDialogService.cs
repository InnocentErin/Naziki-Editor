using System.Threading.Tasks;

namespace Naziki_Editor.Core.Abstractions
{
    /// <summary>
    /// View 层对话框服务接口，供 Core/ViewModel 层解耦调用文件选择、确认等对话框。
    /// 彻底替代 MessageBox.Show 和 OpenFileDialog/SaveFileDialog 的直接调用。
    /// </summary>
    public enum DialogMessageType
    {
        Info,
        Warning,
        Error,
        Question
    }

    public enum ConfirmResult
    {
        Yes,
        No,
        Cancel
    }

    public interface IDialogService
    {
        /// <summary>显示消息对话框</summary>
        void ShowMessage(string message, string title = "", DialogMessageType type = DialogMessageType.Info);

        /// <summary>显示确认对话框，返回 Yes/No/Cancel</summary>
        ConfirmResult ShowConfirm(string message, string title, DialogMessageType type = DialogMessageType.Question);

        /// <summary>显示二选一确认对话框</summary>
        bool ShowYesNo(string message, string title);

        /// <summary>显示错误对话框（含详细错误信息，可折叠查看）</summary>
        void ShowErrorDialog(string message, string title, string? errorDetails = null);

        /// <summary>打开文件选择对话框，返回选中路径或 null</summary>
        string? ShowOpenFileDialog(string title, string filter);

        /// <summary>打开保存文件对话框，返回选中路径或 null</summary>
        string? ShowSaveFileDialog(string title, string filter, string defaultFileName = "");

        // 异步版本
        Task<string?> ShowOpenFileDialogAsync(string title, string filter);
        Task<string?> ShowSaveFileDialogAsync(string title, string filter, string defaultFileName);
        Task<bool> ShowConfirmAsync(string title, string message);
    }
}
