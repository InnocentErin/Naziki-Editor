using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Naziki_Editor.Shared.Input;

public static class Interaction
{
    public static readonly DependencyProperty DoubleClickCommandProperty =
        DependencyProperty.RegisterAttached(
            "DoubleClickCommand",
            typeof(ICommand),
            typeof(Interaction),
            new PropertyMetadata(null, OnDoubleClickCommandChanged));

    public static readonly DependencyProperty RightClickCommandProperty =
        DependencyProperty.RegisterAttached(
            "RightClickCommand",
            typeof(ICommand),
            typeof(Interaction),
            new PropertyMetadata(null, OnRightClickCommandChanged));

    public static void SetDoubleClickCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(DoubleClickCommandProperty, value);
    public static ICommand? GetDoubleClickCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(DoubleClickCommandProperty);
    public static void SetRightClickCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(RightClickCommandProperty, value);
    public static ICommand? GetRightClickCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(RightClickCommandProperty);

    private static void OnDoubleClickCommandChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not Control control) return;
        control.MouseDoubleClick -= OnMouseDoubleClick;
        if (args.NewValue is ICommand) control.MouseDoubleClick += OnMouseDoubleClick;
    }

    private static void OnRightClickCommandChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not UIElement element) return;
        element.PreviewMouseRightButtonDown -= OnPreviewMouseRightButtonDown;
        if (args.NewValue is ICommand) element.PreviewMouseRightButtonDown += OnPreviewMouseRightButtonDown;
    }

    private static void OnMouseDoubleClick(object sender, MouseButtonEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        var command = GetDoubleClickCommand(element);
        var parameter = element.DataContext;
        if (command?.CanExecute(parameter) != true) return;
        command.Execute(parameter);
        args.Handled = true;
    }

    private static void OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        var command = GetRightClickCommand(element);
        var parameter = element.DataContext;
        if (command?.CanExecute(parameter) != true) return;
        command.Execute(parameter);
    }
}
