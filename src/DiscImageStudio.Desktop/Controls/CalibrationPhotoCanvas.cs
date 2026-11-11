using System.Collections.Specialized;
using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DiscImageStudio.Core.Calibration;
using DiscImageStudio.Localization;

namespace DiscImageStudio.Desktop.Controls;

/// <summary>
/// 标定照片画布：照片与标靶预览、边界轮廓、描线点、粗测环、宽带射线、弧长标记和时钟读数。
/// 世界坐标在照片步是源像素，其余步骤是盘心毫米（Y 向下）。
/// </summary>
public sealed class CalibrationPhotoCanvas : Control
{
    // ---- 画笔 ----
    private static readonly IBrush PhotoBackdrop = new SolidColorBrush(Color.FromRgb(0x12, 0x14, 0x18));
    private static readonly IBrush EdgeBrush = new SolidColorBrush(Color.FromRgb(0x5C, 0xC8, 0xFF));
    private static readonly IBrush HubOutlineBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x5C));
    private static readonly IBrush TraceBrush = new SolidColorBrush(Color.FromRgb(0x6F, 0xE0, 0x9A));
    private static readonly IBrush PredictionBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0x92, 0xFF));
    private static readonly IBrush OutlierBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
    private static readonly IBrush HandleBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xEC, 0xF2));
    private static readonly IBrush OutlineBrush = new SolidColorBrush(Color.FromRgb(0x0B, 0x0C, 0x10));
    private static readonly IBrush CaptionBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0x1B, 0x1E, 0x24));
    private static readonly Typeface Typeface = new("Inter, Segoe UI, Noto Sans, sans-serif");

    private const double ArcHalfAngle = Math.PI / 6;

    public static readonly StyledProperty<Bitmap?> ImageProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, Bitmap?>(nameof(Image));
    public static readonly StyledProperty<Bitmap?> FallbackImageProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, Bitmap?>(nameof(FallbackImage));
    public static readonly StyledProperty<bool> IsPhotoStepProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, bool>(nameof(IsPhotoStep), true);
    public static readonly StyledProperty<bool> IsRectificationPreviewProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, bool>(nameof(IsRectificationPreview));
    public static readonly StyledProperty<bool> IsTargetPreviewProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, bool>(nameof(IsTargetPreview));
    public static readonly StyledProperty<double> CanvasRadiusMmProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, double>(nameof(CanvasRadiusMm), 63);
    public static readonly StyledProperty<double> TargetCanvasRadiusMmProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, double>(nameof(TargetCanvasRadiusMm), 60);
    public static readonly StyledProperty<IEnumerable<CalibrationPhotoBoundaryPoint>?> BoundaryPointsProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, IEnumerable<CalibrationPhotoBoundaryPoint>?>(nameof(BoundaryPoints));
    public static readonly StyledProperty<IReadOnlyList<Point[]>?> BoundaryContoursProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, IReadOnlyList<Point[]>?>(nameof(BoundaryContours));
    public static readonly StyledProperty<IEnumerable<CalibrationTracePoint>?> TracePointsProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, IEnumerable<CalibrationTracePoint>?>(nameof(TracePoints));
    public static readonly StyledProperty<IReadOnlyList<Point[]>?> PredictionContoursProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, IReadOnlyList<Point[]>?>(nameof(PredictionContours));
    public static readonly StyledProperty<IReadOnlyList<int>?> OutlierTracePointIndicesProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, IReadOnlyList<int>?>(nameof(OutlierTracePointIndices));
    public static readonly StyledProperty<int> SelectedTraceArmProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, int>(nameof(SelectedTraceArm));
    public static readonly StyledProperty<bool> IsBoundarySegmentModeProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, bool>(nameof(IsBoundarySegmentMode));
    public static readonly StyledProperty<ICommand?> BoundaryEditedCommandProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, ICommand?>(nameof(BoundaryEditedCommand));
    public static readonly StyledProperty<ICommand?> TraceEditedCommandProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, ICommand?>(nameof(TraceEditedCommand));
    public static readonly StyledProperty<bool> IsCoarseModeProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, bool>(nameof(IsCoarseMode));
    public static readonly StyledProperty<bool> HasRadialArcTargetProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, bool>(nameof(HasRadialArcTarget));
    public static readonly StyledProperty<bool> HasWideBandTargetProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, bool>(nameof(HasWideBandTarget));
    public static readonly StyledProperty<bool> IsBandRayModeProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, bool>(nameof(IsBandRayMode));
    public static readonly StyledProperty<double?> BandRayAngleDegreesProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, double?>(nameof(BandRayAngleDegrees));
    public static readonly StyledProperty<ICommand?> BandRayPointCommandProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, ICommand?>(nameof(BandRayPointCommand));
    public static readonly StyledProperty<IReadOnlyList<CalibrationPoint>?> ArcLengthPointsProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, IReadOnlyList<CalibrationPoint>?>(nameof(ArcLengthPoints));
    public static readonly StyledProperty<ICommand?> ArcLengthPointCommandProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, ICommand?>(nameof(ArcLengthPointCommand));
    public static readonly StyledProperty<ICommand?> UndoArcLengthPointCommandProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, ICommand?>(nameof(UndoArcLengthPointCommand));
    public static readonly StyledProperty<bool> HasClockTargetProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, bool>(nameof(HasClockTarget));
    public static readonly StyledProperty<IReadOnlyList<double>?> ClockReadAnglesProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, IReadOnlyList<double>?>(nameof(ClockReadAngles));
    public static readonly StyledProperty<double?> PreferredClockAngleProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, double?>(nameof(PreferredClockAngle));
    public static readonly StyledProperty<ICommand?> PreferClockAngleCommandProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, ICommand?>(nameof(PreferClockAngleCommand));
    public static readonly StyledProperty<IEnumerable<CalibrationRingArc>?> RingArcsProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, IEnumerable<CalibrationRingArc>?>(nameof(RingArcs));
    public static readonly StyledProperty<bool> ThreePointModeProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, bool>(nameof(ThreePointMode));
    public static readonly StyledProperty<int> SelectedRingIndexProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, int>(nameof(SelectedRingIndex), -1);
    public static readonly StyledProperty<IReadOnlyList<CalibrationPoint>?> RingPointMarkersProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, IReadOnlyList<CalibrationPoint>?>(nameof(RingPointMarkers));
    public static readonly StyledProperty<ICommand?> RingPointCommandProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, ICommand?>(nameof(RingPointCommand));
    public static readonly StyledProperty<ICommand?> UndoRingPointCommandProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, ICommand?>(nameof(UndoRingPointCommand));
    public static readonly StyledProperty<ICommand?> RingEditedCommandProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, ICommand?>(nameof(RingEditedCommand));
    public static readonly StyledProperty<double> RingAngleDegreesProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, double>(nameof(RingAngleDegrees));
    public static readonly StyledProperty<double> PhysicalRadiusMmProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, double>(nameof(PhysicalRadiusMm), 60);
    public static readonly StyledProperty<bool> ShowPredictionProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, bool>(nameof(ShowPrediction), true);

    public static readonly StyledProperty<string> ViewDescriptionProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, string>(nameof(ViewDescription), "100%");
    public static readonly StyledProperty<string> SelectionDescriptionProperty =
        AvaloniaProperty.Register<CalibrationPhotoCanvas, string>(nameof(SelectionDescription), string.Empty);

    public Bitmap? Image { get => GetValue(ImageProperty); set => SetValue(ImageProperty, value); }
    public Bitmap? FallbackImage { get => GetValue(FallbackImageProperty); set => SetValue(FallbackImageProperty, value); }
    public bool IsPhotoStep { get => GetValue(IsPhotoStepProperty); set => SetValue(IsPhotoStepProperty, value); }
    public bool IsRectificationPreview { get => GetValue(IsRectificationPreviewProperty); set => SetValue(IsRectificationPreviewProperty, value); }
    public bool IsTargetPreview { get => GetValue(IsTargetPreviewProperty); set => SetValue(IsTargetPreviewProperty, value); }
    public double CanvasRadiusMm { get => GetValue(CanvasRadiusMmProperty); set => SetValue(CanvasRadiusMmProperty, value); }
    public double TargetCanvasRadiusMm { get => GetValue(TargetCanvasRadiusMmProperty); set => SetValue(TargetCanvasRadiusMmProperty, value); }
    public IEnumerable<CalibrationPhotoBoundaryPoint>? BoundaryPoints { get => GetValue(BoundaryPointsProperty); set => SetValue(BoundaryPointsProperty, value); }
    public IReadOnlyList<Point[]>? BoundaryContours { get => GetValue(BoundaryContoursProperty); set => SetValue(BoundaryContoursProperty, value); }
    public IEnumerable<CalibrationTracePoint>? TracePoints { get => GetValue(TracePointsProperty); set => SetValue(TracePointsProperty, value); }
    public IReadOnlyList<Point[]>? PredictionContours { get => GetValue(PredictionContoursProperty); set => SetValue(PredictionContoursProperty, value); }
    public IReadOnlyList<int>? OutlierTracePointIndices { get => GetValue(OutlierTracePointIndicesProperty); set => SetValue(OutlierTracePointIndicesProperty, value); }
    public int SelectedTraceArm { get => GetValue(SelectedTraceArmProperty); set => SetValue(SelectedTraceArmProperty, value); }
    public bool IsBoundarySegmentMode { get => GetValue(IsBoundarySegmentModeProperty); set => SetValue(IsBoundarySegmentModeProperty, value); }
    public ICommand? BoundaryEditedCommand { get => GetValue(BoundaryEditedCommandProperty); set => SetValue(BoundaryEditedCommandProperty, value); }
    public ICommand? TraceEditedCommand { get => GetValue(TraceEditedCommandProperty); set => SetValue(TraceEditedCommandProperty, value); }
    public bool IsCoarseMode { get => GetValue(IsCoarseModeProperty); set => SetValue(IsCoarseModeProperty, value); }
    public bool HasRadialArcTarget { get => GetValue(HasRadialArcTargetProperty); set => SetValue(HasRadialArcTargetProperty, value); }
    public bool HasWideBandTarget { get => GetValue(HasWideBandTargetProperty); set => SetValue(HasWideBandTargetProperty, value); }
    public bool IsBandRayMode { get => GetValue(IsBandRayModeProperty); set => SetValue(IsBandRayModeProperty, value); }
    public double? BandRayAngleDegrees { get => GetValue(BandRayAngleDegreesProperty); set => SetValue(BandRayAngleDegreesProperty, value); }
    public ICommand? BandRayPointCommand { get => GetValue(BandRayPointCommandProperty); set => SetValue(BandRayPointCommandProperty, value); }
    public IReadOnlyList<CalibrationPoint>? ArcLengthPoints { get => GetValue(ArcLengthPointsProperty); set => SetValue(ArcLengthPointsProperty, value); }
    public ICommand? ArcLengthPointCommand { get => GetValue(ArcLengthPointCommandProperty); set => SetValue(ArcLengthPointCommandProperty, value); }
    public ICommand? UndoArcLengthPointCommand { get => GetValue(UndoArcLengthPointCommandProperty); set => SetValue(UndoArcLengthPointCommandProperty, value); }
    public bool HasClockTarget { get => GetValue(HasClockTargetProperty); set => SetValue(HasClockTargetProperty, value); }
    public IReadOnlyList<double>? ClockReadAngles { get => GetValue(ClockReadAnglesProperty); set => SetValue(ClockReadAnglesProperty, value); }
    public double? PreferredClockAngle { get => GetValue(PreferredClockAngleProperty); set => SetValue(PreferredClockAngleProperty, value); }
    public ICommand? PreferClockAngleCommand { get => GetValue(PreferClockAngleCommandProperty); set => SetValue(PreferClockAngleCommandProperty, value); }
    public IEnumerable<CalibrationRingArc>? RingArcs { get => GetValue(RingArcsProperty); set => SetValue(RingArcsProperty, value); }
    public bool ThreePointMode { get => GetValue(ThreePointModeProperty); set => SetValue(ThreePointModeProperty, value); }
    public int SelectedRingIndex { get => GetValue(SelectedRingIndexProperty); set => SetValue(SelectedRingIndexProperty, value); }
    public IReadOnlyList<CalibrationPoint>? RingPointMarkers { get => GetValue(RingPointMarkersProperty); set => SetValue(RingPointMarkersProperty, value); }
    public ICommand? RingPointCommand { get => GetValue(RingPointCommandProperty); set => SetValue(RingPointCommandProperty, value); }
    public ICommand? UndoRingPointCommand { get => GetValue(UndoRingPointCommandProperty); set => SetValue(UndoRingPointCommandProperty, value); }
    public ICommand? RingEditedCommand { get => GetValue(RingEditedCommandProperty); set => SetValue(RingEditedCommandProperty, value); }
    public double RingAngleDegrees { get => GetValue(RingAngleDegreesProperty); set => SetValue(RingAngleDegreesProperty, value); }
    public double PhysicalRadiusMm { get => GetValue(PhysicalRadiusMmProperty); set => SetValue(PhysicalRadiusMmProperty, value); }
    public bool ShowPrediction { get => GetValue(ShowPredictionProperty); set => SetValue(ShowPredictionProperty, value); }

    public string ViewDescription { get => GetValue(ViewDescriptionProperty); private set => SetValue(ViewDescriptionProperty, value); }
    public string SelectionDescription { get => GetValue(SelectionDescriptionProperty); private set => SetValue(SelectionDescriptionProperty, value); }

    private static double Dist(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double DistSquared(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static double Len(Point p) => Math.Sqrt(p.X * p.X + p.Y * p.Y);

    private double _zoom = 1;
    private Vector _pan;
    private bool _panning;
    private Point _panStart;
    private Vector _panBefore;
    private CalibrationPhotoBoundaryPoint? _boundaryDrag;
    private CalibrationTracePoint? _traceDrag;
    private CalibrationRingArc? _ringDrag;
    private double _ringDragRadius;
    private double _ringDragStartRadius;
    private int? _selectedRing;
    private Point _dragStart;
    private Point _dragOriginal;
    private Point _dragPosition;
    private bool _hasMoved;
    private (int Arm, int Index)? _selectedTrace;
    private (int Arm, int Index)? _traceContinuation;
    private Point? _traceHover;

    private bool ShowingTarget => IsTargetPreview || Image is null;
    private bool ShowingRawPhoto => !ShowingTarget && IsPhotoStep && !IsRectificationPreview;
    private bool ShowingEditablePhoto => !ShowingTarget && !IsPhotoStep && !IsRectificationPreview;
    private bool CanEditTrace => ShowingEditablePhoto && !IsCoarseMode && !HasClockTarget && !HasRadialArcTarget && !HasWideBandTarget;
    private bool CanEditRings => ShowingEditablePhoto && IsCoarseMode && !HasClockTarget && !HasRadialArcTarget && !CanMarkBandRay;
    private bool CanMarkBandRay => ShowingEditablePhoto && IsCoarseMode && HasWideBandTarget && IsBandRayMode;
    private bool ShowingClockPhoto => ShowingEditablePhoto && IsCoarseMode && HasClockTarget;
    private bool CanMarkArcLength => ShowingEditablePhoto && IsCoarseMode && HasRadialArcTarget;
    private bool Dragging => _boundaryDrag is not null || _traceDrag is not null || _ringDrag is not null;
    private double RingAngle => RingAngleDegrees * Math.PI / 180;

    private Rect WorldBounds
    {
        get
        {
            if (ShowingRawPhoto && Image is { } photo)
            {
                double w = photo.PixelSize.Width, h = photo.PixelSize.Height;
                return new Rect(-.5, -.5, w, h);
            }

            double radius = ShowingTarget ? TargetCanvasRadiusMm : CanvasRadiusMm;
            radius = double.IsFinite(radius) && radius > 0 ? radius : 63;
            return new Rect(-radius, -radius, radius * 2, radius * 2);
        }
    }

    /// <summary>渲染时实际使用的变换。命中测试和拖动都读它，保证点下去的位置就是看到的位置。</summary>
    private Matrix _renderMatrix = Matrix.Identity;
    private bool _hasRenderMatrix;

    private Matrix ViewMatrix => _hasRenderMatrix ? _renderMatrix : ComputeViewMatrix();

    private Matrix ComputeViewMatrix()
    {
        {
            Rect bounds = WorldBounds;
            double width = Bounds.Width, height = Bounds.Height;
            double scale = Math.Max(.0001, Math.Min(width / bounds.Width, height / bounds.Height) * .92 * _zoom);
            return Matrix.CreateTranslation(-bounds.X - bounds.Width / 2, -bounds.Y - bounds.Height / 2)
                * Matrix.CreateScale(scale, scale)
                * Matrix.CreateTranslation(width / 2 + _pan.X, height / 2 + _pan.Y);
        }
    }

    public CalibrationPhotoCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    static CalibrationPhotoCanvas()
    {
        AffectsRender<CalibrationPhotoCanvas>(
            ImageProperty, FallbackImageProperty, IsPhotoStepProperty, IsRectificationPreviewProperty,
            IsTargetPreviewProperty, CanvasRadiusMmProperty, TargetCanvasRadiusMmProperty,
            BoundaryPointsProperty, BoundaryContoursProperty, TracePointsProperty, PredictionContoursProperty,
            OutlierTracePointIndicesProperty, SelectedTraceArmProperty, IsBoundarySegmentModeProperty,
            IsCoarseModeProperty, HasRadialArcTargetProperty, HasWideBandTargetProperty, IsBandRayModeProperty,
            BandRayAngleDegreesProperty, ArcLengthPointsProperty, HasClockTargetProperty, ClockReadAnglesProperty,
            PreferredClockAngleProperty, RingArcsProperty, ThreePointModeProperty, SelectedRingIndexProperty,
            RingPointMarkersProperty, RingAngleDegreesProperty, ShowPredictionProperty);
    }

    private Point WorldToView(Point world) => world.Transform(ViewMatrix);
    private Point ViewToWorld(Point view) => view.Transform(ViewMatrix.Invert());

    public void ZoomAt(double factor, Point screen)
    {
        Point world = ViewToWorld(screen);
        _zoom = Math.Clamp(_zoom * factor, .2, 12);
        Point after = WorldToView(world);
        _pan += screen - after;
        UpdateViewDescription();
        InvalidateVisual();
    }

    public void ResetView()
    {
        _zoom = 1;
        _pan = default;
        UpdateViewDescription();
        InvalidateVisual();
    }

    private void UpdateViewDescription() => ViewDescription = $"{_zoom * 100:0}%";

    public override void Render(DrawingContext context)
    {
        _renderMatrix = ComputeViewMatrix();
        _hasRenderMatrix = true;
        context.DrawRectangle(PhotoBackdrop, null, new Rect(Bounds.Size));
        Bitmap? bitmap = ShowingTarget ? FallbackImage : Image;
        Rect bounds = WorldBounds;
        Rect viewBounds = new(WorldToView(bounds.TopLeft), WorldToView(bounds.BottomRight));
        if (bitmap is not null) context.DrawImage(bitmap, viewBounds);
        else DrawPlaceholder(context, bounds);

        if (ShowingRawPhoto) DrawBoundaries(context);
        else if (CanMarkArcLength) DrawArcLengthPoints(context);
        else if (CanMarkBandRay) DrawBandRay(context);
        else if (ShowingClockPhoto)
        {
            double radius = PhysicalRadiusMm + .7;
            foreach (double angle in ClockReadAngles ?? [])
                DrawRingSegment(context, radius, (angle - 22.5) * Math.PI / 180, Math.PI / 4, new Pen(TraceBrush, 3));
            if (PreferredClockAngle is { } preferred)
                context.DrawEllipse(HandleBrush, new Pen(OutlineBrush, 1), OnRing(radius, preferred * Math.PI / 180), 3.5, 3.5);
        }
        else if (CanEditRings && ThreePointMode) DrawMeasuredPoints(context);
        else if (CanEditRings) DrawRings(context);
        else if (CanEditTrace)
        {
            if (ShowPrediction && PredictionContours is { } predictions)
                foreach (Point[] prediction in predictions) DrawPath(context, prediction, new Pen(PredictionBrush, 1.8), false);
            foreach (int arm in (TracePoints ?? []).Select(point => point.Arm).Distinct())
            {
                CalibrationTracePoint[] points = TracePoints?.Where(p => p.Arm == arm).ToArray() ?? [];
                IBrush brush = TraceBrush;
                var pen = new Pen(brush, IsBoundarySegmentMode && arm == SelectedTraceArm ? 2 : 1.2)
                {
                    DashStyle = new DashStyle([1, 3], 0),
                };
                DrawPath(context, points.Select(TraceDisplayPosition).ToArray(), pen, false);
                if (IsBoundarySegmentMode && points.Length > 0)
                    DrawText(context, L.Format("HalfDisc.SegmentBadge", arm + 1),
                        WorldToView(TraceDisplayPosition(points[0])) + new Vector(8, 8), brush, 11);
                foreach (CalibrationTracePoint point in points)
                {
                    bool selected = _selectedTrace == (point.Arm, point.Index);
                    bool outlier = OutlierTracePointIndices?.Contains(point.Index) == true;
                    IBrush pointBrush = outlier ? OutlierBrush : brush;
                    Point screen = WorldToView(TraceDisplayPosition(point));
                    context.DrawEllipse(pointBrush, new Pen(OutlineBrush, 1.4), screen, selected ? 6 : 4, selected ? 6 : 4);
                    if (IsBoundarySegmentMode && arm == SelectedTraceArm
                        && (point.Index == points[0].Index || point.Index == points[^1].Index))
                    {
                        bool fixedEnd = _traceContinuation == (point.Arm, point.Index);
                        context.DrawEllipse(null, new Pen(fixedEnd ? PredictionBrush : brush, fixedEnd ? 2.3 : 1.3),
                            screen, fixedEnd ? 10 : 7.5, fixedEnd ? 10 : 7.5);
                        if (fixedEnd) DrawText(context, L.T("HalfDisc.ContinueBadge"), screen + new Vector(10, 7), PredictionBrush, 11);
                    }
                    if (selected) DrawText(context, L.F($"点 {point.Index + 1}"), screen + new Vector(9, -16), pointBrush, 12);
                }
            }

            DrawTraceInsertionPreview(context);
        }

        if (ShowingTarget && bitmap is null) DrawCaption(context, L.T("CalibrationArt.Empty"));
        else if (ShowingTarget && HasClockTarget) DrawCaption(context, L.T("Clock.PreviewCaption"));
        else if (ShowingTarget) DrawCaption(context, Image is null ? L.T("标靶预览 · 导入照片后开始矫正") : L.T("刻录图案预览 · 关闭预览后继续照片标定"));
        else if (IsRectificationPreview) DrawCaption(context, L.T("Xaml.RectifiedCalibrationCaption"));
    }

    // ---- 命中与拖动 ----

    public bool BeginDragAt(Point screen)
    {
        if (CanEditRings && !ThreePointMode)
        {
            if (HitRing(screen) is { } ring)
            {
                _ringDrag = ring;
                _ringDragRadius = ring.RadiusMm;
                _ringDragStartRadius = Len(ViewToWorld(screen));
                _hasMoved = false;
                _selectedRing = ring.RingIndex;
                UpdateRingDescription(ring.RingIndex, ring.RadiusMm);
                InvalidateVisual();
                return true;
            }
        }

        if (ShowingRawPhoto && BoundaryPoints is not null)
        {
            CalibrationPhotoBoundaryPoint? hit = null;
            double best = 100;
            foreach (CalibrationPhotoBoundaryPoint point in BoundaryPoints)
            {
                double distance = DistSquared(WorldToView(BoundaryDisplayPosition(point)), screen);
                if (distance < best) { best = distance; hit = point; }
            }

            if (hit is not null && best <= 12 * 12)
            {
                _boundaryDrag = hit;
                _dragStart = screen;
                _dragOriginal = BoundaryDisplayPosition(hit);
                _dragPosition = _dragOriginal;
                _hasMoved = false;
                InvalidateVisual();
                return true;
            }
        }

        if (CanEditTrace && TracePoints is not null)
        {
            CalibrationTracePoint? hit = null;
            double best = 100;
            foreach (CalibrationTracePoint point in TracePoints)
            {
                double distance = DistSquared(WorldToView(TraceDisplayPosition(point)), screen);
                if (distance < best) { best = distance; hit = point; }
            }

            if (hit is not null && best <= 10 * 10)
            {
                _traceDrag = hit;
                _dragStart = screen;
                _dragOriginal = TraceDisplayPosition(hit);
                _dragPosition = _dragOriginal;
                _hasMoved = false;
                _selectedTrace = (hit.Arm, hit.Index);
                UpdateTraceDescription();
                InvalidateVisual();
                return true;
            }
        }

        return false;
    }

    public void DragTo(Point screen)
    {
        if (!Dragging) return;
        if (_ringDrag is { } ring)
        {
            double delta = Len(ViewToWorld(screen)) - _ringDragStartRadius;
            _ringDragRadius = Math.Clamp(ring.RadiusMm + delta, 7.5, PhysicalRadiusMm);
            UpdateRingDescription(ring.RingIndex, _ringDragRadius);
            InvalidateVisual();
            return;
        }

        _dragPosition = _dragOriginal + (ViewToWorld(screen) - ViewToWorld(_dragStart));
        Rect bounds = ShowingRawPhoto && Image is { } photo
            ? new Rect(0, 0, photo.PixelSize.Width - 1, photo.PixelSize.Height - 1) : WorldBounds;
        _dragPosition = new Point(
            Math.Clamp(_dragPosition.X, bounds.Left, bounds.Right),
            Math.Clamp(_dragPosition.Y, bounds.Top, bounds.Bottom));
        _hasMoved |= Dist(WorldToView(_dragPosition), WorldToView(_dragOriginal)) > 2;
        InvalidateVisual();
    }

    public void CompleteDrag(bool commit = true)
    {
        CalibrationPhotoBoundaryPoint? boundary = _boundaryDrag;
        CalibrationTracePoint? trace = _traceDrag;
        CalibrationRingArc? ring = _ringDrag;
        Point position = _dragPosition;
        bool changed = _hasMoved && commit;
        _boundaryDrag = null;
        _traceDrag = null;
        _ringDrag = null;
        _hasMoved = false;
        if (changed && boundary is not null)
            Execute(BoundaryEditedCommand, new CalibrationBoundaryEdit(boundary.Boundary, boundary.Index, position.X, position.Y));
        if (changed && trace is not null)
        {
            Execute(TraceEditedCommand, new CalibrationTraceEdit(trace.Arm, trace.Index, position.X, position.Y));
            _selectedTrace = (trace.Arm, trace.Index);
            UpdateTraceDescription();
        }
        else if (commit && trace is not null && IsBoundarySegmentMode && IsTraceEndpoint(trace.Arm, trace.Index))
        {
            _traceContinuation = (trace.Arm, trace.Index);
            UpdateTraceDescription();
        }

        if (ring is not null)
        {
            if (commit && (!ring.IsConfirmed || ring.RadiusMm != _ringDragRadius))
                Execute(RingEditedCommand, new CalibrationRingEdit(ring.RingIndex, _ringDragRadius));
            UpdateRingDescription(ring.RingIndex, commit ? _ringDragRadius : ring.RadiusMm);
        }

        InvalidateVisual();
    }

    public bool AddTraceAt(Point screen)
    {
        if (!CanEditTrace || Dragging) return false;
        Point point = ViewToWorld(screen);
        if (!WorldBounds.Contains(point) || SelectedTraceArm < 0 || (!IsBoundarySegmentMode && SelectedTraceArm > 3)) return false;
        var edit = new CalibrationTraceEdit(SelectedTraceArm, -1, point.X, point.Y);
        if (TraceEditedCommand?.CanExecute(edit) != true) return false;
        TraceEditedCommand.Execute(edit);
        _selectedTrace = null;
        UpdateTraceDescription();
        InvalidateVisual();
        return true;
    }

    private bool IsTraceEndpoint(int arm, int index)
    {
        CalibrationTracePoint[] points = (TracePoints ?? []).Where(p => p.Arm == arm).ToArray();
        return points.Length > 0 && (points[0].Index == index || points[^1].Index == index);
    }

    public void RemoveSelectedTracePoint()
    {
        if (!CanEditTrace || Dragging || _selectedTrace is not { } selected) return;
        CalibrationTracePoint? point = TracePoints?.FirstOrDefault(p => p.Arm == selected.Arm && p.Index == selected.Index);
        if (point is null) return;
        var edit = new CalibrationTraceEdit(point.Arm, point.Index, point.XMm, point.YMm, true);
        if (TraceEditedCommand?.CanExecute(edit) != true) return;
        TraceEditedCommand.Execute(edit);
        _selectedTrace = null;
        SelectionDescription = L.T("已删除此点；点击照片继续描当前曲线。");
        InvalidateVisual();
    }

    public void RemoveSelectedRing()
    {
        if (!CanEditRings || ThreePointMode || Dragging || _selectedRing is not { } index) return;
        var edit = new CalibrationRingEdit(index, null);
        if (RingEditedCommand?.CanExecute(edit) != true) return;
        RingEditedCommand.Execute(edit);
        _selectedRing = null;
        SelectionDescription = L.T(HasWideBandTarget ? "BandEdges.DragHint" : "Xaml.CoarseRingStatusHint");
        InvalidateVisual();
    }

    public bool AddRingPointAt(Point screen)
    {
        if ((!CanMarkBandRay && !(CanEditRings && ThreePointMode)) || Dragging || SelectedRingIndex < 0) return false;
        Point world = ViewToWorld(screen);
        double radius = Len(world);
        if (!double.IsFinite(radius) || radius < 7.5 || radius > PhysicalRadiusMm) return false;
        var point = new CalibrationPoint(world.X, world.Y);
        ICommand? command = CanMarkBandRay ? BandRayPointCommand : RingPointCommand;
        if (command?.CanExecute(point) != true) return false;
        command.Execute(point);
        return true;
    }

    public bool AddArcLengthPointAt(Point screen)
    {
        if (!CanMarkArcLength || Dragging) return false;
        Point world = ViewToWorld(screen);
        double radius = Len(world);
        if (!double.IsFinite(radius) || radius < 7.5 || radius > PhysicalRadiusMm) return false;
        var point = new CalibrationPoint(world.X, world.Y);
        if (ArcLengthPointCommand?.CanExecute(point) != true) return false;
        ArcLengthPointCommand.Execute(point);
        return true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (!_hasRenderMatrix)
        {
            _renderMatrix = ComputeViewMatrix();
            _hasRenderMatrix = true;
        }

        PointerPoint point = e.GetCurrentPoint(this);
        Point position = point.Position;
        bool space = false;
        if (point.Properties.IsRightButtonPressed || point.Properties.IsMiddleButtonPressed
            || (point.Properties.IsLeftButtonPressed && space))
        {
            _panning = true;
            _panStart = position;
            _panBefore = _pan;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed) return;
        if (CanMarkArcLength) AddArcLengthPointAt(position);
        else if (ShowingClockPhoto)
        {
            Point world = ViewToWorld(position);
            double radius = Len(world);
            if (radius >= 7.5 && radius <= PhysicalRadiusMm)
                Execute(PreferClockAngleCommand, (Math.Atan2(world.Y, world.X) * 180 / Math.PI + 360) % 360);
        }
        else if (CanMarkBandRay || (CanEditRings && ThreePointMode)) AddRingPointAt(position);
        else if (BeginDragAt(position)) e.Pointer.Capture(this);
        else AddTraceAt(position);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point position = e.GetPosition(this);
        _traceHover = position;
        if (_panning)
        {
            _pan = _panBefore + (position - _panStart);
            InvalidateVisual();
        }
        else if (Dragging) DragTo(position);
        else if (IsBoundarySegmentMode && CanEditTrace) InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_panning) { _panning = false; e.Pointer.Capture(null); }
        else if (Dragging) { CompleteDrag(); e.Pointer.Capture(null); }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _traceHover = null;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ZoomAt(e.Delta.Y > 0 ? 1.2 : 1 / 1.2, e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (CanMarkArcLength && e.Key is Key.Back or Key.Delete)
        {
            if (UndoArcLengthPointCommand?.CanExecute(null) == true) UndoArcLengthPointCommand.Execute(null);
            e.Handled = true;
        }
        else if (CanEditRings && ThreePointMode && e.Key is Key.Back or Key.Delete)
        {
            if (UndoRingPointCommand?.CanExecute(null) == true) UndoRingPointCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            if (CanEditRings) RemoveSelectedRing();
            else RemoveSelectedTracePoint();
            e.Handled = true;
        }

        if (e.Key == Key.Escape)
        {
            CompleteDrag(false);
            _traceContinuation = null;
            UpdateTraceDescription();
            InvalidateVisual();
            e.Handled = true;
        }
    }

    // ---- 绘制 ----

    private void DrawTraceInsertionPreview(DrawingContext context)
    {
        if (!IsBoundarySegmentMode || Dragging || _panning || _traceHover is not { } screen) return;
        Point world = ViewToWorld(screen);
        double radius = Len(world);
        if (!WorldBounds.Contains(world) || radius < 7.5 || radius > PhysicalRadiusMm
            || (TracePoints ?? []).Any(p => Dist(WorldToView(Position(p)), screen) <= 10)) return;
        context.DrawEllipse(null, new Pen(TraceBrush, 1.5), screen, 4.5, 4.5);
    }

    private Point OnRing(double radius, double angle) =>
        WorldToView(new Point(radius * Math.Cos(angle), radius * Math.Sin(angle)));

    private Point RingBadge(CalibrationRingArc arc, double radius) =>
        OnRing(radius, RingAngle + (arc.RingIndex % 2 == 0 ? .22 : -.22));

    private CalibrationRingArc? HitRing(Point screen)
    {
        if (RingArcs is not { } arcs) return null;
        List<CalibrationRingArc> list = arcs.ToList();
        if (list.Count == 0) return null;
        CalibrationRingArc badge = list.MinBy(arc => DistSquared(RingBadge(arc, arc.RadiusMm), screen))!;
        if (Dist(RingBadge(badge, badge.RadiusMm), screen) <= 8) return badge;
        Point world = ViewToWorld(screen);
        double angle = Math.Atan2(world.Y, world.X);
        if (Math.Abs(Math.IEEERemainder(angle - RingAngle, Math.PI)) > ArcHalfAngle) return null;
        CalibrationRingArc closest = list.MinBy(arc => DistSquared(OnRing(arc.RadiusMm, angle), screen))!;
        return Dist(OnRing(closest.RadiusMm, angle), screen) <= 8 ? closest : null;
    }

    private void DrawMeasuredPoints(DrawingContext context)
    {
        if (RingPointMarkers is not { Count: > 0 } points) return;
        if (points.Count == 3)
        {
            // 三个点自己决定圆弧：画它们的外接圆，而不是按平均半径估一圈。
            var a = new Point(points[0].X, points[0].Y);
            var b = new Point(points[1].X, points[1].Y);
            var c = new Point(points[2].X, points[2].Y);
            if (CircleThrough(a, b, c) is { } circle)
            {
                Point sa = WorldToView(a), sb = WorldToView(b), sc = WorldToView(c);
                double pixels = Dist(WorldToView(new Point(circle.Center.X + circle.Radius, circle.Center.Y)),
                    WorldToView(circle.Center));
                // 屏幕 Y 向下：叉积为正说明 a→b→c 是顺时针。
                double cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
                SweepDirection sweep = cross > 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise;
                if (pixels is > 0.5 and < 100000)
                {
                    var geometry = new StreamGeometry();
                    using (StreamGeometryContext path = geometry.Open())
                    {
                        path.BeginFigure(sa, false);
                        path.ArcTo(sb, new Size(pixels, pixels), 0, false, sweep);
                        path.ArcTo(sc, new Size(pixels, pixels), 0, false, sweep);
                    }

                    context.DrawGeometry(null, new Pen(TraceBrush, 1.6), geometry);
                }
            }
        }

        for (int i = 0; i < points.Count; i++)
        {
            Point position = WorldToView(new Point(points[i].X, points[i].Y));
            context.DrawEllipse(HubOutlineBrush, new Pen(OutlineBrush, 1), position, 3.5, 3.5);
            FormattedText label = Text((i + 1).ToString(CultureInfo.CurrentCulture), 11, HandleBrush);
            Point at = position + new Vector(5, -label.Height);
            context.DrawRectangle(CaptionBrush, null, new Rect(at.X - 2, at.Y, label.Width + 4, label.Height), 2, 2);
            context.DrawText(label, at);
        }
    }

    private void DrawArcLengthPoints(DrawingContext context)
    {
        if (ArcLengthPoints is not { Count: > 0 } points) return;
        if (CalibrationArcLengthMark.TrySpan(points, out double signedDegrees))
        {
            double first = Math.Atan2(points[0].Y, points[0].X);
            double last = Math.Atan2(points[^1].Y, points[^1].X);
            double start = signedDegrees >= 0 ? first : last;
            double sweep = Math.Abs(signedDegrees) * Math.PI / 180;
            double radius = points.Average(p => Math.Sqrt(p.X * p.X + p.Y * p.Y));
            DrawRingSegment(context, radius, start, sweep, new Pen(TraceBrush, 1.6));
        }

        for (int i = 0; i < points.Count; i++)
        {
            Point position = WorldToView(new Point(points[i].X, points[i].Y));
            context.DrawEllipse(HubOutlineBrush, new Pen(OutlineBrush, 1), position, 3.5, 3.5);
            FormattedText label = Text((i + 1).ToString(CultureInfo.CurrentCulture), 11, HandleBrush);
            Point at = position + new Vector(5, -label.Height);
            context.DrawRectangle(CaptionBrush, null, new Rect(at.X - 2, at.Y, label.Width + 4, label.Height), 2, 2);
            context.DrawText(label, at);
        }
    }

    private void DrawRingSegment(DrawingContext context, double radius, double start, double sweep, Pen pen)
    {
        if (Math.Abs(sweep) < 1e-6) return;
        double pixels = Dist(OnRing(radius, 0), WorldToView(new Point()));
        var geometry = new StreamGeometry();
        using (StreamGeometryContext path = geometry.Open())
        {
            path.BeginFigure(OnRing(radius, start), false);
            path.ArcTo(OnRing(radius, start + sweep), new Size(pixels, pixels), 0,
                Math.Abs(sweep) > Math.PI, SweepDirection.Clockwise);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private void DrawRings(DrawingContext context)
    {
        if (RingArcs is null) return;
        foreach (CalibrationRingArc arc in RingArcs)
        {
            double r = _ringDrag?.RingIndex == arc.RingIndex ? _ringDragRadius : arc.RadiusMm;
            bool selected = _selectedRing == arc.RingIndex;
            IBrush brush = selected ? HubOutlineBrush : arc.IsConfirmed ? TraceBrush : EdgeBrush;
            var pen = new Pen(brush, selected ? 2.2 : 1.4);
            if (!arc.IsConfirmed) pen.DashStyle = new DashStyle([4, 3], 0);
            DrawRingSegment(context, r, RingAngle - ArcHalfAngle, 2 * ArcHalfAngle, pen);
            DrawRingSegment(context, r, RingAngle + Math.PI - ArcHalfAngle, 2 * ArcHalfAngle, pen);
            Point badge = RingBadge(arc, r);
            FormattedText label = Text(HasWideBandTarget ? arc.ShortEdgeLabel
                : (arc.RingIndex + 1).ToString(CultureInfo.CurrentCulture), 10, brush);
            if (HasWideBandTarget)
                context.DrawRectangle(CaptionBrush, new Pen(brush, 1),
                    new Rect(badge.X - label.Width / 2 - 3, badge.Y - label.Height / 2 - 1, label.Width + 6, label.Height + 2), 3, 3);
            else context.DrawEllipse(CaptionBrush, new Pen(brush, 1), badge, 7.5, 7.5);
            context.DrawText(label, badge - new Vector(label.Width / 2, label.Height / 2));
        }
    }

    private void DrawBandRay(DrawingContext context)
    {
        if (BandRayAngleDegrees is not double degrees || RingArcs is null) return;
        double angle = degrees * Math.PI / 180;
        context.DrawLine(new Pen(EdgeBrush, 1) { DashStyle = new DashStyle([4, 3], 0) },
            OnRing(7.5, angle), OnRing(PhysicalRadiusMm, angle));
        var normal = new Vector(-Math.Sin(angle), Math.Cos(angle));
        foreach (CalibrationRingArc edge in RingArcs)
        {
            if (edge.Points is not { Count: 1 } points) continue;
            Point mark = WorldToView(new Point(points[0].X, points[0].Y));
            context.DrawEllipse(HandleBrush, new Pen(OutlineBrush, 1), mark, 3.5, 3.5);
            FormattedText label = Text(edge.ShortEdgeLabel, 10, TraceBrush);
            double offset = 5 + (Math.Abs(normal.X) * label.Width + Math.Abs(normal.Y) * label.Height) / 2;
            Point at = mark + normal * (edge.RingIndex < 3 ? -offset : offset) - new Vector(label.Width / 2, label.Height / 2);
            context.DrawRectangle(CaptionBrush, null, new Rect(at.X - 2, at.Y, label.Width + 4, label.Height), 2, 2);
            context.DrawText(label, at);
        }
    }

    private void UpdateRingDescription(int index, double radius) => SelectionDescription = HasWideBandTarget
        ? L.Format("BandEdges.Selected", CalibrationRingArc.WideEdgeLabel(index), radius)
        : L.Format("Xaml.CoarseRingSelected", index + 1, radius);

    private void DrawBoundaries(DrawingContext context)
    {
        foreach (CalibrationPhotoBoundaryKind kind in new[] { CalibrationPhotoBoundaryKind.OuterEdge, CalibrationPhotoBoundaryKind.CenterHole })
        {
            IBrush brush = kind == CalibrationPhotoBoundaryKind.OuterEdge ? EdgeBrush : HubOutlineBrush;
            CalibrationPhotoBoundaryPoint[] points = BoundaryPoints?.Where(p => p.Boundary == kind).OrderBy(p => p.Index).ToArray() ?? [];
            Point[]? outline = BoundaryContours?.ElementAtOrDefault((int)kind);
            // 控制点之间按平滑曲线连：盘缘和孔缘是圆弧，画成折线会误导轮廓判断。
            if (_boundaryDrag?.Boundary == kind || outline is not { Length: > 2 })
                DrawSmoothPath(context, points.Select(BoundaryDisplayPosition).ToArray(),
                    new Pen(brush, 1.3) { DashStyle = new DashStyle([4, 3], 0) });
            else DrawSmoothPath(context, outline, new Pen(brush, 1.6));
            foreach (CalibrationPhotoBoundaryPoint point in points)
            {
                bool selected = _boundaryDrag?.Boundary == point.Boundary && _boundaryDrag.Index == point.Index;
                Point screen = WorldToView(BoundaryDisplayPosition(point));
                context.DrawEllipse(brush, new Pen(OutlineBrush, 1.2), screen, selected ? 6 : 4.5, selected ? 6 : 4.5);
            }
        }
    }

    private void DrawPlaceholder(DrawingContext context, Rect bounds)
    {
        Point center = WorldToView(new Point());
        double radius = Math.Min(bounds.Width, bounds.Height) * .44;
        var pen = new Pen(HandleBrush, 1.5);
        foreach (double r in new[] { radius, radius / 8 })
        {
            double viewRadius = Dist(WorldToView(new Point(r, 0)), center);
            context.DrawEllipse(null, pen, center, viewRadius, viewRadius);
        }
    }

    /// <summary>三点外接圆；三点共线时返回 null。</summary>
    private static (Point Center, double Radius)? CircleThrough(Point a, Point b, Point c)
    {
        double d = 2 * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
        if (Math.Abs(d) < 1e-9) return null;
        double a2 = a.X * a.X + a.Y * a.Y, b2 = b.X * b.X + b.Y * b.Y, c2 = c.X * c.X + c.Y * c.Y;
        double ux = (a2 * (b.Y - c.Y) + b2 * (c.Y - a.Y) + c2 * (a.Y - b.Y)) / d;
        double uy = (a2 * (c.X - b.X) + b2 * (a.X - c.X) + c2 * (b.X - a.X)) / d;
        var center = new Point(ux, uy);
        return (center, Dist(center, a));
    }

    /// <summary>闭合平滑曲线：用相邻点中点作为端点、原控制点作为二次贝塞尔控制点。</summary>
    private void DrawSmoothPath(DrawingContext context, IReadOnlyList<Point> points, Pen pen)
    {
        if (points.Count < 3) return;
        Point[] screen = points.Select(WorldToView).ToArray();
        if (screen.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y))) return;
        static Point Mid(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        var geometry = new StreamGeometry();
        using (StreamGeometryContext path = geometry.Open())
        {
            path.BeginFigure(Mid(screen[0], screen[^1]), false);
            for (int i = 0; i < screen.Length; i++)
            {
                path.QuadraticBezierTo(screen[i], Mid(screen[i], screen[(i + 1) % screen.Length]));
            }

            path.EndFigure(true);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private void DrawPath(DrawingContext context, IReadOnlyList<Point> points, Pen pen, bool closed)
    {
        if (points.Count < 2) return;
        var geometry = new StreamGeometry();
        using (StreamGeometryContext path = geometry.Open())
        {
            bool started = false;
            foreach (Point point in points)
            {
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) { started = false; continue; }
                Point screen = WorldToView(point);
                if (!started) { path.BeginFigure(screen, false); started = true; }
                else path.LineTo(screen);
            }

            if (started && closed) path.EndFigure(true);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private void DrawCaption(DrawingContext context, string text)
    {
        FormattedText label = Text(text, 11, HandleBrush);
        double y = Math.Max(4, Bounds.Height - label.Height - 12);
        var bounds = new Rect(8, y, label.Width + 12, label.Height + 6);
        context.DrawRectangle(CaptionBrush, null, bounds, 4, 4);
        context.DrawText(label, new Point(bounds.Left + 6, bounds.Top + 3));
    }

    private void DrawText(DrawingContext context, string text, Point at, IBrush brush, double size)
        => context.DrawText(Text(text, size, brush), at);

    private static FormattedText Text(string text, double size, IBrush brush) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface, size, brush);

    private Point BoundaryDisplayPosition(CalibrationPhotoBoundaryPoint point)
        => _boundaryDrag?.Boundary == point.Boundary && _boundaryDrag.Index == point.Index ? _dragPosition : new(point.X, point.Y);

    private Point TraceDisplayPosition(CalibrationTracePoint point)
        => _traceDrag?.Arm == point.Arm && _traceDrag.Index == point.Index ? _dragPosition : Position(point);

    private static Point Position(CalibrationTracePoint point) => new(point.XMm, point.YMm);

    private static void Execute(ICommand? command, object edit)
    {
        if (command?.CanExecute(edit) == true) command.Execute(edit);
    }

    private void UpdateTraceDescription() => SelectionDescription = IsBoundarySegmentMode
        ? L.Format(_traceContinuation is not null ? "HalfDisc.EndpointFixed" : "HalfDisc.CanvasHint", SelectedTraceArm + 1)
        : _selectedTrace is { } point ? L.F($"已选曲线上的点 {point.Index + 1} · 直接拖动修正，Delete 删除")
        : L.T("点击照片上同一条可见曲线，由内向外连续描点。");

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedTraceArmProperty)
        {
            _selectedTrace = null;
            _traceContinuation = null;
            if (CanEditTrace) UpdateTraceDescription();
        }
        else if (change.Property == TracePointsProperty)
        {
            _selectedTrace = null;
            _traceContinuation = null;
            UpdateTraceDescription();
        }
        else if (change.Property == BoundaryPointsProperty || change.Property == RingArcsProperty
            || change.Property == TracePointsProperty)
        {
            // 数据更新只作废旧的选中项，视图保持用户当前的缩放和平移。
            _selectedTrace = null;
            _traceContinuation = null;
            _traceHover = null;
        }
        else if (change.Property == IsPhotoStepProperty || change.Property == IsRectificationPreviewProperty
            || change.Property == IsTargetPreviewProperty || change.Property == CanvasRadiusMmProperty
            || change.Property == HasRadialArcTargetProperty || change.Property == HasWideBandTargetProperty
            || change.Property == HasClockTargetProperty)
        {
            // 换了一张图/换了一个标靶才有必要回到适应窗口。
            CompleteDrag(false);
            _selectedTrace = null;
            _traceContinuation = null;
            _traceHover = null;
            ResetView();
        }
    }
}
