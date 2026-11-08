using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using DiscImageStudio.Localization;

namespace DiscImageStudio.Desktop.Controls;

/// <summary>路径行：可选标签、双向路径框、浏览按钮、提示行。</summary>
public partial class PathField : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<PathField, string>(nameof(Label), string.Empty);

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<PathField, string>(nameof(Text), string.Empty,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> ButtonLabelProperty =
        AvaloniaProperty.Register<PathField, string>(nameof(ButtonLabel), string.Empty);

    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<PathField, ICommand?>(nameof(Command));

    public static readonly StyledProperty<string> HintProperty =
        AvaloniaProperty.Register<PathField, string>(nameof(Hint), string.Empty);

    public static readonly StyledProperty<string> PlaceholderProperty =
        AvaloniaProperty.Register<PathField, string>(nameof(Placeholder), string.Empty);

    public PathField()
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<TextBox>("PathTextBox")!.Bind(TextBox.TextProperty,
            new Binding(nameof(Text)) { Source = this, Mode = BindingMode.TwoWay });
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string ButtonLabel
    {
        get => GetValue(ButtonLabelProperty);
        set => SetValue(ButtonLabelProperty, value);
    }

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public string Hint
    {
        get => GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    public string Placeholder
    {
        get => GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == LabelProperty)
        {
            this.FindControl<TextBlock>("LabelText")?.SetCurrentValue(TextBlock.TextProperty, Label);
            this.FindControl<TextBlock>("LabelText")?.SetCurrentValue(IsVisibleProperty, !string.IsNullOrWhiteSpace(Label));
        }
        else if (change.Property == ButtonLabelProperty)
        {
            this.FindControl<Button>("BrowseButton")?.SetCurrentValue(ContentControl.ContentProperty,
                string.IsNullOrWhiteSpace(ButtonLabel) ? L.T("浏览") : ButtonLabel);
        }
        else if (change.Property == CommandProperty)
        {
            this.FindControl<Button>("BrowseButton")?.SetCurrentValue(Button.CommandProperty, Command);
        }
        else if (change.Property == HintProperty)
        {
            this.FindControl<TextBlock>("HintTextBlock")?.SetCurrentValue(TextBlock.TextProperty, Hint);
            this.FindControl<TextBlock>("HintTextBlock")?.SetCurrentValue(IsVisibleProperty, !string.IsNullOrWhiteSpace(Hint));
        }
        else if (change.Property == PlaceholderProperty)
        {
            this.FindControl<TextBox>("PathTextBox")?.SetCurrentValue(TextBox.PlaceholderTextProperty,
                string.IsNullOrWhiteSpace(Placeholder) ? null : Placeholder);
        }
    }
}
