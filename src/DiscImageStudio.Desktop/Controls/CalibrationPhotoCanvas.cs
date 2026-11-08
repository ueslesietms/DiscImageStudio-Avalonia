using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DiscImageStudio.Desktop.ViewModels;

namespace DiscImageStudio.Desktop.Controls;

/// <summary>标定照片画布：按等比缩放显示照片，点击记录图片像素坐标，并画出已标点和拟合圆。</summary>
public class CalibrationPhotoCanvas : Control
{
    public static readonly StyledProperty<CalibrationPageViewModel?> DesignProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, CalibrationPageViewModel?>(nameof(Design));

    private static readonly IBrush DiscBrush = new SolidColorBrush(Color.FromUInt32(0xFFE4E4E7));
    private static readonly IBrush InnerBrush = new SolidColorBrush(Color.FromUInt32(0xFF17C964));
    private static readonly IBrush OuterBrush = new SolidColorBrush(Color.FromUInt32(0xFFFFA94D));

    public CalibrationPageViewModel? Design
    {
        get => GetValue(DesignProperty);
        set => SetValue(DesignProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DesignProperty)
        {
            if (change.OldValue is CalibrationPageViewModel old)
            {
                old.DiscPoints.CollectionChanged -= OnPointsChanged;
                old.InnerPoints.CollectionChanged -= OnPointsChanged;
                old.OuterPoints.CollectionChanged -= OnPointsChanged;
                old.PropertyChanged -= OnDesignPropertyChanged;
            }

            if (Design is { } design)
            {
                design.DiscPoints.CollectionChanged += OnPointsChanged;
                design.InnerPoints.CollectionChanged += OnPointsChanged;
                design.OuterPoints.CollectionChanged += OnPointsChanged;
                design.PropertyChanged += OnDesignPropertyChanged;
            }

            InvalidateVisual();
        }
    }

    private void OnPointsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
        => InvalidateVisual();

    private void OnDesignPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        => InvalidateVisual();

    private (double Scale, Point Offset)? Layout()
    {
        if (Design?.Photo is not { } photo || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return null;
        }

        double scale = Math.Min(Bounds.Width / photo.PixelSize.Width, Bounds.Height / photo.PixelSize.Height);
        return (scale, new Point(
            (Bounds.Width - photo.PixelSize.Width * scale) / 2,
            (Bounds.Height - photo.PixelSize.Height * scale) / 2));
    }

    public override void Render(DrawingContext context)
    {
        context.DrawRectangle(new SolidColorBrush(Color.FromUInt32(0xFF0B0B0D)), null, new Rect(Bounds.Size));
        if (Design is not { Photo: { } photo })
        {
            return;
        }

        if (Layout() is not { } layout)
        {
            return;
        }

        Rect target = new(layout.Offset.X, layout.Offset.Y,
            photo.PixelSize.Width * layout.Scale, photo.PixelSize.Height * layout.Scale);
        context.DrawImage(photo, target);

        DrawEdge(context, Design.DiscPoints, DiscBrush, layout);
        DrawEdge(context, Design.InnerPoints, InnerBrush, layout);
        DrawEdge(context, Design.OuterPoints, OuterBrush, layout);
    }

    private static void DrawEdge(DrawingContext context, IReadOnlyList<Point> points, IBrush brush,
        (double Scale, Point Offset) layout)
    {
        if (points.Count == 0)
        {
            return;
        }

        foreach (Point point in points)
        {
            Point screen = new(layout.Offset.X + point.X * layout.Scale, layout.Offset.Y + point.Y * layout.Scale);
            context.DrawEllipse(brush, null, screen, 3.5, 3.5);
        }

        // 连线只用来确认点的顺序，不参与拟合。
        if (points.Count >= 2)
        {
            Pen pen = new(brush, 1);
            for (int index = 1; index < points.Count; index++)
            {
                Point a = new(layout.Offset.X + points[index - 1].X * layout.Scale,
                    layout.Offset.Y + points[index - 1].Y * layout.Scale);
                Point b = new(layout.Offset.X + points[index].X * layout.Scale,
                    layout.Offset.Y + points[index].Y * layout.Scale);
                context.DrawLine(pen, a, b);
            }
        }

        if (points.Count >= 3 && CalibrationPageViewModel.FitCircle(points) is { } circle)
        {
            Point center = new(layout.Offset.X + circle.Center.X * layout.Scale,
                layout.Offset.Y + circle.Center.Y * layout.Scale);
            Pen pen = new(brush, 1, new DashStyle([6, 4], 0));
            context.DrawEllipse(null, pen, center, circle.Radius * layout.Scale, circle.Radius * layout.Scale);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Design is null || Layout() is not { } layout)
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Point screen = e.GetPosition(this);
        Point imagePoint = new(
            (screen.X - layout.Offset.X) / layout.Scale,
            (screen.Y - layout.Offset.Y) / layout.Scale);
        Design.AddPoint(imagePoint);
        InvalidateVisual();
        e.Handled = true;
    }
}
