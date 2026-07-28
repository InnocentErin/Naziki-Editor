using AvalonDock.Layout.Serialization;
using Naziki_Editor.Views.Dialogs;
using System.Globalization;
using System.IO;
using System.Windows;

namespace Naziki_Editor.Views;

public partial class MainWindow
{
    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _defaultWorkspaceLayout ??= SerializeWorkspaceLayout();
        var saved = _workspaceLayoutService.RestoreLastActive();
        if (!string.IsNullOrWhiteSpace(saved))
            DeserializeWorkspaceLayout(saved);
    }

    private string SerializeWorkspaceLayout()
    {
        var serializer = new XmlLayoutSerializer(WorkspaceDockingManager);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        serializer.Serialize(writer);
        return writer.ToString();
    }

    private void DeserializeWorkspaceLayout(string xml)
    {
        var content = new Dictionary<string, object>
        {
            ["Events"] = EventList,
            ["Notes"] = NoteList,
            ["Assets"] = AssetList,
            ["Canvas"] = CanvasArea,
            ["MainTimeline"] = TimelineConsole,
            ["Properties"] = PropertyPanel
        };
        var serializer = new XmlLayoutSerializer(WorkspaceDockingManager);
        serializer.LayoutSerializationCallback += (_, args) =>
        {
            if (args.Model.ContentId is not null &&
                content.TryGetValue(args.Model.ContentId, out var paneContent))
                args.Content = paneContent;
            else
                args.Cancel = true;
        };
        using var reader = new StringReader(xml);
        serializer.Deserialize(reader);
    }

    private void WorkspaceSaveAs_Click(object sender, RoutedEventArgs e)
    {
        var name = InputDialog.ShowInput("请输入工作区布局名称：", "保存工作区布局", owner: this);
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            _workspaceLayoutService.SaveAs(name, SerializeWorkspaceLayout());
            _notificationService.ShowSuccess($"工作区布局“{name}”已保存。");
        }
        catch (Exception ex)
        {
            _dialogService.ShowErrorDialog(ex.Message, "保存工作区失败", ex.ToString());
        }
    }

    private void WorkspaceLoad_Click(object sender, RoutedEventArgs e)
    {
        var layouts = _workspaceLayoutService.ListLayouts();
        var names = string.Join(Environment.NewLine, layouts.Select(item => item.Name));
        var name = InputDialog.ShowInput(
            $"可用布局：{Environment.NewLine}{names}{Environment.NewLine}{Environment.NewLine}请输入要加载的名称：",
            "加载工作区布局", owner: this);
        var selected = layouts.FirstOrDefault(item =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (selected is null) return;
        var xml = _workspaceLayoutService.Activate(selected.Id);
        if (xml is null)
        {
            WorkspaceReset_Click(sender, e);
            return;
        }
        DeserializeWorkspaceLayout(xml);
    }

    private void WorkspaceReset_Click(object sender, RoutedEventArgs e)
    {
        _workspaceLayoutService.ResetToDefault();
        if (!string.IsNullOrWhiteSpace(_defaultWorkspaceLayout))
            DeserializeWorkspaceLayout(_defaultWorkspaceLayout);
    }
}
