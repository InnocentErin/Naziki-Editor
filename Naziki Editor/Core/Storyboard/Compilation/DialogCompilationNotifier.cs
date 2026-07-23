using Naziki_Editor.Core.Abstractions;

namespace Naziki_Editor.Core.Storyboard.Compilation
{
    public class DialogCompilationNotifier : ICompilationNotifier
    {
        private readonly IDialogService _dialogService;

        public DialogCompilationNotifier(IDialogService dialogService)
        {
            _dialogService = dialogService;
        }

        public void NotifyInfo(string title, string message)
            => _dialogService.ShowMessage(message, title, DialogMessageType.Info);

        public void NotifyWarning(string title, string message)
            => _dialogService.ShowMessage(message, title, DialogMessageType.Warning);
    }
}