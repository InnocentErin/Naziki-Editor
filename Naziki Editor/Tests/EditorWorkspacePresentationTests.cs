using AvalonDock.Controls;
using Naziki_Editor.Views.Dialogs;
using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Xunit;

namespace Naziki_Editor.Tests;

public sealed class EditorWorkspacePresentationTests
{
    [Fact]
    public void EditorWorkspaceStyle_LoadsWithoutPrivateAvalonDockResources()
    {
        RunSta(() =>
        {
            EnsureApplication();
            var resources = new ResourceDictionary
            {
                Source = new Uri(
                    "/Naziki Editor;component/Themes/Base/EditorWorkspace.xaml",
                    UriKind.Relative)
            };

            var style = Assert.IsType<Style>(resources["EditorAnchorablePaneStyle"]);
            Assert.Equal(typeof(LayoutAnchorablePaneControl), style.TargetType);
            Assert.Null(style.BasedOn);

            var itemTemplate = style.Setters
                .OfType<Setter>()
                .Single(setter => setter.Property == ItemsControl.ItemTemplateProperty);
            var contentTemplate = style.Setters
                .OfType<Setter>()
                .Single(setter => setter.Property == TabControl.ContentTemplateProperty);

            Assert.IsType<DataTemplate>(itemTemplate.Value);
            Assert.IsType<DataTemplate>(contentTemplate.Value);

        });
    }

    [Fact]
    public void ErrorDialog_UsesBoundedResizableScrollableLayout()
    {
        RunSta(() =>
        {
            EnsureApplication();
            var dialog = new ErrorDialog(
                "Test error",
                new string('m', 4_000),
                ErrorDialogMode.Error,
                "C:\\" + new string('x', 4_000));

            try
            {
                Assert.Equal(820, dialog.Width);
                Assert.Equal(600, dialog.Height);
                Assert.Equal(ResizeMode.CanResize, dialog.ResizeMode);
                Assert.Equal(SizeToContent.Manual, dialog.SizeToContent);

                var details = Assert.IsType<TextBox>(
                    dialog.FindName("DetailsTextBox"));
                Assert.Equal(TextWrapping.NoWrap, details.TextWrapping);
                Assert.Equal(ScrollBarVisibility.Auto, details.VerticalScrollBarVisibility);
                Assert.Equal(ScrollBarVisibility.Auto, details.HorizontalScrollBarVisibility);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static void EnsureApplication()
    {
        if (Application.Current is null)
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });

        Assert.True(thread.TrySetApartmentState(ApartmentState.STA));
        thread.Start();
        thread.Join();

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
