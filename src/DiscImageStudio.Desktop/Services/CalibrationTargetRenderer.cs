using DiscImageStudio.Core.Calibration;

namespace DiscImageStudio.Desktop.Services;

/// <summary>把标定靶标采样成灰度位图（对应 WPF 版 CalibrationPatternRenderer 的纯采样部分）。</summary>
public static class CalibrationTargetRenderer
{
    /// <summary>画布比靶标外径多出的边距（毫米）。</summary>
    public const double CanvasMarginMm = 3.0;

    public static byte[] RenderGray(CalibrationTarget target, int size, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

        double canvasRadius = target.Parameters.OuterRadiusMm + CanvasMarginMm;
        double step = 2 * canvasRadius / size;
        byte[] pixels = new byte[checked(size * size)];
        Parallel.For(0, size, new ParallelOptions { CancellationToken = cancellationToken }, y =>
        {
            double ym = (y + 0.5) * step - canvasRadius;
            int row = y * size;
            for (int x = 0; x < size; x++)
            {
                double xm = (x + 0.5) * step - canvasRadius;
                pixels[row + x] = target.Sample(xm, ym);
            }
        });
        return pixels;
    }
}
