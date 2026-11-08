using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DiscImageStudio.Desktop.Controls;

/// <summary>画布调色板：优先取应用资源里的盘面令牌，取不到就用冻结的默认值。</summary>
internal static class DiscTheme
{
    public static IBrush Backdrop => Resolve("DiscCanvasBackdropBrush", 0xFF0D0D0F);

    public static IBrush DiscBody => Resolve("DiscBodyBrush", 0xFF1C1C20);

    public static IBrush Hub => Resolve("DiscHubBrush", 0xFF0D0D0F);

    public static IBrush Guide => Resolve("DiscGuideBrush", 0x55A1A1AA);

    public static IBrush Mask => Resolve("DiscMaskBrush", 0x88000000);

    public static IBrush Selection => Resolve("DiscSelectionBrush", 0xFF3385FF);

    public static IBrush MissingImage => Resolve("DangerBrush", 0xFFF31260);

    public static IBrush DataRegion => Resolve("DiscDataRegionBrush", 0x663385FF);

    public static Typeface Typeface => new(
        Application.Current is { } app
            && app.TryFindResource("AppFontFamily", out object? value)
            && value is FontFamily family
            ? family
            : FontFamily.Default,
        FontStyle.Normal,
        FontWeight.Normal);

    private static IBrush Resolve(string key, uint argb)
    {
        if (Application.Current is { } app && app.TryFindResource(key, out object? value) && value is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Color.FromUInt32(argb));
    }
}
