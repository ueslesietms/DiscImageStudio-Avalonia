using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DiscImageStudio.ImageIO;

namespace DiscImageStudio.Desktop.Services.Calibration;

/// <summary>标定用工作位图：照片是 BGRA32，标靶渲染是 Gray8；Core 的采样/重采样只认这块字节。</summary>
public sealed record CalibrationRaster(int Width, int Height, byte[] Pixels, bool Grayscale)
{
    public static CalibrationRaster FromPhoto(BgraRaster raster) =>
        new(raster.Width, raster.Height, raster.Pixels, Grayscale: false);

    /// <summary>转成可直接绑到 Image.Source 的位图。像素是内嵌内存，不需要中间文件。</summary>
    public Bitmap ToImage()
    {
        // Avalonia 12 的 WriteableBitmap 不接受 Gray8，灰度图在这里展开成 BGRA；
        // 只用于页面预览，高分辨率标靶直接写 PNG，不经过这里。
        var bitmap = new WriteableBitmap(
            new PixelSize(Width, Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using ILockedFramebuffer buffer = bitmap.Lock();
        int rowBytes = Width * 4;
        if (!Grayscale)
        {
            for (int y = 0; y < Height; y++)
            {
                Marshal.Copy(Pixels, y * rowBytes, buffer.Address + y * buffer.RowBytes, rowBytes);
            }

            return bitmap;
        }

        byte[] row = new byte[rowBytes];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                byte value = Pixels[y * Width + x];
                int offset = x * 4;
                row[offset] = value;
                row[offset + 1] = value;
                row[offset + 2] = value;
                row[offset + 3] = 255;
            }

            Marshal.Copy(row, 0, buffer.Address + y * buffer.RowBytes, rowBytes);
        }

        return bitmap;
    }

    /// <summary>缩放（双线性）；照片解码后按尺寸上限收一次，避免逐像素重采样用超大图。</summary>
    public CalibrationRaster ScaleToFit(int maxDimension, long maxPixelCount)
    {
        double scale = Math.Min(1, Math.Min(
            (double)maxDimension / Math.Max(Width, Height),
            Math.Sqrt((double)maxPixelCount / ((long)Width * Height))));
        if (scale >= 1)
        {
            return this;
        }

        int width = Math.Max(1, (int)Math.Floor(Width * scale));
        int height = Math.Max(1, (int)Math.Floor(Height * scale));
        int bytesPerPixel = Grayscale ? 1 : 4;
        int sourceStride = Width * bytesPerPixel;
        int stride = width * bytesPerPixel;
        byte[] pixels = new byte[checked(stride * height)];
        double scaleX = (double)Width / width, scaleY = (double)Height / height;
        Parallel.For(0, height, y =>
        {
            double sy = Math.Min(Height - 1, (y + 0.5) * scaleY - 0.5);
            int y0 = Math.Max(0, (int)Math.Floor(sy));
            int y1 = Math.Min(Height - 1, y0 + 1);
            double fy = sy - y0;
            for (int x = 0; x < width; x++)
            {
                double sx = Math.Min(Width - 1, (x + 0.5) * scaleX - 0.5);
                int x0 = Math.Max(0, (int)Math.Floor(sx));
                int x1 = Math.Min(Width - 1, x0 + 1);
                double fx = sx - x0;
                int destination = y * stride + x * bytesPerPixel;
                for (int channel = 0; channel < bytesPerPixel; channel++)
                {
                    double value =
                        Pixels[y0 * sourceStride + x0 * bytesPerPixel + channel] * (1 - fx) * (1 - fy)
                        + Pixels[y0 * sourceStride + x1 * bytesPerPixel + channel] * fx * (1 - fy)
                        + Pixels[y1 * sourceStride + x0 * bytesPerPixel + channel] * (1 - fx) * fy
                        + Pixels[y1 * sourceStride + x1 * bytesPerPixel + channel] * fx * fy;
                    pixels[destination + channel] = (byte)Math.Clamp((int)Math.Round(value), 0, 255);
                }
            }
        });
        return new CalibrationRaster(width, height, pixels, Grayscale);
    }
}
