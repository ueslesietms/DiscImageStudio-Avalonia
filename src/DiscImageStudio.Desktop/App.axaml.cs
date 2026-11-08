using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace DiscImageStudio.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Console.Error.WriteLine("[UI exception] " + e.Exception);
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow window = new();
            desktop.MainWindow = window;

            string? projectPath = Program.Arguments.FirstOrDefault(
                argument => !argument.StartsWith('-') && File.Exists(argument));
            if (projectPath is not null)
            {
                window.OpenProject(projectPath);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
