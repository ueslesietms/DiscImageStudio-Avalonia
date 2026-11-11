using Avalonia.Controls;
using Avalonia.Interactivity;
using DiscImageStudio.Desktop.ViewModels;

namespace DiscImageStudio.Desktop.Views;

public partial class DiscSettingsWindow : Window
{
    public DiscSettingsWindow()
    {
        InitializeComponent();
    }

    public DiscSettingsWindow(DiscSettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DiscSettingsViewModel.Saved) && viewModel.Saved)
            {
                Close();
            }
        };
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
