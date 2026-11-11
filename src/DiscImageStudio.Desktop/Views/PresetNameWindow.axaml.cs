using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Linq;

namespace DiscImageStudio.Desktop.Views;

/// <summary>保存预设前问名字；确认后返回去空白的名称，取消返回 null。</summary>
public partial class PresetNameWindow : Window
{
    public PresetNameWindow()
    {
        InitializeComponent();
        Opened += (_, _) => NameBox.Focus();
    }

    public PresetNameWindow(string initial) : this() => NameBox.Text = initial;

    private void Save_Click(object? sender, RoutedEventArgs e) => Close(NameBox.Text?.Trim());

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
    }
}
