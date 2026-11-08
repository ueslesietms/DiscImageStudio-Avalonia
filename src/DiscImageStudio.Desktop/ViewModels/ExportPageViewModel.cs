using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscImageStudio.Cd;
using DiscImageStudio.Core;
using DiscImageStudio.Desktop.Services;
using DiscImageStudio.Dvd;
using DiscImageStudio.Localization;
using DiscImageStudio.Services;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Desktop.ViewModels;

/// <summary>导出页：盘面合成图 + 输出文件。CD 可以带真实音乐轨（多轨），DVD/BD 直接写 ISO。</summary>
public partial class ExportPageViewModel : ObservableObject
{
    private const int CompositeSize = 4096;

    private readonly ShellViewModel _shell;

    public ExportPageViewModel(ShellViewModel shell)
    {
        _shell = shell;
        _shell.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ShellViewModel.CurrentProject) or nameof(ShellViewModel.IsProjectOpen))
            {
                Refresh();
            }
        };
        _shell.State.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(DiscParametersState.CdDataDirectory)
                or nameof(DiscParametersState.DvdDataDirectory)
                or nameof(DiscParametersState.BdDataDirectory))
            {
                OnPropertyChanged(nameof(GenerateButtonText));
                OnPropertyChanged(nameof(IsMusicMode));
            }
        };
        _shell.Design.Layers.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasLayers));
            OnPropertyChanged(nameof(CanGenerate));
            GenerateCommand.NotifyCanExecuteChanged();
        };
        MusicTracks.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasMusicTracks));
            OnPropertyChanged(nameof(MusicSummary));
        };
    }

    public DiscParametersState State => _shell.State;

    public ObservableCollection<MusicTrackViewModel> MusicTracks { get; } = [];

    public bool IsCd => _shell.CurrentProject?.IsCd ?? true;

    public bool IsDvd => _shell.CurrentProject?.IsDvd ?? false;

    public bool IsBluRay => _shell.CurrentProject?.IsBluRay ?? false;

    public bool HasLayers => _shell.Design.Layers.Count > 0;

    public string FamilyLabel => _shell.CurrentProject?.FamilyLabel ?? "CD";

    public string GenerateButtonText => !IsCd || State.HasCdFiles ? L.T("生成镜像") : L.T("生成音轨");

    public string SourceSummary => _shell.CurrentProject switch
    {
        null => L.T("请先打开或新建一个工程。"),
        _ when _shell.Design.Layers.Count == 0 => L.T("请先在设计页导入图片。"),
        _ => L.F($"设计页合成图源（{_shell.Design.Layers.Count} 个图层）"),
    };

    /// <summary>当前盘型的盘片预设（内置 JSON，含实测盘）。</summary>
    public IReadOnlyList<DiscPresetItem> Presets =>
        PresetCatalog.For(_shell.CurrentProject?.Family ?? OpticalDiscFamily.CompactDisc);

    private bool _suppressPresetApply;

    [ObservableProperty]
    private DiscPresetItem? _selectedPreset;

    partial void OnSelectedPresetChanged(DiscPresetItem? value)
    {
        if (_suppressPresetApply || value is null)
        {
            return;
        }

        ApplyPreset(value);
    }

    private void ApplyPreset(DiscPresetItem preset)
    {
        string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        string Sectors(long value) => value.ToString(CultureInfo.InvariantCulture);
        string Usable(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

        if (IsCd)
        {
            State.CdInnerRadius = Number(preset.InnerRadiusMm);
            State.CdOuterRadius = Number(preset.OuterRadiusMm);
            State.CdSectors = Sectors(preset.Sectors);
            State.CdUsableSectors = Usable(preset.UsableSectors);
            State.CdPitchLinear = Number(preset.PitchLinear);
            State.CdPitchQuadratic = Number(preset.PitchQuadratic);
            State.CdPitchCubic = Number(preset.PitchCubic);
        }
        else if (IsDvd)
        {
            State.DvdInnerRadius = Number(preset.InnerRadiusMm);
            State.DvdOuterRadius = Number(preset.OuterRadiusMm);
            State.DvdTotalSectors = Sectors(preset.Sectors);
            State.DvdUsableSectors = Usable(preset.UsableSectors);
            State.DvdPitchLinear = Number(preset.PitchLinear);
            State.DvdPitchQuadratic = Number(preset.PitchQuadratic);
            State.DvdPitchCubic = Number(preset.PitchCubic);
        }
        else
        {
            State.BdInnerRadius = Number(preset.InnerRadiusMm);
            State.BdOuterRadius = Number(preset.OuterRadiusMm);
            State.BdTotalSectors = Sectors(preset.Sectors);
            State.BdUsableSectors = Usable(preset.UsableSectors);
            State.BdPitchLinear = Number(preset.PitchLinear);
            State.BdPitchQuadratic = Number(preset.PitchQuadratic);
            State.BdPitchCubic = Number(preset.PitchCubic);
            State.BdInvertMapping = preset.InvertMapping;
        }

        _shell.Design.RefreshGeometry();
    }

    /// <summary>打开工程后按当前几何反查预设；对不上就让下拉留空（自定义）。</summary>
    private void MatchPresetFromState()
    {
        DiscPresetItem? match = null;
        foreach (DiscPresetItem candidate in Presets)
        {
            if (Math.Abs(candidate.InnerRadiusMm - CurrentInnerRadius()) < 0.0005
                && Math.Abs(candidate.OuterRadiusMm - CurrentOuterRadius()) < 0.0005)
            {
                match = candidate;
                break;
            }
        }

        _suppressPresetApply = true;
        SelectedPreset = match;
        _suppressPresetApply = false;
    }

    private double CurrentInnerRadius() => ParseRadius(IsCd ? State.CdInnerRadius : IsDvd ? State.DvdInnerRadius : State.BdInnerRadius);

    private double CurrentOuterRadius() => ParseRadius(IsCd ? State.CdOuterRadius : IsDvd ? State.DvdOuterRadius : State.BdOuterRadius);

    private static double ParseRadius(string text) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : double.NaN;

    public bool HasMusicTracks => MusicTracks.Count > 0;

    /// <summary>CD 且没有随盘文件夹时才是音乐/图案音轨模式。</summary>
    public bool IsMusicMode => IsCd && !State.HasCdFiles;

    public string MusicSummary => MusicTracks.Count == 0
        ? L.T("没有音乐轨：整张盘只有图案。")
        : L.F($"共 {MusicTracks.Count} 条音乐轨，最后一轨留给图案。");

    public bool CanGenerate => _shell.IsProjectOpen && HasLayers;

    [ObservableProperty]
    private bool _isGenerating;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        if (_shell.Dialogs is not { } dialogs)
        {
            return;
        }

        if (IsBluRay)
        {
            string? iso = await dialogs.PickSaveFileAsync(
                L.T("选择蓝光 ISO 输出位置"), "bd-image.iso", ".iso", L.T("ISO 镜像|*.iso|所有文件|*.*"));
            if (iso is not null)
            {
                State.BdOutputPath = iso;
            }
        }
        else if (IsDvd)
        {
            string? iso = await dialogs.PickSaveFileAsync(
                L.T("选择 DVD ISO 输出位置"), "dvd-image.iso", ".iso", L.T("ISO 镜像|*.iso|所有文件|*.*"));
            if (iso is not null)
            {
                State.DvdOutputPath = iso;
            }
        }
        else if (State.HasCdFiles || MusicTracks.Count > 0)
        {
            string? iso = await dialogs.PickSaveFileAsync(
                L.T("选择 CD 输出位置"), "cd-image.wav", ".wav",
                L.T("WAV 音轨（推荐）|*.wav|RAW 音轨（旧格式）|*.raw|所有文件|*.*"));
            if (iso is not null)
            {
                State.CdOutputPath = iso;
            }
        }
        else
        {
            string? wav = await dialogs.PickSaveFileAsync(
                L.T("选择 CD 音轨输出位置"), "cd-track.wav", ".wav",
                L.T("WAV 音轨（推荐）|*.wav|RAW 音轨（旧格式）|*.raw|所有文件|*.*"));
            if (wav is not null)
            {
                State.CdOutputPath = wav;
            }
        }
    }

    [RelayCommand]
    private async Task AddMusicTracksAsync()
    {
        if (_shell.Dialogs is not { } dialogs)
        {
            return;
        }

        IReadOnlyList<string> files = await dialogs.PickOpenFilesAsync(
            L.T("添加音乐轨"),
            L.T("音频文件|*.wav;*.flac;*.mp3;*.m4a;*.aac;*.ogg;*.opus;*.aiff;*.wma|所有文件|*.*"));
        foreach (string path in files)
        {
            MusicTracks.Add(new MusicTrackViewModel(path, Path.GetFileNameWithoutExtension(path), RemoveMusicTrack));
        }
    }

    private void RemoveMusicTrack(MusicTrackViewModel track) => MusicTracks.Remove(track);

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        if (_shell.Dialogs is null && !_shell.IsProjectOpen)
        {
            return;
        }

        IsGenerating = true;
        StatusMessage = L.T("正在渲染设计合成图…");
        GenerateCommand.NotifyCanExecuteChanged();
        string composite = Path.Combine(
            Path.GetTempPath(), $"disc-image-studio-composite-{Guid.NewGuid():N}.png");
        try
        {
            _shell.Design.RenderCompositePng(CompositeSize, composite);
            string message = await Task.Run(() => GenerateCore(composite));
            StatusMessage = message;
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            try
            {
                File.Delete(composite);
            }
            catch (IOException)
            {
                // 临时图删不掉不影响产物。
            }

            IsGenerating = false;
            GenerateCommand.NotifyCanExecuteChanged();
        }
    }

    private string GenerateCore(string compositePath)
    {
        if (IsDvd)
        {
            DvdStreamingOptions options = new(
                checked((uint)ParameterParser.ParsePositiveLong(State.DvdTotalSectors, "DVD 总扇区数")),
                ParameterParser.ParsePositiveDouble(State.DvdInnerRadius, "DVD 内半径"),
                ParameterParser.ParsePositiveDouble(State.DvdOuterRadius, "DVD 外半径"),
                PitchLinear: ParameterParser.ParseDouble(State.DvdPitchLinear, "相对一次项"),
                PitchQuadratic: ParameterParser.ParseDouble(State.DvdPitchQuadratic, "相对二次项"),
                PitchCubic: ParameterParser.ParseDouble(State.DvdPitchCubic, "相对三次项"),
                UsableSectors: checked((uint)State.GetUsableSectors(OpticalDiscFamily.Dvd)),
                Tone: State.DvdTone,
                ReadCompatibility: State.DvdReadCompatibility);
            DvdImageRequest request = new(compositePath, RequirePath(State.DvdOutputPath, "请选择 DVD ISO 输出位置"),
                options, State.DvdDataDirectory.Trim(),
                string.IsNullOrWhiteSpace(State.DvdVolumeLabel) ? "DISC_IMAGE" : State.DvdVolumeLabel.Trim(),
                !string.IsNullOrWhiteSpace(State.DvdDataDirectory) && State.DvdFilesAtOuterRing);
            DiscImageResult result = DvdImageGenerator.Generate(request);
            return L.F($"DVD 完成：{result.OutputPath}");
        }

        CdDiscParameters parameters = new(
            ParameterParser.ParsePositiveDouble(State.CdInnerRadius, "CD 内半径"),
            ParameterParser.ParsePositiveDouble(State.CdOuterRadius, "CD 外半径"),
            ParameterParser.ParsePositiveLong(State.CdSectors, "CD 总扇区数"),
            PitchLinear: ParameterParser.ParseDouble(State.CdPitchLinear, "相对一次项"),
            PitchQuadratic: ParameterParser.ParseDouble(State.CdPitchQuadratic, "相对二次项"),
            PitchCubic: ParameterParser.ParseDouble(State.CdPitchCubic, "相对三次项"),
            UsableSectors: State.GetUsableSectors(OpticalDiscFamily.CompactDisc));

        if (State.HasCdFiles)
        {
            CdImageRequest dataRequest = new(compositePath, RequirePath(State.CdOutputPath, "请选择 CD ISO 输出位置"),
                parameters, State.Tone, State.CdInterleave, State.CdWriteCue,
                State.CdDataDirectory.Trim(),
                string.IsNullOrWhiteSpace(State.CdVolumeLabel) ? "CD_IMAGE" : State.CdVolumeLabel.Trim(),
                State.CdFilesAtOuterRing);
            DiscImageResult dataResult = CdImageGenerator.Generate(dataRequest);
            return L.F($"CD 数据盘完成：{dataResult.OutputPath}");
        }

        if (MusicTracks.Count > 0)
        {
            string output = RequirePath(State.CdOutputPath, "请选择 CD 音轨输出位置");
            CdMultiTrackRequest multiRequest = new(
                compositePath,
                output,
                [.. MusicTracks.Select(track => new CdMusicTrack(track.FilePath, track.Title))],
                parameters,
                State.Tone,
                State.CdInterleave,
                State.CdWriteCue);
            CdMultiTrackSummary summary = CdMultiTrackGenerator.Generate(multiRequest);
            return L.F($"音乐 CD 完成：{MusicTracks.Count} 条音乐轨 + 1 条图案轨，输出 {summary.OutputWave}");
        }

        CdImageRequest singleRequest = new(compositePath,
            RequirePath(State.CdOutputPath, "请选择 CD 音轨输出位置"),
            parameters, State.Tone, State.CdInterleave, State.CdWriteCue);
        DiscImageResult singleResult = CdImageGenerator.Generate(singleRequest);
        return L.F($"CD 音轨完成：{singleResult.OutputPath}");
    }

    [RelayCommand]
    private void GoNext() => _shell.NavigateTo(Workspace.Burn);

    [RelayCommand]
    private void GoDesign() => _shell.NavigateTo(Workspace.Design);

    [RelayCommand]
    private async Task OpenProjectSettingsAsync()
    {
        if (_shell.Dialogs is not { } dialogs || _shell.CurrentProject is not { } project)
        {
            return;
        }

        ProjectSettingsViewModel settings = new(_shell.State, project.Family, project.Name, () =>
        {
            _shell.Design.RefreshGeometry();
            Refresh();
        });
        await dialogs.ShowProjectSettingsAsync(settings);
    }

    internal void Refresh()
    {
        OnPropertyChanged(nameof(IsCd));
        OnPropertyChanged(nameof(IsDvd));
        OnPropertyChanged(nameof(IsBluRay));
        OnPropertyChanged(nameof(HasLayers));
        OnPropertyChanged(nameof(FamilyLabel));
        OnPropertyChanged(nameof(GenerateButtonText));
        OnPropertyChanged(nameof(SourceSummary));
        OnPropertyChanged(nameof(IsMusicMode));
        OnPropertyChanged(nameof(Presets));
        MatchPresetFromState();
        OnPropertyChanged(nameof(CanGenerate));
        GenerateCommand.NotifyCanExecuteChanged();
    }

    private static string RequirePath(string raw, string message)
    {
        string value = raw.Trim();
        return value.Length == 0 ? throw new ArgumentException(message) : value;
    }
}

/// <summary>一条音乐轨：源文件 + 显示名 + 移除命令。</summary>
public sealed class MusicTrackViewModel
{
    public MusicTrackViewModel(string filePath, string title, Action<MusicTrackViewModel> remove)
    {
        FilePath = filePath;
        Title = title;
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public string FilePath { get; }

    public string Title { get; }

    public ICommand RemoveCommand { get; }
}
