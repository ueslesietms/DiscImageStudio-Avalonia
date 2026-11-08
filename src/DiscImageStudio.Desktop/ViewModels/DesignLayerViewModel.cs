using System.Windows.Input;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DiscImageStudio.Desktop.ViewModels;

/// <summary>图层：一张图相对盘心的位置。平移单位毫米（+x 右、+y 下），旋转角度顺时针，
/// 缩放是相对"导入时大边撑满内容环直径"这个基准尺寸的倍数。</summary>
public partial class DesignLayerViewModel : ObservableObject
{
    public string Id { get; } = Guid.NewGuid().ToString("N");

    /// <summary>图层行点击选中，由 DesignPageViewModel 注入。</summary>
    public ICommand? SelectCommand { get; set; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _imagePath = string.Empty;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private double _translateXMm;

    [ObservableProperty]
    private double _translateYMm;

    [ObservableProperty]
    private double _rotationDeg;

    [ObservableProperty]
    private double _scale = 1.0;

    public Bitmap? Image { get; private set; }

    public Bitmap? Thumbnail => Image;

    public double BaseWidthMm { get; private set; }

    public double BaseHeightMm { get; private set; }

    public bool HasImage => Image is not null;

    public double ScalePercent
    {
        get => Scale * 100.0;
        set => Scale = value / 100.0;
    }

    partial void OnScaleChanged(double value) => OnPropertyChanged(nameof(ScalePercent));

    /// <summary>读图并按较大边撑满内容环直径算出基准尺寸；读不出来返回 false，图层画成占位框。</summary>
    public bool LoadImage(double contentOuterRadiusMm)
    {
        try
        {
            Bitmap bitmap = new(ImagePath);
            Image = bitmap;
        }
        catch (Exception)
        {
            return false;
        }

        double width = Image.PixelSize.Width;
        double height = Image.PixelSize.Height;
        if (width <= 0 || height <= 0)
        {
            Image = null;
            return false;
        }

        double diameterMm = 2.0 * contentOuterRadiusMm;
        if (width >= height)
        {
            BaseWidthMm = diameterMm;
            BaseHeightMm = diameterMm * height / width;
        }
        else
        {
            BaseHeightMm = diameterMm;
            BaseWidthMm = diameterMm * width / height;
        }

        OnPropertyChanged(nameof(Thumbnail));
        OnPropertyChanged(nameof(HasImage));
        return true;
    }
}
