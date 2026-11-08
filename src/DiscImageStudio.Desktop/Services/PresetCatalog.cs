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
    bool InvertMapping,
    bool IsUserPreset)
{
    public string Summary =>
        string.Create(CultureInfo.InvariantCulture,
            $"{InnerRadiusMm:0.###}–{OuterRadiusMm:0.###} mm · {Sectors:N0} 扇区");
}

/// <summary>盘片预设：内置层（Core 内嵌 JSON）+ 用户层（%LOCALAPPDATA%/DiscImageStudio/disc-presets.json），按 WPF 版的合并规则读取。</summary>
public static class PresetCatalog
{
    private static readonly object Gate = new();
    private static DiscPresetJsonDocument _userDocument = DiscPresetJsonStore.CreateInitialUserDocument();
    private static IReadOnlyList<DiscPresetItem> _all = Build();

    public static string UserPresetPath { get; } = DiscPresetJsonStore.GetDefaultPath();

    public static IReadOnlyList<DiscPresetItem> All
    {
        get { lock (Gate) { return _all; } }
    }

    public static IReadOnlyList<DiscPresetItem> For(OpticalDiscFamily family) =>
        [.. All.Where(item => item.Family == family)];

    public static DiscPresetItem? Find(OpticalDiscFamily family, string id) =>
        All.FirstOrDefault(item => item.Family == family && item.Id == id);

    public static DiscPresetItem Default(OpticalDiscFamily family) => For(family).First();

    /// <summary>重新读用户层并合并；保存或删除预设后调用。</summary>
    public static void Reload()
    {
        lock (Gate)
        {
            _userDocument = DiscPresetJsonStore.LoadOrCreate(
                UserPresetPath, DiscPresetJsonStore.CreateInitialUserDocument());
            _all = Build();
        }
    }

    /// <summary>把一套参数追加进用户层（同名覆盖由调用方决定），返回新预设 id。</summary>
    public static string AddUserPreset(
        OpticalDiscFamily family, string name,
        double innerRadiusMm, double outerRadiusMm, long sectors, long? usableSectors,
        double pitchLinear, double pitchQuadratic, double pitchCubic, bool invertMapping)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (Gate)
        {
            string id = $"user-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..40];
            DiscPresetJsonDocument current = _userDocument;
            _userDocument = family switch
            {
                OpticalDiscFamily.Dvd => current with
                {
                    DvdPresets =
                    [
                        .. current.DvdPresets,
                        new DvdDiscPresetDefinition(id, name, "用户保存的 DVD 参数。",
                            checked((uint)sectors), innerRadiusMm, outerRadiusMm,
                            PitchLinear: pitchLinear, PitchQuadratic: pitchQuadratic, PitchCubic: pitchCubic,
                            UsableSectors: usableSectors is null ? null : checked((uint)usableSectors.Value)),
                    ],
                },
                OpticalDiscFamily.BluRay => current with
                {
                    BluRayPresets =
                    [
                        .. current.BluRayPresets ?? [],
                        new BluRayDiscPresetDefinition(id, name, "用户保存的蓝光参数。",
                            checked((uint)sectors), innerRadiusMm, outerRadiusMm, invertMapping,
                            PitchLinear: pitchLinear, PitchQuadratic: pitchQuadratic, PitchCubic: pitchCubic,
                            UsableSectors: usableSectors is null ? null : checked((uint)usableSectors.Value)),
                    ],
                },
                _ => current with
                {
                    CdPresets =
                    [
                        .. current.CdPresets,
                        new CdDiscPresetDefinition(id, name, "用户保存的 CD 参数。",
                            sectors, innerRadiusMm, outerRadiusMm,
                            PitchLinear: pitchLinear, PitchQuadratic: pitchQuadratic, PitchCubic: pitchCubic,
                            UsableSectors: usableSectors),
                    ],
                },
            };
            DiscPresetJsonStore.Save(UserPresetPath, _userDocument);
            _all = Build();
            return id;
        }
    }

    /// <summary>用新参数覆盖一条用户预设（同 id 替换）；内置预设不能被覆盖，返回 false。</summary>
    public static bool UpdateUserPreset(
        OpticalDiscFamily family, string id, string name,
        double innerRadiusMm, double outerRadiusMm, long sectors, long? usableSectors,
        double pitchLinear, double pitchQuadratic, double pitchCubic, bool invertMapping)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (Gate)
        {
            DiscPresetJsonDocument current = _userDocument;
            DiscPresetJsonDocument updated;
            bool hit = false;
            switch (family)
            {
                case OpticalDiscFamily.Dvd:
                {
                    List<DvdDiscPresetDefinition> items = [];
                    foreach (DvdDiscPresetDefinition item in current.DvdPresets)
                    {
                        if (item.Id == id)
                        {
                            hit = true;
                            items.Add(new DvdDiscPresetDefinition(item.Id, name, item.Description,
                                checked((uint)sectors), innerRadiusMm, outerRadiusMm, item.ChannelBitLengthNm,
                                item.DisplayNameResourceKey, item.DescriptionResourceKey,
                                pitchLinear, pitchQuadratic, pitchCubic, item.Contributor,
                                usableSectors is null ? null : checked((uint)usableSectors.Value)));
                        }
                        else
                        {
                            items.Add(item);
                        }
                    }

                    updated = current with { DvdPresets = items };
                    break;
                }

                case OpticalDiscFamily.BluRay:
                {
                    List<BluRayDiscPresetDefinition> items = [];
                    foreach (BluRayDiscPresetDefinition item in current.BluRayPresets ?? [])
                    {
                        if (item.Id == id)
                        {
                            hit = true;
                            items.Add(new BluRayDiscPresetDefinition(item.Id, name, item.Description,
                                checked((uint)sectors), innerRadiusMm, outerRadiusMm, invertMapping,
                                item.DisplayNameResourceKey, item.DescriptionResourceKey, item.Contributor,
                                pitchLinear, pitchQuadratic, pitchCubic,
                                usableSectors is null ? null : checked((uint)usableSectors.Value)));
                        }
                        else
                        {
                            items.Add(item);
                        }
                    }

                    updated = current with { BluRayPresets = items };
                    break;
                }

                default:
                {
                    List<CdDiscPresetDefinition> items = [];
                    foreach (CdDiscPresetDefinition item in current.CdPresets)
                    {
                        if (item.Id == id)
                        {
                            hit = true;
                            items.Add(new CdDiscPresetDefinition(item.Id, name, item.Description,
                                sectors, innerRadiusMm, outerRadiusMm, item.LinearVelocityMmPerSecond,
                                item.DisplayNameResourceKey, item.DescriptionResourceKey, item.Contributor,
                                pitchLinear, pitchQuadratic, pitchCubic, usableSectors));
                        }
                        else
                        {
                            items.Add(item);
                        }
                    }

                    updated = current with { CdPresets = items };
                    break;
                }
            }

            if (!hit)
            {
                return false;
            }

            _userDocument = updated;
            DiscPresetJsonStore.Save(UserPresetPath, _userDocument);
            _all = Build();
            return true;
        }
    }

    /// <summary>只删用户层里的预设；内置预设删不掉，返回是否删除成功。</summary>
    public static bool RemoveUserPreset(OpticalDiscFamily family, string id)
    {
        lock (Gate)
        {
            DiscPresetJsonDocument current = _userDocument;
            bool removed = family switch
            {
                OpticalDiscFamily.Dvd => RemoveFrom(ref current, current.DvdPresets, id,
                    value => current with { DvdPresets = value }),
                OpticalDiscFamily.BluRay => RemoveFrom(ref current, current.BluRayPresets ?? [], id,
                    value => current with { BluRayPresets = value }),
                _ => RemoveFrom(ref current, current.CdPresets, id,
                    value => current with { CdPresets = value }),
            };
            if (!removed)
            {
                return false;
            }

            _userDocument = current;
            DiscPresetJsonStore.Save(UserPresetPath, _userDocument);
            _all = Build();
            return true;

            static bool RemoveFrom<T>(ref DiscPresetJsonDocument document, IReadOnlyList<T> presets, string id,
                Func<IReadOnlyList<T>, DiscPresetJsonDocument> replace)
            {
                List<T> remaining = [.. presets.Where(item => item switch
                {
                    CdDiscPresetDefinition cd => cd.Id != id,
                    DvdDiscPresetDefinition dvd => dvd.Id != id,
                    BluRayDiscPresetDefinition bd => bd.Id != id,
                    _ => true,
                })];
                if (remaining.Count == presets.Count)
                {
                    return false;
                }

                document = replace(remaining);
                return true;
            }
        }
    }

    private static List<DiscPresetItem> Build()
    {
        DiscPresetJsonDocument builtIn = DiscPresetJsonStore.LoadBuiltIn();
        DiscPresetJsonDocument merged = DiscPresetJsonStore.Merge(builtIn, _userDocument);
        HashSet<string> userCd = [.. _userDocument.CdPresets.Select(value => value.Id)];
        HashSet<string> userDvd = [.. _userDocument.DvdPresets.Select(value => value.Id)];
        HashSet<string> userBd = [.. (_userDocument.BluRayPresets ?? []).Select(value => value.Id)];

        List<DiscPresetItem> result = [];
        foreach (CdDiscPresetDefinition preset in merged.CdPresets)
        {
            result.Add(new DiscPresetItem(preset.Id, preset.DisplayName, preset.Description,
                OpticalDiscFamily.CompactDisc, preset.Sectors, preset.UsableSectors,
                preset.InnerRadiusMm, preset.OuterRadiusMm,
                preset.PitchLinear, preset.PitchQuadratic, preset.PitchCubic, false, userCd.Contains(preset.Id)));
        }

        foreach (DvdDiscPresetDefinition preset in merged.DvdPresets)
        {
            result.Add(new DiscPresetItem(preset.Id, preset.DisplayName, preset.Description,
                OpticalDiscFamily.Dvd, preset.TotalSectors, preset.UsableSectors,
                preset.InnerRadiusMm, preset.OuterRadiusMm,
                preset.PitchLinear, preset.PitchQuadratic, preset.PitchCubic, false, userDvd.Contains(preset.Id)));
        }

        foreach (BluRayDiscPresetDefinition preset in merged.BluRayPresets ?? [])
        {
            result.Add(new DiscPresetItem(preset.Id, preset.DisplayName, preset.Description,
                OpticalDiscFamily.BluRay, preset.TotalSectors, preset.UsableSectors,
                preset.InnerRadiusMm, preset.OuterRadiusMm,
                preset.PitchLinear, preset.PitchQuadratic, preset.PitchCubic,
                preset.InvertMapping, userBd.Contains(preset.Id)));
        }

        return result;
    }
}
