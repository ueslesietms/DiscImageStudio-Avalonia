using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DiscImageStudio.Desktop.Views;

public partial class CalibrationView : UserControl
{
    public CalibrationView() => InitializeComponent();

    private void OnRemoveTracePoint(object? sender, RoutedEventArgs e) => PhotoCanvas.RemoveSelectedTracePoint();

    private void OnZoomIn(object? sender, RoutedEventArgs e) => PhotoCanvas.ZoomAt(1.2, Center());

    private void OnZoomOut(object? sender, RoutedEventArgs e) => PhotoCanvas.ZoomAt(1 / 1.2, Center());

    private void OnResetView(object? sender, RoutedEventArgs e) => PhotoCanvas.ResetView();

    private Avalonia.Point Center() => new(PhotoCanvas.Bounds.Width / 2, PhotoCanvas.Bounds.Height / 2);
}
