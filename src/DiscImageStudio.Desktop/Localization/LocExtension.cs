using Avalonia.Markup.Xaml;
using DiscImageStudio.Localization;

namespace DiscImageStudio.Desktop.Localization;

/// <summary>Avalonia 版 {loc:Loc Key}：XAML 里直接取当前语言的文案。</summary>
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Key);
}
