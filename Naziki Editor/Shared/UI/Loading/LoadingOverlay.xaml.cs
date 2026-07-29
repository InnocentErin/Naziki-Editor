using System.Windows;
using System.Windows.Controls;

namespace Naziki_Editor.Views.Loading;

public partial class LoadingOverlay : UserControl
{
    public static readonly DependencyProperty IsLoadingProperty =
        DependencyProperty.Register(nameof(IsLoading), typeof(bool), typeof(LoadingOverlay),
            new PropertyMetadata(false, OnIsLoadingChanged));

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(LoadingOverlay),
            new PropertyMetadata("正在加载…"));

    public static readonly DependencyProperty IsIndeterminateProperty =
        DependencyProperty.Register(nameof(IsIndeterminate), typeof(bool), typeof(LoadingOverlay),
            new PropertyMetadata(true));
    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(nameof(Progress), typeof(double), typeof(LoadingOverlay),
            new PropertyMetadata(0d));
    public static readonly DependencyProperty IsProgressVisibleProperty =
        DependencyProperty.Register(nameof(IsProgressVisible), typeof(Visibility), typeof(LoadingOverlay),
            new PropertyMetadata(Visibility.Collapsed));
    public static readonly DependencyProperty CanCancelProperty =
        DependencyProperty.Register(nameof(CanCancel), typeof(Visibility), typeof(LoadingOverlay),
            new PropertyMetadata(Visibility.Collapsed));

    public bool IsLoading { get => (bool)GetValue(IsLoadingProperty); set => SetValue(IsLoadingProperty, value); }
    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public bool IsIndeterminate { get => (bool)GetValue(IsIndeterminateProperty); set => SetValue(IsIndeterminateProperty, value); }
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public Visibility IsProgressVisible { get => (Visibility)GetValue(IsProgressVisibleProperty); set => SetValue(IsProgressVisibleProperty, value); }
    public Visibility CanCancel { get => (Visibility)GetValue(CanCancelProperty); set => SetValue(CanCancelProperty, value); }
    internal Action? CancelAction { get; set; }
    internal void SetCancelAction(Action? action)
    {
        CancelAction = action;
        CancelButton.IsEnabled = true;
        CanCancel = action is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public LoadingOverlay() => InitializeComponent();

    private static void OnIsLoadingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LoadingOverlay overlay)
        {
            overlay.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
            overlay.IsHitTestVisible = (bool)e.NewValue;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        CancelAction?.Invoke();
    }
}
