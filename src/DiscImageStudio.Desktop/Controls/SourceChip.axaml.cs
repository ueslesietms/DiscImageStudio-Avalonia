using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Markup.Xaml;

namespace DiscImageStudio.Desktop.Controls;

/// <summary>只读来源条：说明这个工作区吃什么，右侧可挂一个跳转动作。</summary>
public partial class SourceChip : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<SourceChip, string>(nameof(Text), string.Empty);

    public static readonly StyledProperty<object?> ActionsProperty =
        AvaloniaProperty.Register<SourceChip, object?>(nameof(Actions));

    public SourceChip()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty)
        {
            this.FindControl<TextBlock>("ChipText")?.SetCurrentValue(TextBlock.TextProperty, Text);
        }
        else if (change.Property == ActionsProperty)
        {
            if (this.FindControl<ContentPresenter>("ActionHost") is { } host)
            {
                host.Content = Actions;
            }
        }
    }
}
