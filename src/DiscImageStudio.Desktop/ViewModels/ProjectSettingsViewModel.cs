using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscImageStudio.Core;
using DiscImageStudio.Desktop.Services;
using DiscImageStudio.Localization;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Desktop.ViewModels;

/// <summary>工程设置对话框：编辑当前盘型的几何参数，保存后写回共享状态。</summary>
public partial class ProjectSettingsViewModel : ObservableObject
{
    private readonly DiscParametersState _state;
    private readonly OpticalDiscFamily _family;
    private readonly Action _onSaved;

    public ProjectSettingsViewModel(DiscParametersState state, OpticalDiscFamily family, string name, Action saved)
    {
        _state = state;
        _family = family;
        _onSaved = saved;
        Presets = PresetCatalog.For(family);
        _name = name;
        switch (family)
        {
            case OpticalDiscFamily.Dvd:
                (_innerRadius, _outerRadius, _sectors, _usableSectors, _pitchLinear, _pitchQuadratic, _pitchCubic) =
                    (state.DvdInnerRadius, state.DvdOuterRadius, state.DvdTotalSectors, state.DvdUsableSectors,
                        state.DvdPitchLinear, state.DvdPitchQuadratic, state.DvdPitchCubic);
                break;
            case OpticalDiscFamily.BluRay:
                (_innerRadius, _outerRadius, _sectors, _usableSectors, _pitchLinear, _pitchQuadratic, _pitchCubic) =
                    (state.BdInnerRadius, state.BdOuterRadius, state.BdTotalSectors, state.BdUsableSectors,
                        state.BdPitchLinear, state.BdPitchQuadratic, state.BdPitchCubic);
                break;
            default:
                (_innerRadius, _outerRadius, _sectors, _usableSectors, _pitchLinear, _pitchQuadratic, _pitchCubic) =
                    (state.CdInnerRadius, state.CdOuterRadius, state.CdSectors, state.CdUsableSectors,
                        state.CdPitchLinear, state.CdPitchQuadratic, state.CdPitchCubic);
                break;
        }
    }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _innerRadius;

    [ObservableProperty]
    private string _outerRadius;

    [ObservableProperty]
    private string _sectors;

    [ObservableProperty]
    private string _usableSectors;

    [ObservableProperty]
    private string _pitchLinear;

    [ObservableProperty]
    private string _pitchQuadratic;

    [ObservableProperty]
    private string _pitchCubic;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>当前盘型的预设（内置 + 用户层）。</summary>
    public IReadOnlyList<DiscPresetItem> Presets { get; private set; }

    [ObservableProperty]
    private DiscPresetItem? _selectedPreset;

    partial void OnSelectedPresetChanged(DiscPresetItem? value)
    {
        if (value is null)
        {
            return;
        }

        // 选预设只是把几何填进对话框，保存时才会写回工程。
        string Number(double number) => number.ToString("R", CultureInfo.InvariantCulture);
        _innerRadius = Number(value.InnerRadiusMm);
        _outerRadius = Number(value.OuterRadiusMm);
        _sectors = value.Sectors.ToString(CultureInfo.InvariantCulture);
        _usableSectors = value.UsableSectors?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        _pitchLinear = Number(value.PitchLinear);
        _pitchQuadratic = Number(value.PitchQuadratic);
        _pitchCubic = Number(value.PitchCubic);
        OnPropertyChanged(nameof(InnerRadius));
        OnPropertyChanged(nameof(OuterRadius));
        OnPropertyChanged(nameof(Sectors));
        OnPropertyChanged(nameof(UsableSectors));
        OnPropertyChanged(nameof(PitchLinear));
        OnPropertyChanged(nameof(PitchQuadratic));
        OnPropertyChanged(nameof(PitchCubic));
        OnPropertyChanged(nameof(CanDeletePreset));
    }

    public bool CanDeletePreset => SelectedPreset?.IsUserPreset == true;

    [ObservableProperty]
    private string _newPresetName = string.Empty;

    [ObservableProperty]
    private string _presetMessage = string.Empty;

    [RelayCommand]
    private void SaveAsPreset()
    {
        if (string.IsNullOrWhiteSpace(NewPresetName))
        {
            PresetMessage = "先给预设起个名字。";
            return;
        }

        if (!TryParsePositive(InnerRadius, out double inner) || !TryParsePositive(OuterRadius, out double outer)
            || !long.TryParse(Sectors.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long sectors))
        {
            PresetMessage = "半径和扇区数都填对之后再保存。";
            return;
        }

        long? usable = UsableSectors.Trim().Length > 0
            && long.TryParse(UsableSectors.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
                ? parsed
                : null;
        double Number(string text) =>
            double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? value
                : 0;

        PresetCatalog.AddUserPreset(_family, NewPresetName.Trim(), inner, outer, sectors, usable,
            Number(PitchLinear), Number(PitchQuadratic), Number(PitchCubic), false);
        Presets = PresetCatalog.For(_family);
        OnPropertyChanged(nameof(Presets));
        PresetMessage = L.F($"已保存预设「{NewPresetName.Trim()}」。");
        NewPresetName = string.Empty;
    }

    [RelayCommand]
    private void DeletePreset()
    {
        if (SelectedPreset is not { IsUserPreset: true } preset)
        {
            PresetMessage = "内置预设不能删除。";
            return;
        }

        if (PresetCatalog.RemoveUserPreset(_family, preset.Id))
        {
            Presets = PresetCatalog.For(_family);
            OnPropertyChanged(nameof(Presets));
            SelectedPreset = null;
            PresetMessage = L.F($"已删除「{preset.DisplayName}」。");
        }
    }

    public string FamilyLabel => _family switch
    {
        OpticalDiscFamily.Dvd => "DVD 工程",
        OpticalDiscFamily.BluRay => "蓝光工程",
        _ => "CD 工程",
    };

    [ObservableProperty]
    private bool _saved;

    [RelayCommand]
    private void Save()
    {
        if (!TryParsePositive(InnerRadius, out double inner) || !TryParsePositive(OuterRadius, out double outer))
        {
            ErrorMessage = "内/外半径必须是正数。";
            return;
        }

        if (outer <= inner)
        {
            ErrorMessage = "外半径必须大于内半径。";
            return;
        }

        if (!long.TryParse(Sectors.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long sectors)
            || sectors <= 0)
        {
            ErrorMessage = "总扇区数必须是正整数。";
            return;
        }

        if (!TryParseNumber(PitchLinear) || !TryParseNumber(PitchQuadratic) || !TryParseNumber(PitchCubic))
        {
            ErrorMessage = "轨距系数必须是数字。";
            return;
        }

        if (UsableSectors.Trim().Length > 0
            && (!long.TryParse(UsableSectors.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long usable)
                || usable <= 0 || usable > sectors))
        {
            ErrorMessage = "可用扇区必须是不超过总数的正整数，或者留空。";
            return;
        }

        ErrorMessage = string.Empty;
        string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        string Count(long value) => value.ToString(CultureInfo.InvariantCulture);

        switch (_family)
        {
            case OpticalDiscFamily.Dvd:
                _state.DvdInnerRadius = Number(inner);
                _state.DvdOuterRadius = Number(outer);
                _state.DvdTotalSectors = Count(sectors);
                _state.DvdUsableSectors = UsableSectors.Trim();
                _state.DvdPitchLinear = Number(double.Parse(PitchLinear.Trim(), CultureInfo.InvariantCulture));
                _state.DvdPitchQuadratic = Number(double.Parse(PitchQuadratic.Trim(), CultureInfo.InvariantCulture));
                _state.DvdPitchCubic = Number(double.Parse(PitchCubic.Trim(), CultureInfo.InvariantCulture));
                break;
            case OpticalDiscFamily.BluRay:
                _state.BdInnerRadius = Number(inner);
                _state.BdOuterRadius = Number(outer);
                _state.BdTotalSectors = Count(sectors);
                _state.BdUsableSectors = UsableSectors.Trim();
                _state.BdPitchLinear = Number(double.Parse(PitchLinear.Trim(), CultureInfo.InvariantCulture));
                _state.BdPitchQuadratic = Number(double.Parse(PitchQuadratic.Trim(), CultureInfo.InvariantCulture));
                _state.BdPitchCubic = Number(double.Parse(PitchCubic.Trim(), CultureInfo.InvariantCulture));
                break;
            default:
                _state.CdInnerRadius = Number(inner);
                _state.CdOuterRadius = Number(outer);
                _state.CdSectors = Count(sectors);
                _state.CdUsableSectors = UsableSectors.Trim();
                _state.CdPitchLinear = Number(double.Parse(PitchLinear.Trim(), CultureInfo.InvariantCulture));
                _state.CdPitchQuadratic = Number(double.Parse(PitchQuadratic.Trim(), CultureInfo.InvariantCulture));
                _state.CdPitchCubic = Number(double.Parse(PitchCubic.Trim(), CultureInfo.InvariantCulture));
                break;
        }

        Saved = true;
        _onSaved();
    }

    private static bool TryParsePositive(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value) && value > 0;

    private static bool TryParseNumber(string text) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
        && double.IsFinite(value);
}
