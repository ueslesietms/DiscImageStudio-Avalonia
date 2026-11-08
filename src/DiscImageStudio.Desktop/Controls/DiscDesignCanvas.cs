using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DiscImageStudio.Desktop.ViewModels;
using DiscImageStudio.Localization;

namespace DiscImageStudio.Desktop.Controls;

/// <summary>直接操作的设计画布：盘体与内容环引导、图层变换、选中框与角点缩放手柄、旋转手柄、
/// 滚轮缩放与中键平移。选中状态和图层数据在 DesignPageViewModel 里，画布只改选中图层的变换。</summary>
public class DiscDesignCanvas : Control
{
    private const double ViewMarginMm = 3.0;
    private const double HandleSize = 8.0;
    private const double RotateHandleDistance = 26.0;
    private const double MinScale = 0.05;
    private const double MinZoom = 0.25;
    private const double MaxZoom = 12.0;
    private const double ZoomStep = 1.25;
    private const double WheelZoomStep = 1.15;

    private DesignPageViewModel? _design;
    private NotifyCollectionChangedEventHandler? _collectionHandler;
    private DragMode _dragMode;
    private Point _dragStartMm;
    private double _dragStartTranslateX;
    private double _dragStartTranslateY;
    private double _dragStartScale = 1.0;
    private double _dragStartDistanceMm = 1.0;
    private double _dragStartRotationDeg;
    private double _dragStartMouseAngleDeg;
    private bool _isDragging;
    private bool _isPanning;
    private Vector _panOffset;
    private Vector _panStartOffset;
    private Point _panStartMouse;
    private double _zoomFactor = 1.0;

    private enum DragMode
    {
        None,
        Move,
        Scale,
        Rotate,
    }

    public DiscDesignCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    public double ZoomFactor
    {
        get => _zoomFactor;
        set
        {
            double clamped = Math.Clamp(value, MinZoom, MaxZoom);
            if (Math.Abs(clamped - _zoomFactor) < double.Epsilon)
            {
                return;
            }

            _zoomFactor = clamped;
            ZoomChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    public event EventHandler? ZoomChanged;

    public void ResetView()
    {
        _zoomFactor = 1.0;
        _panOffset = default;
        InvalidateVisual();
    }

    public void ZoomIn() => ZoomAt(ZoomStep, ViewCenter);

    public void ZoomOut() => ZoomAt(1.0 / ZoomStep, ViewCenter);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        AttachDesign();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachDesign();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        DetachDesign();
    }

    private void AttachDesign()
    {
        DetachDesign();
        if (DataContext is not DesignPageViewModel design)
        {
            return;
        }

        _design = design;
        _collectionHandler = (_, args) =>
        {
            InvalidateVisual();
            if (args.OldItems is not null)
            {
                foreach (DesignLayerViewModel layer in args.OldItems)
                {
                    layer.PropertyChanged -= OnLayerPropertyChanged;
                }
            }

            if (args.NewItems is not null)
            {
                foreach (DesignLayerViewModel layer in args.NewItems)
                {
                    layer.PropertyChanged += OnLayerPropertyChanged;
                }
            }
        };
        design.Layers.CollectionChanged += _collectionHandler;
        foreach (DesignLayerViewModel layer in design.Layers)
        {
            layer.PropertyChanged += OnLayerPropertyChanged;
        }

        design.PropertyChanged += OnDesignPropertyChanged;
        InvalidateVisual();
    }

    private void DetachDesign()
    {
        if (_design is not null)
        {
            if (_collectionHandler is not null)
            {
                _design.Layers.CollectionChanged -= _collectionHandler;
            }

            _design.PropertyChanged -= OnDesignPropertyChanged;
            foreach (DesignLayerViewModel layer in _design.Layers)
            {
                layer.PropertyChanged -= OnLayerPropertyChanged;
            }
        }

        _collectionHandler = null;
        _design = null;
    }

    private void OnLayerPropertyChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    private void OnDesignPropertyChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    // ---- 坐标换算 ----

    private double FitScale
    {
        get
        {
            double radiusMm = DesignPageViewModel.DiscRadiusMm + ViewMarginMm;
            return Math.Min(Bounds.Width / (2 * radiusMm), Bounds.Height / (2 * radiusMm));
        }
    }

    private double ViewScale => FitScale * _zoomFactor;

    private Point ViewCenterBase => new(Bounds.Width / 2, Bounds.Height / 2);

    private Point ViewCenter => new(
        ViewCenterBase.X + _panOffset.X,
        ViewCenterBase.Y + _panOffset.Y);

    private Point ScreenFromMm(double xMm, double yMm)
    {
        Point center = ViewCenter;
        double scale = ViewScale;
        return new Point(center.X + xMm * scale, center.Y + yMm * scale);
    }

    private Point MmFromScreen(Point point)
    {
        Point center = ViewCenter;
        double scale = ViewScale;
        return new Point((point.X - center.X) / scale, (point.Y - center.Y) / scale);
    }

    private double ScreenLength(double mm) => mm * ViewScale;

    // ---- 几何 ----

    private static double SizeWidthMm(DesignLayerViewModel layer) => layer.BaseWidthMm * layer.Scale;

    private static double SizeHeightMm(DesignLayerViewModel layer) => layer.BaseHeightMm * layer.Scale;

    private static Point[] LayerCorners(DesignLayerViewModel layer)
    {
        double halfWidth = SizeWidthMm(layer) / 2;
        double halfHeight = SizeHeightMm(layer) / 2;
        double radians = layer.RotationDeg * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        (double dx, double dy)[] locals =
        [
            (-halfWidth, -halfHeight),
            (halfWidth, -halfHeight),
            (halfWidth, halfHeight),
            (-halfWidth, halfHeight),
        ];
        return locals
            .Select(local => new Point(
                layer.TranslateXMm + local.dx * cos - local.dy * sin,
                layer.TranslateYMm + local.dx * sin + local.dy * cos))
            .ToArray();
    }

    private static Geometry PolygonGeometry(Point[] points)
    {
        PathGeometry geometry = new();
        PathFigure figure = new() { StartPoint = points[0], IsClosed = true, IsFilled = false };
        for (int index = 1; index < points.Length; index++)
        {
            figure.Segments!.Add(new LineSegment { Point = points[index], IsStroked = true });
        }

        geometry.Figures = new PathFigures { figure };
        return geometry;
    }

    private static bool PointInPolygon(Point point, Point[] polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            bool crosses = (polygon[i].Y > point.Y) != (polygon[j].Y > point.Y)
                && point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y)
                    + polygon[i].X;
            if (crosses)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    // ---- 绘制 ----

    public override void Render(DrawingContext context)
    {
        context.DrawRectangle(DiscTheme.Backdrop, null, new Rect(Bounds.Size));
        if (_design is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        double discRadius = ScreenLength(DesignPageViewModel.DiscRadiusMm);
        double hubRadius = ScreenLength(DesignPageViewModel.HubRadiusMm);
        double innerRadius = ScreenLength(_design.ContentInnerRadiusMm);
        double outerRadius = ScreenLength(Math.Min(_design.ContentOuterRadiusMm, DesignPageViewModel.DiscRadiusMm));
        Point center = ViewCenter;

        DiscRenderCore.DrawDiscBody(context, center, discRadius, hubRadius);
        DiscRenderCore.DrawGuideCircles(context, center, discRadius, outerRadius, innerRadius);
        DiscRenderCore.DrawOriginCross(context, center, 5);

        for (int index = _design.Layers.Count - 1; index >= 0; index--)
        {
            DrawLayer(context, _design.Layers[index]);
        }

        DrawDimMask(context, center, outerRadius, innerRadius);

        if (_design.SelectedLayer is { } selected)
        {
            DrawSelection(context, selected);
        }

        if (!_design.HasLayers)
        {
            DrawEmptyHint(context, center);
        }
    }

    private void DrawLayer(DrawingContext context, DesignLayerViewModel layer)
    {
        double widthPx = ScreenLength(SizeWidthMm(layer));
        double heightPx = ScreenLength(SizeHeightMm(layer));
        if (widthPx <= 0 || heightPx <= 0)
        {
            return;
        }

        if (!layer.IsVisible)
        {
            Pen hiddenPen = new(DiscTheme.Guide, 1, new DashStyle([4, 4], 0));
            Point[] screenCorners = LayerCorners(layer)
                .Select(corner => ScreenFromMm(corner.X, corner.Y))
                .ToArray();
            context.DrawGeometry(null, hiddenPen, PolygonGeometry(screenCorners));
            return;
        }

        if (layer.HasImage)
        {
            Point centerPt = ScreenFromMm(layer.TranslateXMm, layer.TranslateYMm);
            Matrix matrix = Matrix.CreateRotation(layer.RotationDeg * Math.PI / 180.0)
                * Matrix.CreateTranslation(centerPt.X, centerPt.Y);
            using (context.PushTransform(matrix))
            {
                context.DrawImage(layer.Image!, new Rect(-widthPx / 2, -heightPx / 2, widthPx, heightPx));
            }
        }
        else
        {
            Pen placeholderPen = new(DiscTheme.MissingImage, 1, new DashStyle([4, 4], 0));
            Point[] screenCorners = LayerCorners(layer)
                .Select(corner => ScreenFromMm(corner.X, corner.Y))
                .ToArray();
            context.DrawGeometry(null, placeholderPen, PolygonGeometry(screenCorners));
        }
    }

    private void DrawDimMask(DrawingContext context, Point center, double outerRadius, double innerRadius)
    {
        PathGeometry geometry = new() { FillRule = FillRule.EvenOdd };
        geometry.Figures = new PathFigures
        {
            DiscRenderCore.RectangleFigure(new Rect(Bounds.Size)),
            DiscRenderCore.CircleFigure(center, outerRadius),
            DiscRenderCore.CircleFigure(center, innerRadius),
        };
        context.DrawGeometry(DiscTheme.Mask, null, geometry);
    }

    private void DrawSelection(DrawingContext context, DesignLayerViewModel layer)
    {
        Point[] cornersMm = LayerCorners(layer);
        Point[] corners = cornersMm
            .Select(corner => ScreenFromMm(corner.X, corner.Y))
            .ToArray();
        Pen outlinePen = new(DiscTheme.Selection, 1.5);
        context.DrawGeometry(null, outlinePen, PolygonGeometry(corners));

        foreach (Point corner in corners)
        {
            Rect handle = new(corner.X - HandleSize / 2, corner.Y - HandleSize / 2, HandleSize, HandleSize);
            context.DrawRectangle(Brushes.White, new Pen(DiscTheme.Selection, 1.5), handle);
        }

        Point topMidMm = new(
            (cornersMm[0].X + cornersMm[1].X) / 2,
            (cornersMm[0].Y + cornersMm[1].Y) / 2);
        Point centerMm = new(layer.TranslateXMm, layer.TranslateYMm);
        double dx = topMidMm.X - centerMm.X;
        double dy = topMidMm.Y - centerMm.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0)
        {
            dx = 0;
            dy = -1;
            length = 1;
        }

        double extensionMm = RotateHandleDistance / Math.Max(0.001, ViewScale);
        Point handleMm = new(
            topMidMm.X + dx / length * extensionMm,
            topMidMm.Y + dy / length * extensionMm);
        Point handleTop = ScreenFromMm(topMidMm.X, topMidMm.Y);
        Point handlePoint = ScreenFromMm(handleMm.X, handleMm.Y);
        context.DrawLine(outlinePen, handleTop, handlePoint);
        context.DrawEllipse(Brushes.White, new Pen(DiscTheme.Selection, 1.5),
            handlePoint, HandleSize / 2, HandleSize / 2);
    }

    private void DrawEmptyHint(DrawingContext context, Point center)
    {
        FormattedText text = new(
            L.T("点击左侧「导入图片」开始设计"),
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            DiscTheme.Typeface,
            14,
            DiscTheme.Guide);
        context.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }

    // ---- 命中测试 ----

    private static bool Near(Point a, Point b, double tolerance) =>
        Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance;

    private (int CornerIndex, Point ScreenPoint)? HitScaleHandle(Point mouse)
    {
        if (_design?.SelectedLayer is not { } layer)
        {
            return null;
        }

        Point[] cornersMm = LayerCorners(layer);
        for (int index = 0; index < cornersMm.Length; index++)
        {
            Point corner = ScreenFromMm(cornersMm[index].X, cornersMm[index].Y);
            if (Near(mouse, corner, HandleSize))
            {
                return (index, corner);
            }
        }

        return null;
    }

    private Point? RotateHandlePoint(DesignLayerViewModel layer)
    {
        Point[] cornersMm = LayerCorners(layer);
        Point centerMm = new(layer.TranslateXMm, layer.TranslateYMm);
        double dx = (cornersMm[0].X + cornersMm[1].X) / 2 - centerMm.X;
        double dy = (cornersMm[0].Y + cornersMm[1].Y) / 2 - centerMm.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0)
        {
            return null;
        }

        double extensionMm = RotateHandleDistance / Math.Max(0.001, ViewScale);
        Point handleMm = new(
            centerMm.X + dx / length * (length + extensionMm),
            centerMm.Y + dy / length * (length + extensionMm));
        return ScreenFromMm(handleMm.X, handleMm.Y);
    }

    private DesignLayerViewModel? HitTestLayer(Point mouseMm)
    {
        if (_design is null)
        {
            return null;
        }

        DesignLayerViewModel? hidden = null;
        foreach (DesignLayerViewModel layer in _design.Layers)
        {
            if (!PointInPolygon(mouseMm, LayerCorners(layer)))
            {
                continue;
            }

            if (layer.IsVisible)
            {
                return layer;
            }

            hidden ??= layer;
        }

        return hidden;
    }

    // ---- 交互 ----

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_design is null
            || (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt)) == 0)
        {
            return;
        }

        ZoomAt(e.Delta.Y > 0 ? WheelZoomStep : 1.0 / WheelZoomStep, e.GetPosition(this));
        e.Handled = true;
    }

    private void ZoomAt(double factor, Point anchor)
    {
        double fit = FitScale;
        if (fit <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        Point anchorMm = MmFromScreen(anchor);
        _zoomFactor = Math.Clamp(_zoomFactor * factor, MinZoom, MaxZoom);
        _panOffset = _zoomFactor > 1.0001
            ? ClampPan(new Vector(anchor.X, anchor.Y)
                - new Vector(ViewCenterBase.X, ViewCenterBase.Y)
                - new Vector(anchorMm.X, anchorMm.Y) * (fit * _zoomFactor))
            : default;
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_design is null)
        {
            return;
        }

        PointerPoint point = e.GetCurrentPoint(this);
        Point mouse = point.Position;

        if (point.Properties.IsMiddleButtonPressed)
        {
            if (_zoomFactor <= 1.0001)
            {
                return;
            }

            _isPanning = true;
            _panStartOffset = _panOffset;
            _panStartMouse = mouse;
            Cursor = new Cursor(StandardCursorType.SizeAll);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus();
        Point mouseMm = MmFromScreen(mouse);

        if (e.ClickCount == 2
            && _design.SelectedLayer is null
            && HitScaleHandle(mouse) is null
            && HitTestLayer(mouseMm) is null)
        {
            ResetView();
            return;
        }

        if (HitScaleHandle(mouse) is { } scaleHit)
        {
            DesignLayerViewModel scaleLayer = _design.SelectedLayer!;
            _dragMode = DragMode.Scale;
            _dragStartMm = mouseMm;
            _dragStartScale = scaleLayer.Scale;
            double dx = mouseMm.X - scaleLayer.TranslateXMm;
            double dy = mouseMm.Y - scaleLayer.TranslateYMm;
            _dragStartDistanceMm = Math.Max(0.001, Math.Sqrt(dx * dx + dy * dy));
            _isDragging = true;
            e.Pointer.Capture(this);
            return;
        }

        if (_design.SelectedLayer is { } selected && RotateHandlePoint(selected) is { } rotatePoint
            && Near(mouse, rotatePoint, HandleSize + 2))
        {
            _dragMode = DragMode.Rotate;
            _dragStartRotationDeg = selected.RotationDeg;
            _dragStartMouseAngleDeg = AngleFromCenter(selected, mouseMm);
            _isDragging = true;
            e.Pointer.Capture(this);
            return;
        }

        if (HitTestLayer(mouseMm) is { } hit)
        {
            _design.SelectedLayer = hit;
            _dragMode = DragMode.Move;
            _dragStartMm = mouseMm;
            _dragStartTranslateX = hit.TranslateXMm;
            _dragStartTranslateY = hit.TranslateYMm;
            _isDragging = true;
            e.Pointer.Capture(this);
            return;
        }

        _design.SelectedLayer = null;
    }

    private static double AngleFromCenter(DesignLayerViewModel layer, Point mouseMm)
    {
        double dx = mouseMm.X - layer.TranslateXMm;
        double dy = mouseMm.Y - layer.TranslateYMm;
        return Math.Atan2(dy, dx) * 180.0 / Math.PI;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_isPanning)
        {
            Point current = e.GetPosition(this);
            _panOffset = ClampPan(_panStartOffset + new Vector(
                current.X - _panStartMouse.X,
                current.Y - _panStartMouse.Y));
            InvalidateVisual();
            return;
        }

        if (!_isDragging || _design?.SelectedLayer is not { } layer)
        {
            UpdateHoverCursor(e);
            return;
        }

        Point mouseMm = MmFromScreen(e.GetPosition(this));
        switch (_dragMode)
        {
            case DragMode.Move:
                layer.TranslateXMm = _dragStartTranslateX + (mouseMm.X - _dragStartMm.X);
                layer.TranslateYMm = _dragStartTranslateY + (mouseMm.Y - _dragStartMm.Y);
                break;
            case DragMode.Scale:
            {
                double dx = mouseMm.X - layer.TranslateXMm;
                double dy = mouseMm.Y - layer.TranslateYMm;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                layer.Scale = Math.Max(MinScale, _dragStartScale * distance / _dragStartDistanceMm);
                break;
            }
            case DragMode.Rotate:
            {
                double delta = AngleFromCenter(layer, mouseMm) - _dragStartMouseAngleDeg;
                while (delta <= -180)
                {
                    delta += 360;
                }

                while (delta > 180)
                {
                    delta -= 360;
                }

                double angle = _dragStartRotationDeg + delta;
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    angle = Math.Round(angle / 90.0) * 90.0;
                    if (angle == 360)
                    {
                        angle = 0;
                    }
                }

                layer.RotationDeg = Math.Round(angle, 1);
                break;
            }
        }
    }

    private void UpdateHoverCursor(PointerEventArgs e)
    {
        if (_design is null)
        {
            return;
        }

        Point mouse = e.GetPosition(this);
        bool onHandle = HitScaleHandle(mouse) is not null
            || (_design.SelectedLayer is { } selected
                && RotateHandlePoint(selected) is { } rotate
                && Near(mouse, rotate, HandleSize + 2));
        Cursor = new Cursor(onHandle ? StandardCursorType.SizeAll : StandardCursorType.Arrow);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isPanning)
        {
            _isPanning = false;
            Cursor = new Cursor(StandardCursorType.Arrow);
            e.Pointer.Capture(null);
        }

        if (_isDragging)
        {
            _isDragging = false;
            _dragMode = DragMode.None;
            e.Pointer.Capture(null);
        }
    }

    private Vector ClampPan(Vector pan)
    {
        double fit = FitScale;
        if (fit <= 0 || _zoomFactor <= 1.0001)
        {
            return default;
        }

        double maxLength = fit * _zoomFactor * DesignPageViewModel.DiscRadiusMm;
        return pan.Length > maxLength ? pan * (maxLength / pan.Length) : pan;
    }
}
