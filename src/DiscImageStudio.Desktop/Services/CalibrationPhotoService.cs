using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DiscImageStudio.Core.Calibration;
using DiscImageStudio.ImageIO;
using DiscImageStudio.Localization;

namespace DiscImageStudio.Desktop.Services;

/// <summary>照片读入、初始轮廓与透视矫正。像素一律是紧密排列的 BGRA32。</summary>
public static class CalibrationPhotoService
{
    public static (byte[] Pixels, int Width, int Height) Load(
        string path, int maxDimension = CalibrationPhotoLimits.MaxDimension, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        BgraRaster raster = RasterCodec.LoadBgra32(path, maxFileBytes: 256L * 1024 * 1024);
        if (raster.Width < 32 || raster.Height < 32 || !CalibrationPhotoLimits.Contains(raster.Width, raster.Height))
        {
            throw new ArgumentException(L.T("照片尺寸无效。"));
        }

        double scale = Math.Min(1, Math.Min(
            (double)maxDimension / Math.Max(raster.Width, raster.Height),
            Math.Sqrt((double)CalibrationPhotoLimits.MaxPixelCount / ((long)raster.Width * raster.Height))));
        if (scale >= 1)
        {
            return (raster.Pixels, raster.Width, raster.Height);
        }

        int width = Math.Max(1, (int)Math.Floor(raster.Width * scale));
        int height = Math.Max(1, (int)Math.Floor(raster.Height * scale));
        byte[] scaled = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y++)
        {
            double sy = (y + 0.5) * raster.Height / height - 0.5;
            int y0 = Math.Clamp((int)Math.Floor(sy), 0, raster.Height - 1);
            int y1 = Math.Min(y0 + 1, raster.Height - 1);
            double fy = Math.Clamp(sy - y0, 0, 1);
            for (int x = 0; x < width; x++)
            {
                double sx = (x + 0.5) * raster.Width / width - 0.5;
                int x0 = Math.Clamp((int)Math.Floor(sx), 0, raster.Width - 1);
                int x1 = Math.Min(x0 + 1, raster.Width - 1);
                double fx = Math.Clamp(sx - x0, 0, 1);
                int destination = (y * width + x) * 4;
                for (int channel = 0; channel < 4; channel++)
                {
                    double value =
                        raster.Pixels[(y0 * raster.Width + x0) * 4 + channel] * (1 - fx) * (1 - fy)
                        + raster.Pixels[(y0 * raster.Width + x1) * 4 + channel] * fx * (1 - fy)
                        + raster.Pixels[(y1 * raster.Width + x0) * 4 + channel] * (1 - fx) * fy
                        + raster.Pixels[(y1 * raster.Width + x1) * 4 + channel] * fx * fy;
                    scaled[destination + channel] = (byte)Math.Clamp((int)Math.Round(value), 0, 255);
                }
            }
        }

        return (scaled, width, height);
    }

    /// <summary>沿照片中心给出两条椭圆初始轮廓（实体外缘、中心孔缘），供用户再拖点。</summary>
    public static (CalibrationPoint[] Outer, CalibrationPoint[] Hole) SuggestContours(int width, int height, int count = 8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 5);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 64);
        double cx = (width - 1) / 2.0, cy = (height - 1) / 2.0;
        double rx = width * 0.43, ry = height * 0.43;
        CalibrationPoint[] Make(double ratio) => Enumerable.Range(0, count).Select(index =>
        {
            double angle = -Math.PI / 2 + 2 * Math.PI * index / count;
            return new CalibrationPoint(cx + rx * ratio * Math.Cos(angle), cy + ry * ratio * Math.Sin(angle));
        }).ToArray();
        return (Make(1), Make(0.125));
    }

    public static (byte[] Pixels, int Size) Rectify(
        byte[] source, int sourceWidth, int sourceHeight,
        CalibrationPhotoTransform transform, double canvasOuterRadiusMm,
        int pixelSize = 0, CancellationToken cancellationToken = default)
    {
        if (pixelSize == 0)
        {
            pixelSize = GetRectifiedPixelSize(transform, canvasOuterRadiusMm);
        }

        double[] matrix = transform.DiscToSourceMatrix9;
        double step = 2 * canvasOuterRadiusMm / pixelSize;
        byte[] pixels = new byte[checked(pixelSize * pixelSize * 4)];
        Parallel.For(0, pixelSize, new ParallelOptions { CancellationToken = cancellationToken }, y =>
        {
            double ym = (y + 0.5) * step - canvasOuterRadiusMm;
            for (int x = 0; x < pixelSize; x++)
            {
                double xm = (x + 0.5) * step - canvasOuterRadiusMm;
                int destination = (y * pixelSize + x) * 4;
                double divisor = matrix[6] * xm + matrix[7] * ym + matrix[8];
                double sx = (matrix[0] * xm + matrix[1] * ym + matrix[2]) / divisor;
                double sy = (matrix[3] * xm + matrix[4] * ym + matrix[5]) / divisor;
                if (xm * xm + ym * ym > canvasOuterRadiusMm * canvasOuterRadiusMm
                    || !double.IsFinite(sx) || !double.IsFinite(sy)
                    || sx < 0 || sy < 0 || sx > sourceWidth - 1 || sy > sourceHeight - 1)
                {
                    pixels[destination] = pixels[destination + 1] = pixels[destination + 2] = 255;
                    pixels[destination + 3] = 255;
                    continue;
                }

                int x0 = (int)Math.Floor(sx), y0 = (int)Math.Floor(sy);
                int x1 = Math.Min(x0 + 1, sourceWidth - 1), y1 = Math.Min(y0 + 1, sourceHeight - 1);
                double fx = sx - x0, fy = sy - y0;
                for (int channel = 0; channel < 4; channel++)
                {
                    double value =
                        source[(y0 * sourceWidth + x0) * 4 + channel] * (1 - fx) * (1 - fy)
                        + source[(y0 * sourceWidth + x1) * 4 + channel] * fx * (1 - fy)
                        + source[(y1 * sourceWidth + x0) * 4 + channel] * (1 - fx) * fy
                        + source[(y1 * sourceWidth + x1) * 4 + channel] * fx * fy;
                    pixels[destination + channel] = (byte)Math.Clamp((int)Math.Round(value), 0, 255);
                }
            }
        });
        return (pixels, pixelSize);
    }

    public static int GetRectifiedPixelSize(CalibrationPhotoTransform transform, double canvasOuterRadiusMm)
    {
        double[] m = transform.DiscToSourceMatrix9;
        double maxScale = ScaleAt(0, 0);
        for (int ring = 1; ring <= 2; ring++)
        {
            for (int index = 0; index < 64; index++)
            {
                double angle = index * Math.Tau / 64;
                double radius = canvasOuterRadiusMm * ring / 2;
                maxScale = Math.Max(maxScale, ScaleAt(radius * Math.Cos(angle), radius * Math.Sin(angle)));
            }
        }

        double size = Math.Ceiling(2 * canvasOuterRadiusMm * maxScale - 1e-9);
        return (int)Math.Clamp(size, 32, CalibrationPhotoLimits.MaxRectifiedDimension);

        double ScaleAt(double x, double y)
        {
            double w = m[6] * x + m[7] * y + m[8];
            double u = (m[0] * x + m[1] * y + m[2]) / w;
            double v = (m[3] * x + m[4] * y + m[5]) / w;
            double ux = (m[0] - m[6] * u) / w, uy = (m[1] - m[7] * u) / w;
            double vx = (m[3] - m[6] * v) / w, vy = (m[4] - m[7] * v) / w;
            double a = ux * ux + vx * vx, b = ux * uy + vx * vy, c = uy * uy + vy * vy;
            return Math.Sqrt((a + c + Math.Sqrt((a - c) * (a - c) + 4 * b * b)) / 2);
        }
    }

    /// <summary>BGRA 像素 → Avalonia 位图（写时复制，调用方拥有新位图）。</summary>
    public static WriteableBitmap ToBitmap(byte[] bgra, int width, int height)
    {
        WriteableBitmap bitmap = new(
            new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using ILockedFramebuffer buffer = bitmap.Lock();
        int stride = buffer.RowBytes;
        for (int y = 0; y < height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(
                bgra, y * width * 4, buffer.Address + y * stride, width * 4);
        }

        return bitmap;
    }

    public static string SavePng(byte[] bgra, int width, int height, string path)
    {
        // RasterCodec 的 BGRA 写入需要直通 alpha 顺序，这里是整块拷贝，不修改调用方数据。
        RasterCodec.SavePng(path, width, height, bgra, grayscale: false);
        return path;
    }
}
