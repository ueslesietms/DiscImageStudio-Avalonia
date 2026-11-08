using Avalonia;
using Avalonia.Controls;

namespace DiscImageStudio.Desktop.Controls;

/// <summary>按一个 bool 显示/隐藏内容；Inverted 时翻转。</summary>
public class VisibilityGate : ContentControl
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<VisibilityGate, bool>(nameof(IsOpen));

    public static readonly StyledProperty<bool> InvertedProperty =
        AvaloniaProperty.Register<VisibilityGate, bool>(nameof(Inverted));

    public VisibilityGate()
    {
        // StyledProperty 默认值不会走 OnPropertyChanged，初始状态得自己设。
        IsVisible = IsOpen != Inverted;
    }

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public bool Inverted
    {
        get => GetValue(InvertedProperty);
        set => SetValue(InvertedProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsOpenProperty || change.Property == InvertedProperty)
        {
            IsVisible = IsOpen != Inverted;
        }
    }
}
