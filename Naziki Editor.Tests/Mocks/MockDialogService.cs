using Naziki_Editor.Core.Abstractions;

namespace Naziki_Editor.Tests.Mocks
{
    public class MockDialogService : IDialogService
    {
        // 模拟弹窗：什么都不做，直接返回“确定”
        public void ShowMessage(string message, string title = "", DialogMessageType type = DialogMessageType.Info) { }
        public ConfirmResult ShowConfirm(string message, string title, DialogMessageType type = DialogMessageType.Question) => ConfirmResult.Yes;
        public bool ShowYesNo(string message, string title) => true;
        public string? ShowOpenFileDialog(string title, string filter) => @"C:\Test\sample.nep"; // 测试时直接返回固定路径
        public string? ShowSaveFileDialog(string title, string filter, string defaultFileName = "") => @"C:\Test\sample.nep";
        public Task<string?> ShowOpenFileDialogAsync(string title, string filter) => Task.FromResult(@"C:\Test\sample.nep");
        public Task<string?> ShowSaveFileDialogAsync(string title, string filter, string defaultFileName) => Task.FromResult(@"C:\Test\sample.nep");
        public Task<bool> ShowConfirmAsync(string title, string message) => Task.FromResult(true);
    }
}