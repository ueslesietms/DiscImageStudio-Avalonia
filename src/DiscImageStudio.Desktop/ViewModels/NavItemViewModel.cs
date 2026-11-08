using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DiscImageStudio.Desktop.ViewModels;

public partial class NavItemViewModel : ObservableObject
{
    public NavItemViewModel(Action<NavItemViewModel> select, Workspace workspace, string label, string iconKey)
    {
        SelectCommand = new RelayCommand(() => select(this));
        Workspace = workspace;
        Label = label;
        Icon = Application.Current is { } app && app.TryFindResource(iconKey, out object? value)
            ? value as Geometry
            : null;
    }

    public ICommand SelectCommand { get; }

    public Workspace Workspace { get; }

    public string Label { get; }

    public Geometry? Icon { get; }

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _showLabel = true;
}
