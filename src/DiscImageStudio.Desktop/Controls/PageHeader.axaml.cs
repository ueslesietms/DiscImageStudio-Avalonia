using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Markup.Xaml;

namespace DiscImageStudio.Desktop.Controls;

/// <summary>工作区页眉：标题、盘型徽章、副标题、右侧动作槽。阶段条留到工作流接线时补。</summary>
public partial class PageHeader : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<PageHeader, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<string> BadgeProperty =
        AvaloniaProperty.Register<PageHeader, string>(nameof(Badge), string.Empty);

    public static readonly StyledProperty<string> SubtitleProperty =
        AvaloniaProperty.Register<PageHeader, string>(nameof(Subtitle), string.Empty);

    public static readonly StyledProperty<object?> ActionsProperty =
        AvaloniaProperty.Register<PageHeader, object?>(nameof(Actions));

    public PageHeader()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Badge
    {
        get => GetValue(BadgeProperty);
        set => SetValue(BadgeProperty, value);
    }

    public string Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TitleProperty)
        {
            this.FindControl<TextBlock>("TitleText")?.SetCurrentValue(TextBlock.TextProperty, Title);
        }
        else if (change.Property == BadgeProperty)
        {
            this.FindControl<Border>("BadgeChip")?.SetCurrentValue(IsVisibleProperty, !string.IsNullOrWhiteSpace(Badge));
            this.FindControl<TextBlock>("BadgeText")?.SetCurrentValue(TextBlock.TextProperty, Badge);
        }
        else if (change.Property == SubtitleProperty)
        {
            this.FindControl<TextBlock>("SubtitleText")?.SetCurrentValue(TextBlock.TextProperty, Subtitle);
            this.FindControl<TextBlock>("SubtitleText")?.SetCurrentValue(IsVisibleProperty, !string.IsNullOrWhiteSpace(Subtitle));
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
