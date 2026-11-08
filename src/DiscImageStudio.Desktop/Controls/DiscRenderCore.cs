using Avalonia;
using Avalonia.Media;

namespace DiscImageStudio.Desktop.Controls;

/// <summary>圆环/十字等共用图元，画布与后续的预览页都用它。</summary>
internal static class DiscRenderCore
{
    public static void DrawDiscBody(DrawingContext context, Point center, double discRadius, double hubRadius)
    {
        context.DrawEllipse(DiscTheme.DiscBody, null, center, discRadius, discRadius);
        context.DrawEllipse(DiscTheme.Hub, null, center, hubRadius, hubRadius);
    }

    public static void DrawGuideCircles(
        DrawingContext context,
        Point center,
        double discRadius,
        double outerRadius,
        double innerRadius)
    {
        Pen pen = new(DiscTheme.Guide, 1);
        context.DrawEllipse(null, pen, center, discRadius, discRadius);
        context.DrawEllipse(null, pen, center, outerRadius, outerRadius);
        if (innerRadius > 0)
        {
            context.DrawEllipse(null, pen, center, innerRadius, innerRadius);
        }
    }

    public static void DrawOriginCross(DrawingContext context, Point center, double arm)
    {
        Pen pen = new(DiscTheme.Guide, 1);
        context.DrawLine(pen, new Point(center.X - arm, center.Y), new Point(center.X + arm, center.Y));
        context.DrawLine(pen, new Point(center.X, center.Y - arm), new Point(center.X, center.Y + arm));
    }

    public static PathFigure CircleFigure(Point center, double radius)
    {
        PathFigure figure = new() { StartPoint = new Point(center.X - radius, center.Y), IsClosed = true };
        figure.Segments!.Add(new ArcSegment
        {
            Point = new Point(center.X + radius, center.Y),
            Size = new Size(radius, radius),
            IsLargeArc = true,
            SweepDirection = SweepDirection.Clockwise,
            IsStroked = false,
        });
        figure.Segments!.Add(new ArcSegment
        {
            Point = new Point(center.X - radius, center.Y),
            Size = new Size(radius, radius),
            IsLargeArc = true,
            SweepDirection = SweepDirection.Clockwise,
            IsStroked = false,
        });
        return figure;
    }

    public static PathFigure RectangleFigure(Rect rect)
    {
        PathFigure figure = new() { StartPoint = rect.TopLeft, IsClosed = true };
        figure.Segments!.Add(new LineSegment { Point = rect.TopRight, IsStroked = true });
        figure.Segments.Add(new LineSegment { Point = rect.BottomRight, IsStroked = true });
        figure.Segments.Add(new LineSegment { Point = rect.BottomLeft, IsStroked = true });
        return figure;
    }
}
