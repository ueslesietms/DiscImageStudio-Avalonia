using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscImageStudio.Core;

namespace DiscImageStudio.Desktop.ViewModels;

public partial class HomePageViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;

    public HomePageViewModel(ShellViewModel shell)
    {
        _shell = shell;
        Families =
        [
            new("CD", "音频 / 数据 · 700 MB", "IconDisc", OpticalDiscFamily.CompactDisc, NewProjectCommand),
            new("DVD", "数据 · 4.7 GB", "IconDisc", OpticalDiscFamily.Dvd, NewProjectCommand),
            new("Blu-ray", "单层 BD-R · 25 GB", "IconDisc", OpticalDiscFamily.BluRay, NewProjectCommand),
        ];
    }

    public string Title => "Disc Image Studio";

    public string Subtitle => "把一张图片变成光盘盘面上肉眼可见的图案。";

    public ObservableCollection<DiscFamilyCard> Families { get; }

    [RelayCommand]
    private void NewProject(DiscFamilyCard? card)
    {
        _shell.NewProject(card?.Family ?? OpticalDiscFamily.CompactDisc);
    }

    [RelayCommand]
    private Task OpenProject() => _shell.OpenProjectAsync();
}

public sealed record DiscFamilyCard(
    string Name,
    string Detail,
    string IconKey,
    OpticalDiscFamily Family,
    System.Windows.Input.ICommand Command)
{
    public Geometry? Icon => Application.Current is { } app
        && app.TryFindResource(IconKey, out object? value)
        ? value as Geometry
        : null;

    public string ActionLabel => $"新建 {Name} 工程";
}
