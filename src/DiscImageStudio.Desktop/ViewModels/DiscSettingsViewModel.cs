using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscImageStudio.Core;
using DiscImageStudio.Desktop.Services;
using DiscImageStudio.Localization;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Desktop.ViewModels;

/// <summary>盘片设置：预设选择与管理 + 几何参数（半径、扇区、三项轨距）。保存后写回共享状态。</summary>
public partial class DiscSettingsViewModel : ObservableObject
{
    private readonly DiscParametersState _state;
    private readonly OpticalDiscFamily _family;
    private readonly Action _onSaved;

    public DiscSettingsViewModel(DiscParametersState state, OpticalDiscFamily family, Action saved)
    {
        _state = state;
        _family = family;
        _onSaved = saved;
        Presets = PresetCatalog.For(family);
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

    public string FamilyLabel => _family switch
    {
        OpticalDiscFamily.Dvd => "DVD 工程",
        OpticalDiscFamily.BluRay => "蓝光工程",
        _ => "CD 工程",
    };

    public IReadOnlyList<DiscPresetItem> Presets { get; private set; }

    [ObservableProperty]
    private DiscPresetItem? _selectedPreset;

    partial void OnSelectedPresetChanged(DiscPresetItem? value)
    {
        if (value is null)
        {
            return;
        }

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
        OnPropertyChanged(nameof(CanManagePreset));
    }

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

    [ObservableProperty]
    private string _newPresetName = string.Empty;

    [ObservableProperty]
    private string _presetMessage = string.Empty;

    [ObservableProperty]
    private bool _saved;

    public bool CanManagePreset => SelectedPreset?.IsUserPreset == true;

    [RelayCommand]
    private void SaveAsPreset()
    {
        if (string.IsNullOrWhiteSpace(NewPresetName))
        {
            PresetMessage = "先给预设起个名字。";
            return;
        }

        if (!TryReadGeometry(out double inner, out double outer, out long sectors, out long? usable, out _))
        {
            return;
        }

        PresetCatalog.AddUserPreset(_family, NewPresetName.Trim(), inner, outer, sectors, usable,
            Parse(PitchLinear), Parse(PitchQuadratic), Parse(PitchCubic), _family == OpticalDiscFamily.BluRay);
        RefreshPresets();
        PresetMessage = L.F($"已保存预设「{NewPresetName.Trim()}」。");
        NewPresetName = string.Empty;
    }

    [RelayCommand]
    private void UpdatePreset()
    {
        if (SelectedPreset is not { IsUserPreset: true } preset)
        {
            PresetMessage = "只有用户预设可以更新。";
            return;
        }

        if (!TryReadGeometry(out double inner, out double outer, out long sectors, out long? usable, out _))
        {
            return;
        }

        string name = string.IsNullOrWhiteSpace(NewPresetName) ? preset.DisplayName : NewPresetName.Trim();
        if (!PresetCatalog.UpdateUserPreset(_family, preset.Id, name, inner, outer, sectors, usable,
                Parse(PitchLinear), Parse(PitchQuadratic), Parse(PitchCubic), preset.InvertMapping))
        {
            PresetMessage = "更新失败，预设可能已被删除。";
            return;
        }

        RefreshPresets();
        SelectedPreset = Presets.FirstOrDefault(item => item.Id == preset.Id);
        PresetMessage = L.F($"已更新「{name}」。");
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
            string removed = preset.DisplayName;
            RefreshPresets();
            SelectedPreset = null;
            PresetMessage = L.F($"已删除「{removed}」。");
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (!TryReadGeometry(out double inner, out double outer, out long sectors, out long? usable, out string? error))
        {
            ErrorMessage = error ?? "参数不完整。";
            return;
        }

        ErrorMessage = string.Empty;
        string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        string Count(long value) => value.ToString(CultureInfo.InvariantCulture);
        switch (_family)
        {
            case OpticalDiscFamily.Dvd:
                _state.DvdInnerRadius = Format(inner);
                _state.DvdOuterRadius = Format(outer);
                _state.DvdTotalSectors = Count(sectors);
                _state.DvdUsableSectors = UsableSectors.Trim();
                _state.DvdPitchLinear = Format(Parse(PitchLinear));
                _state.DvdPitchQuadratic = Format(Parse(PitchQuadratic));
                _state.DvdPitchCubic = Format(Parse(PitchCubic));
                break;
            case OpticalDiscFamily.BluRay:
                _state.BdInnerRadius = Format(inner);
                _state.BdOuterRadius = Format(outer);
                _state.BdTotalSectors = Count(sectors);
                _state.BdUsableSectors = UsableSectors.Trim();
                _state.BdPitchLinear = Format(Parse(PitchLinear));
                _state.BdPitchQuadratic = Format(Parse(PitchQuadratic));
                _state.BdPitchCubic = Format(Parse(PitchCubic));
                break;
            default:
                _state.CdInnerRadius = Format(inner);
                _state.CdOuterRadius = Format(outer);
                _state.CdSectors = Count(sectors);
                _state.CdUsableSectors = UsableSectors.Trim();
                _state.CdPitchLinear = Format(Parse(PitchLinear));
                _state.CdPitchQuadratic = Format(Parse(PitchQuadratic));
                _state.CdPitchCubic = Format(Parse(PitchCubic));
                break;
        }

        Saved = true;
        _onSaved();
    }

    private bool TryReadGeometry(out double inner, out double outer, out long sectors, out long? usable, out string? error)
    {
        error = null;
        inner = 0;
        outer = 0;
        sectors = 0;
        usable = null;
        if (!TryParsePositive(InnerRadius, out inner) || !TryParsePositive(OuterRadius, out outer))
        {
            error = "内/外半径必须是正数。";
            return false;
        }

        if (outer <= inner)
        {
            error = "外半径必须大于内半径。";
            return false;
        }

        if (!long.TryParse(Sectors.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out sectors)
            || sectors <= 0)
        {
            error = "总扇区数必须是正整数。";
            return false;
        }

        if (!double.TryParse(PitchLinear.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            || !double.TryParse(PitchQuadratic.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            || !double.TryParse(PitchCubic.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            error = "轨距系数必须是数字。";
            return false;
        }

        if (UsableSectors.Trim().Length > 0)
        {
            if (!long.TryParse(UsableSectors.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
                || value <= 0 || value > sectors)
            {
                error = "可用扇区必须是不超过总数的正整数，或者留空。";
                return false;
            }

            usable = value;
        }

        return true;
    }

    private void RefreshPresets()
    {
        Presets = PresetCatalog.For(_family);
        OnPropertyChanged(nameof(Presets));
        OnPropertyChanged(nameof(CanManagePreset));
    }

    private static double Parse(string text) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0;

    private static bool TryParsePositive(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value) && value > 0;
}
