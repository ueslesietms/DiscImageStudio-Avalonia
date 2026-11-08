using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscImageStudio.Core;
using DiscImageStudio.Localization;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Desktop.ViewModels;

/// <summary>标定页（简化版）：从一张正拍的盘片照片量出数据区内/外半径。
/// 参照物是盘外缘（120 mm 盘的半径 60 mm），所以在照片里标三组点：
/// 盘外缘、数据区内缘、数据区外缘。圆心取盘外缘拟合结果。</summary>
public partial class CalibrationPageViewModel : ObservableObject
{
    public enum EdgeKind
    {
        Disc,
        Inner,
        Outer,
    }

    public const double StandardDiscRadiusMm = 60.0;

    private readonly ShellViewModel _shell;

    public CalibrationPageViewModel(ShellViewModel shell)
    {
        _shell = shell;
        DiscPoints.CollectionChanged += (_, _) => OnPointsChanged();
        InnerPoints.CollectionChanged += (_, _) => OnPointsChanged();
        OuterPoints.CollectionChanged += (_, _) => OnPointsChanged();
    }

    public DiscParametersState State => _shell.State;

    public string FamilyLabel => _shell.CurrentProject?.FamilyLabel ?? "CD";

    public ObservableCollection<Point> DiscPoints { get; } = [];

    public ObservableCollection<Point> InnerPoints { get; } = [];

    public ObservableCollection<Point> OuterPoints { get; } = [];

    [ObservableProperty]
    private Bitmap? _photo;

    [ObservableProperty]
    private EdgeKind _activeEdge = EdgeKind.Disc;

    [ObservableProperty]
    private string _statusMessage = "先在照片上沿盘外缘点三个以上点。";

    [ObservableProperty]
    private string _resultSummary = string.Empty;

    [ObservableProperty]
    private bool _hasResult;

    public bool HasPhoto => Photo is not null;

    partial void OnPhotoChanged(Bitmap? value) => OnPropertyChanged(nameof(HasPhoto));

    partial void OnActiveEdgeChanged(EdgeKind value)
    {
        OnPropertyChanged(nameof(ActiveEdgeName));
        OnPropertyChanged(nameof(ActivePoints));
        OnPropertyChanged(nameof(IsDiscEdge));
        OnPropertyChanged(nameof(IsInnerEdge));
        OnPropertyChanged(nameof(IsOuterEdge));
    }

    public bool IsDiscEdge
    {
        get => ActiveEdge == EdgeKind.Disc;
        set { if (value) ActiveEdge = EdgeKind.Disc; }
    }

    public bool IsInnerEdge
    {
        get => ActiveEdge == EdgeKind.Inner;
        set { if (value) ActiveEdge = EdgeKind.Inner; }
    }

    public bool IsOuterEdge
    {
        get => ActiveEdge == EdgeKind.Outer;
        set { if (value) ActiveEdge = EdgeKind.Outer; }
    }

    public string ActiveEdgeName => ActiveEdge switch
    {
        EdgeKind.Inner => "数据区内缘",
        EdgeKind.Outer => "数据区外缘",
        _ => "盘外缘",
    };

    public ObservableCollection<Point> ActivePoints => ActiveEdge switch
    {
        EdgeKind.Inner => InnerPoints,
        EdgeKind.Outer => OuterPoints,
        _ => DiscPoints,
    };

    /// <summary>画布点击后调用；坐标是图片像素。</summary>
    public void AddPoint(Point imagePoint)
    {
        ActivePoints.Add(imagePoint);
        UpdateStatus();
    }

    [RelayCommand]
    private void SelectEdge(string? kind)
    {
        ActiveEdge = kind switch
        {
            "inner" => EdgeKind.Inner,
            "outer" => EdgeKind.Outer,
            _ => EdgeKind.Disc,
        };
        OnPropertyChanged(nameof(ActivePoints));
        UpdateStatus();
    }

    [RelayCommand]
    private async Task LoadPhotoAsync()
    {
        if (_shell.Dialogs is not { } dialogs)
        {
            return;
        }

        string? path = await dialogs.PickOpenFileAsync(
            L.T("选择盘片照片"), L.T("图片|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|所有文件|*.*"));
        if (path is null)
        {
            return;
        }

        try
        {
            Bitmap bitmap = new(path);
            Bitmap? previous = Photo;
            Photo = bitmap;
            previous?.Dispose();
            DiscPoints.Clear();
            InnerPoints.Clear();
            OuterPoints.Clear();
            HasResult = false;
            ResultSummary = string.Empty;
            UpdateStatus();
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    [RelayCommand]
    private void Undo()
    {
        if (ActivePoints.Count > 0)
        {
            ActivePoints.RemoveAt(ActivePoints.Count - 1);
        }

        UpdateStatus();
    }

    [RelayCommand]
    private void ClearActive()
    {
        ActivePoints.Clear();
        UpdateStatus();
    }

    /// <summary>拟合三个圆并算出数据区半径。返回 false 时 StatusMessage 说明缺什么。</summary>
    [RelayCommand]
    private void Measure()
    {
        if (DiscPoints.Count < 3)
        {
            StatusMessage = "盘外缘至少要点三个点。";
            return;
        }

        if (InnerPoints.Count < 3 || OuterPoints.Count < 3)
        {
            StatusMessage = "数据区内缘和外缘各至少三个点。";
            return;
        }

        if (FitCircle(DiscPoints) is not { } disc)
        {
            StatusMessage = "盘外缘的三个点几乎在一条直线上，换个位置补点。";
            return;
        }

        double innerPx = AverageRadius(InnerPoints, disc.Center);
        double outerPx = AverageRadius(OuterPoints, disc.Center);
        if (innerPx <= 0 || outerPx <= innerPx)
        {
            StatusMessage = "内缘半径比外缘大，检查是不是点反了。";
            return;
        }

        double scale = StandardDiscRadiusMm / disc.Radius;
        double innerMm = innerPx * scale;
        double outerMm = outerPx * scale;
        MeasuredInnerRadiusMm = innerMm;
        MeasuredOuterRadiusMm = outerMm;
        ResultSummary = string.Create(CultureInfo.InvariantCulture,
            $"内半径 {innerMm:0.###} mm · 外半径 {outerMm:0.###} mm（盘缘 {disc.Radius:0.#} px 对应 60 mm）");
        HasResult = true;
        StatusMessage = "量好了，点“应用到工程”写进当前参数。";
    }

    public double MeasuredInnerRadiusMm { get; private set; }

    public double MeasuredOuterRadiusMm { get; private set; }

    [RelayCommand]
    private void Apply()
    {
        if (!HasResult)
        {
            return;
        }

        string inner = MeasuredInnerRadiusMm.ToString("R", CultureInfo.InvariantCulture);
        string outer = MeasuredOuterRadiusMm.ToString("R", CultureInfo.InvariantCulture);
        switch (_shell.CurrentProject?.Family)
        {
            case OpticalDiscFamily.Dvd:
                State.DvdInnerRadius = inner;
                State.DvdOuterRadius = outer;
                break;
            case OpticalDiscFamily.BluRay:
                State.BdInnerRadius = inner;
                State.BdOuterRadius = outer;
                break;
            default:
                State.CdInnerRadius = inner;
                State.CdOuterRadius = outer;
                break;
        }

        _shell.Design.RefreshGeometry();
        StatusMessage = "已写进当前盘型的内/外半径。";
    }

    private void OnPointsChanged()
    {
        OnPropertyChanged(nameof(CountsText));
        UpdateStatus();
    }

    public string CountsText =>
        $"盘缘 {DiscPoints.Count} · 内缘 {InnerPoints.Count} · 外缘 {OuterPoints.Count}";

    private void UpdateStatus()
    {
        if (Photo is null)
        {
            StatusMessage = "先导入一张盘片照片。";
            return;
        }

        StatusMessage = ActiveEdge switch
        {
            EdgeKind.Disc when DiscPoints.Count < 3 => $"沿盘外缘点（还差 {3 - DiscPoints.Count} 个）",
            EdgeKind.Inner when InnerPoints.Count < 3 => $"沿数据区内缘点（还差 {3 - InnerPoints.Count} 个）",
            EdgeKind.Outer when OuterPoints.Count < 3 => $"沿数据区外缘点（还差 {3 - OuterPoints.Count} 个）",
            _ => "点“测量”拟合半径。",
        };
    }

    private static double AverageRadius(IReadOnlyList<Point> points, Point center)
    {
        double sum = 0;
        foreach (Point point in points)
        {
            double dx = point.X - center.X;
            double dy = point.Y - center.Y;
            sum += Math.Sqrt(dx * dx + dy * dy);
        }

        return sum / points.Count;
    }

    /// <summary>Kasa 代数圆拟合：解 x²+y² + D x + E y + F = 0。</summary>
    public static (Point Center, double Radius)? FitCircle(IReadOnlyList<Point> points)
    {
        if (points.Count < 3)
        {
            return null;
        }

        double sumX = 0, sumY = 0, sumX2 = 0, sumY2 = 0, sumXY = 0;
        double sumX3 = 0, sumY3 = 0, sumXY2 = 0, sumX2Y = 0;
        foreach (Point p in points)
        {
            double x2 = p.X * p.X;
            double y2 = p.Y * p.Y;
            sumX += p.X;
            sumY += p.Y;
            sumX2 += x2;
            sumY2 += y2;
            sumXY += p.X * p.Y;
            sumX3 += x2 * p.X;
            sumY3 += y2 * p.Y;
            sumXY2 += p.X * y2;
            sumX2Y += x2 * p.Y;
        }

        double n = points.Count;
        // 正规方程：n·D + Σx·E + Σy·F = -Σ(x²+y²) 等
        double[,] a =
        {
            { sumX2, sumXY, sumX },
            { sumXY, sumY2, sumY },
            { sumX, sumY, n },
        };
        double[] b =
        {
            -(sumX3 + sumXY2),
            -(sumX2Y + sumY3),
            -(sumX2 + sumY2),
        };

        if (!Solve3(a, b, out double d, out double e, out double f))
        {
            return null;
        }

        double cx = -d / 2;
        double cy = -e / 2;
        double radiusSquared = cx * cx + cy * cy - f;
        if (radiusSquared <= 0)
        {
            return null;
        }

        return (new Point(cx, cy), Math.Sqrt(radiusSquared));
    }

    private static bool Solve3(double[,] a, double[] b, out double x, out double y, out double z)
    {
        double det = a[0, 0] * (a[1, 1] * a[2, 2] - a[1, 2] * a[2, 1])
            - a[0, 1] * (a[1, 0] * a[2, 2] - a[1, 2] * a[2, 0])
            + a[0, 2] * (a[1, 0] * a[2, 1] - a[1, 1] * a[2, 0]);
        if (Math.Abs(det) < 1e-9)
        {
            x = y = z = 0;
            return false;
        }

        x = (b[0] * (a[1, 1] * a[2, 2] - a[1, 2] * a[2, 1])
            - a[0, 1] * (b[1] * a[2, 2] - a[1, 2] * b[2])
            + a[0, 2] * (b[1] * a[2, 1] - a[1, 1] * b[2])) / det;
        y = (a[0, 0] * (b[1] * a[2, 2] - a[1, 2] * b[2])
            - b[0] * (a[1, 0] * a[2, 2] - a[1, 2] * a[2, 0])
            + a[0, 2] * (a[1, 0] * b[2] - b[1] * a[2, 0])) / det;
        z = (a[0, 0] * (a[1, 1] * b[2] - b[1] * a[2, 1])
            - a[0, 1] * (a[1, 0] * b[2] - b[1] * a[2, 0])
            + b[0] * (a[1, 0] * a[2, 1] - a[1, 1] * a[2, 0])) / det;
        return true;
    }

    [RelayCommand]
    private void GoBack() => _shell.NavigateTo(Workspace.Design);
}
