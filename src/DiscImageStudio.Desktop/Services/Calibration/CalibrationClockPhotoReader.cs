using System.Numerics;
using DiscImageStudio.Core;
using DiscImageStudio.Core.Calibration;

namespace DiscImageStudio.Desktop.Services.Calibration;

internal static class CalibrationClockPhotoReader
{
    public static CalibrationClockResult Read(CalibrationRaster rectified, double canvasRadiusMm, CalibrationTarget target,
        double? preferredAngleDegrees, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return CalibrationClockReader.Read(target, canvasRadiusMm, CreateSampler(rectified, canvasRadiusMm), preferredAngleDegrees, token);
    }

    internal static Func<double, double, Vector3> CreateSampler(CalibrationRaster rectified, double canvasRadiusMm)
    {
        if (rectified.Width != rectified.Height)
            throw DiagnosticMessage.Argument($"时钟标尺需要矫正后的正方形照片。", nameof(rectified));
        int size = rectified.Width;
        byte[] pixels = rectified.Pixels;
        double center = (size - 1) / 2.0, scale = size / (2 * canvasRadiusMm);
        return Sample;

        Vector3 Sample(double xMm, double yMm)
        {
            double x = center + xMm * scale, y = center + yMm * scale;
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            if ((uint)x0 >= (uint)(size - 1) || (uint)y0 >= (uint)(size - 1)) return Vector3.Zero;
            int top = 4 * (y0 * size + x0), bottom = top + 4 * size;
            float fx = (float)(x - x0), fy = (float)(y - y0);
            return Vector3.Lerp(Vector3.Lerp(Rgb(top), Rgb(top + 4), fx),
                Vector3.Lerp(Rgb(bottom), Rgb(bottom + 4), fx), fy) * (1f / 255);
        }
        Vector3 Rgb(int offset) => new(pixels[offset + 2], pixels[offset + 1], pixels[offset]);
    }
}
