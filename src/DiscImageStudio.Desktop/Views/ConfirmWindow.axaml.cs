using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DiscImageStudio.Desktop.Views;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow()
    {
        InitializeComponent();
    }

    public ConfirmWindow(string title, string message, string confirmText, string cancelText) : this()
    {
        Title = title;
        this.FindControl<TextBlock>("TitleText")!.Text = title;
        this.FindControl<TextBlock>("MessageText")!.Text = message;
        this.FindControl<Button>("ConfirmButton")!.Content = confirmText;
        this.FindControl<Button>("CancelButton")!.Content = cancelText;
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
