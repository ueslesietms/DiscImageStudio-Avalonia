using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DiscImageStudio.Desktop.ViewModels;

/// <summary>
/// 骨架阶段的工作区占位页：保留真实页面的标题、副标题与阶段位置，
/// 具体内容在迁移对应 View 时替换。
/// </summary>
public partial class WorkspacePageViewModel : ObservableObject
{
    public WorkspacePageViewModel(Workspace workspace, string title, string subtitle, string iconKey)
    {
        Workspace = workspace;
        Title = title;
        Subtitle = subtitle;
        Icon = Application.Current is { } app && app.TryFindResource(iconKey, out object? value)
            ? value as Geometry
            : null;
    }

    public Workspace Workspace { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public Geometry? Icon { get; }

    public string Placeholder => "这一页正在从 WPF 迁移到 Avalonia，先占个位。";
}
