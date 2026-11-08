using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DiscImageStudio.Desktop.Controls;

/// <summary>卡片：图标 + 标题 + 内容。模板见 Themes/Controls.axaml（ContentControl 的
/// Content 就是内容槽，避免和模板里的视觉树抢同一个属性）。</summary>
public class SectionCard : ContentControl
{
    public static readonly StyledProperty<string> HeaderProperty =
        AvaloniaProperty.Register<SectionCard, string>(nameof(Header), string.Empty);

    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<SectionCard, Geometry?>(nameof(Icon));

    public string Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }
}
