using Avalonia.Controls;
using Avalonia.Interactivity;
using DiscImageStudio.Desktop.ViewModels;

namespace DiscImageStudio.Desktop.Views;

public partial class ProjectSettingsWindow : Window
{
    public ProjectSettingsWindow()
    {
        InitializeComponent();
    }

    public ProjectSettingsWindow(ProjectSettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ProjectSettingsViewModel.Saved) && viewModel.Saved)
            {
                Close();
            }
        };
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
