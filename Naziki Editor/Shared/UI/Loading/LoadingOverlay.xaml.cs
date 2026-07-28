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

    public bool IsLoading { get => (bool)GetValue(IsLoadingProperty); set => SetValue(IsLoadingProperty, value); }
    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public bool IsIndeterminate { get => (bool)GetValue(IsIndeterminateProperty); set => SetValue(IsIndeterminateProperty, value); }

    public LoadingOverlay() => InitializeComponent();

    private static void OnIsLoadingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LoadingOverlay overlay)
        {
            overlay.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
            overlay.IsHitTestVisible = (bool)e.NewValue;
        }
    }
}
