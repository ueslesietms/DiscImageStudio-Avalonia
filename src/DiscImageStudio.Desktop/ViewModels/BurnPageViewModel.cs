using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscImageStudio.Core;
using DiscImageStudio.Desktop.Services;
using DiscImageStudio.Localization;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Desktop.ViewModels;

/// <summary>刻录页：选设备、确认介质、把导出的镜像/音轨写进空白盘。Windows 走 IMAPI，Linux 走 cdrecord / growisofs。</summary>
public partial class BurnPageViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;
    private readonly IDiscBurner _burner;
    private CancellationTokenSource? _cancellation;

    public BurnPageViewModel(ShellViewModel shell)
    {
        _shell = shell;
        _burner = BurnerFactory.Create();
        _shell.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ShellViewModel.CurrentProject) or nameof(ShellViewModel.IsProjectOpen))
            {
                OnPropertyChanged(nameof(FamilyLabel));
                OnPropertyChanged(nameof(SourcePath));
            }
        };
        _shell.State.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(SourcePath));
            OnPropertyChanged(nameof(CanBurn));
            BurnCommand.NotifyCanExecuteChanged();
        };
        _ = RefreshDevicesAsync();
    }

    public DiscParametersState State => _shell.State;

    internal IDiscBurner Burner => _burner;

    public string BackendName => _burner.BackendName;

    public string FamilyLabel => _shell.CurrentProject?.FamilyLabel ?? "CD";

    public ObservableCollection<BurnDevice> Devices { get; } = [];

    public ObservableCollection<string> Log { get; } = [];

    [ObservableProperty]
    private BurnDevice? _selectedDevice;

    [ObservableProperty]
    private string _mediaSummary = "还没有读取介质。";

    [ObservableProperty]
    private bool _mediaIsBlank;

    [ObservableProperty]
    private bool _isBurning;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusMessage = "点“刷新设备”读取光驱和盘片。";

    [ObservableProperty]
    private string? _speedText;

    /// <summary>默认取导出页刚生成的文件；也可以手动换。</summary>
    public string SourcePath
    {
        get
        {
            string raw = _shell.CurrentProject?.Family switch
            {
                OpticalDiscFamily.Dvd => State.DvdOutputPath,
                OpticalDiscFamily.BluRay => State.BdOutputPath,
                _ => State.CdOutputPath,
            };
            return raw.Trim();
        }
        set
        {
            switch (_shell.CurrentProject?.Family)
            {
                case OpticalDiscFamily.Dvd:
                    State.DvdOutputPath = value;
                    break;
                case OpticalDiscFamily.BluRay:
                    State.BdOutputPath = value;
                    break;
                default:
                    State.CdOutputPath = value;
                    break;
            }

            OnPropertyChanged();
        }
    }

    public bool HasDevices => Devices.Count > 0;

    public bool CanBurn => !IsBurning && SelectedDevice is not null && SourcePath.Length > 0;

    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        try
        {
            StatusMessage = "正在读取设备…";
            Devices.Clear();
            foreach (BurnDevice device in await _burner.GetDevicesAsync())
            {
                Devices.Add(device);
            }

            OnPropertyChanged(nameof(HasDevices));
            SelectedDevice = Devices.FirstOrDefault();
            StatusMessage = Devices.Count == 0 ? "没有找到光驱。" : $"找到 {Devices.Count} 个光驱。";
            if (SelectedDevice is not null)
            {
                await ReadMediaAsync();
            }
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    partial void OnSelectedDeviceChanged(BurnDevice? value)
    {
        OnPropertyChanged(nameof(CanBurn));
        BurnCommand.NotifyCanExecuteChanged();
        if (value is not null)
        {
            _ = ReadMediaAsync();
        }
    }

    private async Task ReadMediaAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        try
        {
            BurnMediaInfo? media = await _burner.GetMediaAsync(SelectedDevice.Id);
            if (media is null)
            {
                MediaSummary = "没有读取到介质（空仓或无法识别）。";
                MediaIsBlank = false;
            }
            else
            {
                MediaSummary = media.IsBlank
                    ? $"{media.Description}（空白，可写 {media.WritableSectors:N0} 扇区）"
                    : $"{media.Description}（已写入过）";
                MediaIsBlank = media.IsBlank;
            }
        }
        catch (Exception exception)
        {
            MediaSummary = exception.Message;
        }
    }

    [RelayCommand]
    private async Task BrowseSourceAsync()
    {
        if (_shell.Dialogs is not { } dialogs)
        {
            return;
        }

        string? path = await dialogs.PickOpenFileAsync(
            L.T("选择要刻录的文件"),
            L.T("镜像或音轨|*.wav;*.raw;*.iso|所有文件|*.*"));
        if (path is not null)
        {
            SourcePath = path;
            OnPropertyChanged(nameof(CanBurn));
            BurnCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanBurn))]
    private async Task BurnAsync()
    {
        if (SelectedDevice is not { } device)
        {
            return;
        }

        string source = SourcePath;
        if (!File.Exists(source))
        {
            StatusMessage = "找不到要刻录的文件，先导出。";
            return;
        }

        IsBurning = true;
        Progress = 0;
        Log.Clear();
        BurnCommand.NotifyCanExecuteChanged();
        _cancellation = new CancellationTokenSource();
        Progress<BurnProgress> progress = new(value =>
        {
            if (!double.IsNaN(value.Fraction))
            {
                Progress = value.Fraction;
            }

            if (value.Message.Length > 0)
            {
                StatusMessage = value.Message;
                if (Log.Count == 0 || Log[^1] != value.Message)
                {
                    Log.Add(value.Message);
                    while (Log.Count > 200)
                    {
                        Log.RemoveAt(0);
                    }
                }
            }
        });

        try
        {
            BurnJob job = new(device.Id, ResolveKind(source), source, ParseSpeed());
            await _burner.BurnAsync(job, progress, _cancellation.Token);
            StatusMessage = "刻录完成，可以取盘了。";
            Progress = 1;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消。";
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBurning = false;
            _cancellation.Dispose();
            _cancellation = null;
            BurnCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cancellation?.Cancel();
    }

    private double? ParseSpeed() =>
        double.TryParse(SpeedText?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
        && value > 0
            ? value
            : null;

    private BurnMediaKind ResolveKind(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".iso")
        {
            return _shell.CurrentProject?.Family switch
            {
                OpticalDiscFamily.Dvd => BurnMediaKind.DvdData,
                OpticalDiscFamily.BluRay => BurnMediaKind.BluRayData,
                _ => BurnMediaKind.CdData,
            };
        }

        return _shell.CurrentProject?.Family == OpticalDiscFamily.CompactDisc
            ? BurnMediaKind.CdAudio
            : BurnMediaKind.DvdData;
    }

    [RelayCommand]
    private void GoBack() => _shell.NavigateTo(Workspace.Export);
}
