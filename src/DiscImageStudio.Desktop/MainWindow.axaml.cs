using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DiscImageStudio.Desktop.ViewModels;

namespace DiscImageStudio.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 三平台统一：无边框窗口 + 自绘标题栏 + 边缘缩放热区。
        ShellViewModel shell = new();
        shell.Dialogs = new Services.AvaloniaDialogService(this);
        DataContext = shell;
    }

    /// <summary>启动参数里带工程路径时直接打开。</summary>
    public void OpenProject(string path)
    {
        if (DataContext is ShellViewModel shell)
        {
            shell.OpenProject(path);
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void Resize_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { Tag: string edge } || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        WindowEdge? target = edge switch
        {
            "North" => Avalonia.Controls.WindowEdge.North,
            "South" => Avalonia.Controls.WindowEdge.South,
            "West" => Avalonia.Controls.WindowEdge.West,
            "East" => Avalonia.Controls.WindowEdge.East,
            "NorthWest" => Avalonia.Controls.WindowEdge.NorthWest,
            "NorthEast" => Avalonia.Controls.WindowEdge.NorthEast,
            "SouthWest" => Avalonia.Controls.WindowEdge.SouthWest,
            "SouthEast" => Avalonia.Controls.WindowEdge.SouthEast,
            _ => null,
        };

        if (target is { } value)
        {
            BeginResizeDrag(value, e);
        }
    }

    private void Settings_Click(object? sender, RoutedEventArgs e)
    {
        // 设置窗口在迁移 SettingsWindow 后接上。
    }

    private void Minimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
