using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DiscImageStudio.Desktop.ViewModels;

namespace DiscImageStudio.Desktop.Controls;

/// <summary>离屏盘面合成：只画盘体和图层，不含引导圈、遮罩和选中框，用作导出/刻录的源图。</summary>
public class DiscCompositeControl : Control
{
    public static readonly StyledProperty<DesignPageViewModel?> DesignProperty =
        AvaloniaProperty.Register<DiscCompositeControl, DesignPageViewModel?>(nameof(Design));

    public DesignPageViewModel? Design
    {
        get => GetValue(DesignProperty);
        set => SetValue(DesignProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        context.DrawRectangle(Brushes.Black, null, new Rect(Bounds.Size));
        if (Design is not { } design)
        {
            return;
        }

        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0)
        {
            return;
        }

        double scale = size / (2 * DesignPageViewModel.DiscRadiusMm);
        Point center = new(Bounds.Width / 2, Bounds.Height / 2);
        double discRadius = DesignPageViewModel.DiscRadiusMm * scale;
        double hubRadius = DesignPageViewModel.HubRadiusMm * scale;

        context.DrawEllipse(DiscTheme.DiscBody, null, center, discRadius, discRadius);
        context.DrawEllipse(DiscTheme.Hub, null, center, hubRadius, hubRadius);

        for (int index = design.Layers.Count - 1; index >= 0; index--)
        {
            DesignLayerViewModel layer = design.Layers[index];
            if (!layer.IsVisible || !layer.HasImage)
            {
                continue;
            }

            double widthPx = layer.BaseWidthMm * layer.Scale * scale;
            double heightPx = layer.BaseHeightMm * layer.Scale * scale;
            if (widthPx <= 0 || heightPx <= 0)
            {
                continue;
            }

            Point layerCenter = new(
                center.X + layer.TranslateXMm * scale,
                center.Y + layer.TranslateYMm * scale);
            Matrix matrix = Matrix.CreateRotation(layer.RotationDeg * Math.PI / 180.0)
                * Matrix.CreateTranslation(layerCenter.X, layerCenter.Y);
            using (context.PushTransform(matrix))
            {
                context.DrawImage(layer.Image!, new Rect(-widthPx / 2, -heightPx / 2, widthPx, heightPx));
            }
        }
    }
}
