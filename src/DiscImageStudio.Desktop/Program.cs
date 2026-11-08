using Avalonia;

namespace DiscImageStudio.Desktop;

internal static class Program
{
    /// <summary>命令行参数：第一个非选项参数视为要打开的工程路径。</summary>
    public static string[] Arguments { get; private set; } = [];

    [STAThread]
    public static int Main(string[] arguments)
    {
        Arguments = arguments;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(arguments);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
