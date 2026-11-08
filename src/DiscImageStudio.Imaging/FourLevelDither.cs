#if WINDOWS
using System.Windows.Media.Imaging;
#endif

namespace DiscImageStudio.Imaging;

/// <summary>Compatibility entry points for the CD four-level palette.</summary>
public static class FourLevelDither
{
    public static ReadOnlySpan<byte> Levels => [0, 85, 170, 255];

#if WINDOWS
    public static BitmapSource LoadImage(string path) => GrayscaleDither.LoadImage(path);

    public static BitmapSource Process(BitmapSource source, CancellationToken cancellationToken = default)
        => GrayscaleDither.Process(source, Levels, cancellationToken);

    public static void SavePng(BitmapSource output, string path) => GrayscaleDither.SavePng(output, path, Levels);
#else
    public static (int Width, int Height, byte[] Pixels) LoadBgra32(string path) => GrayscaleDither.LoadBgra32(path);

    public static void SaveGrayPng(int width, int height, byte[] gray, string path)
        => GrayscaleDither.SaveGrayPng(width, height, gray, path, Levels);
#endif

    public static byte[] ProcessPixels(
        ReadOnlySpan<byte> bgra,
        int width,
        int height,
        CancellationToken cancellationToken = default)
        => GrayscaleDither.ProcessPixels(bgra, width, height, Levels, cancellationToken);
}
