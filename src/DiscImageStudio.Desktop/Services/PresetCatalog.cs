using System.Globalization;
using DiscImageStudio.Core;

namespace DiscImageStudio.Desktop.Services;

/// <summary>一条盘片预设，三种盘型统一成一个形状，供下拉框绑定。</summary>
public sealed record DiscPresetItem(
    string Id,
    string DisplayName,
    string Description,
    OpticalDiscFamily Family,
    long Sectors,
    long? UsableSectors,
    double InnerRadiusMm,
    double OuterRadiusMm,
    double PitchLinear,
    double PitchQuadratic,
    double PitchCubic,
    bool InvertMapping)
{
    public string Summary =>
        string.Create(CultureInfo.InvariantCulture,
            $"{InnerRadiusMm:0.###}–{OuterRadiusMm:0.###} mm · {Sectors:N0} 扇区");
}

/// <summary>内置盘片预设（Core 内嵌的 built-in-disc-presets.json），按盘型分组。</summary>
public static class PresetCatalog
{
    private static readonly Lazy<DiscPresetJsonDocument> Document =
        new(DiscPresetJsonStore.LoadBuiltIn);

    public static IReadOnlyList<DiscPresetItem> All { get; } = Build();

    public static IReadOnlyList<DiscPresetItem> For(OpticalDiscFamily family) =>
        [.. All.Where(item => item.Family == family)];

    public static DiscPresetItem? Find(OpticalDiscFamily family, string id) =>
        All.FirstOrDefault(item => item.Family == family && item.Id == id);

    public static DiscPresetItem Default(OpticalDiscFamily family) =>
        For(family).First();

    private static List<DiscPresetItem> Build()
    {
        DiscPresetJsonDocument document = Document.Value;
        HashSet<string> disabledCd = [.. document.DisabledCdPresetIds ?? []];
        HashSet<string> disabledDvd = [.. document.DisabledDvdPresetIds ?? []];
        HashSet<string> disabledBd = [.. document.DisabledBluRayPresetIds ?? []];

        List<DiscPresetItem> result = [];
        foreach (CdDiscPresetDefinition preset in document.CdPresets)
        {
            if (disabledCd.Contains(preset.Id))
            {
                continue;
            }

            result.Add(new DiscPresetItem(preset.Id, preset.DisplayName, preset.Description,
                OpticalDiscFamily.CompactDisc, preset.Sectors, preset.UsableSectors,
                preset.InnerRadiusMm, preset.OuterRadiusMm,
                preset.PitchLinear, preset.PitchQuadratic, preset.PitchCubic, false));
        }

        foreach (DvdDiscPresetDefinition preset in document.DvdPresets)
        {
            if (disabledDvd.Contains(preset.Id))
            {
                continue;
            }

            result.Add(new DiscPresetItem(preset.Id, preset.DisplayName, preset.Description,
                OpticalDiscFamily.Dvd, preset.TotalSectors, preset.UsableSectors,
                preset.InnerRadiusMm, preset.OuterRadiusMm,
                preset.PitchLinear, preset.PitchQuadratic, preset.PitchCubic, false));
        }

        foreach (BluRayDiscPresetDefinition preset in document.BluRayPresets ?? [])
        {
            if (disabledBd.Contains(preset.Id))
            {
                continue;
            }

            result.Add(new DiscPresetItem(preset.Id, preset.DisplayName, preset.Description,
                OpticalDiscFamily.BluRay, preset.TotalSectors, preset.UsableSectors,
                preset.InnerRadiusMm, preset.OuterRadiusMm,
                preset.PitchLinear, preset.PitchQuadratic, preset.PitchCubic, preset.InvertMapping));
        }

        return result;
    }
}
