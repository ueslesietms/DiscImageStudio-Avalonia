using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DiscImageStudio.Desktop.ViewModels;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>标靶变化通知：刻录页或未来的作业面板可以接这个钩子。</summary>
public interface ICalibrationBurnHook
{
    void OnCalibrationTargetChanged();
}

/// <summary>
/// 标定页需要的那一小块外壳能力：忙碌标志、作业状态行、日志、提示和返回。
/// 桌面版没有 WPF 版的作业停靠面板，这里用状态行 + 内存日志承载同样的语义。
/// </summary>
public partial class ShellViewModel
{
    private IRelayCommand? _returnFromCalibrationCommand;

    public Services.IDiscBurner Burner => Burn.Burner;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _jobStatusText = string.Empty;

    [ObservableProperty]
    private string _toastTitle = string.Empty;

    [ObservableProperty]
    private string _toastMessage = string.Empty;

    [ObservableProperty]
    private ToastKind _toastKind = ToastKind.Info;

    public ObservableCollection<string> LogLines { get; } = [];

    public ICalibrationBurnHook? CalibrationBurn { get; set; }

    public IRelayCommand ReturnFromCalibrationCommand =>
        _returnFromCalibrationCommand ??= new RelayCommand(() => NavigateTo(Workspace.Design));

    public void SetBusy(bool busy) => IsBusy = busy;

    public void AppendLog(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            LogLines.Add(line);
        }

        while (LogLines.Count > 500)
        {
            LogLines.RemoveAt(0);
        }
    }

    public void ShowToast(string title, string message, ToastKind kind)
    {
        ToastTitle = title;
        ToastMessage = message;
        ToastKind = kind;
        JobStatusText = message;
    }

    public void BeginDockJob(string title, bool determinate)
    {
        JobStatusText = title;
        IsBusy = true;
    }

    public void ReportDockJob(string message, double fraction) => JobStatusText = message;

    public void CompleteDockJob(string message)
    {
        JobStatusText = message;
        IsBusy = false;
    }

    public void AbortDockJob(string message, bool expandLog = false)
    {
        JobStatusText = message;
        IsBusy = false;
    }
}
