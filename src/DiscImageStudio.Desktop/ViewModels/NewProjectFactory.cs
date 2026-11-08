using System.Globalization;
using DiscImageStudio.Core;
using DiscImageStudio.Desktop.Services;

namespace DiscImageStudio.Desktop.ViewModels;

/// <summary>
/// 新工程的内存文档：几何取自内置预设（cd-80 / dvd-5 / bd-r-sl-25gb）。
/// 落盘与新建向导随后续迁移补齐。
/// </summary>
internal static class NewProjectFactory
{
    public static ProjectDocument Create(OpticalDiscFamily family, string name)
    {
        DiscPresetItem preset = PresetCatalog.Default(family);
        return Create(family, name, preset);
    }

    public static ProjectDocument Create(OpticalDiscFamily family, string name, DiscPresetItem preset)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ProjectGeometry geometry = new(
            preset.InnerRadiusMm,
            preset.OuterRadiusMm,
            preset.Sectors,
            preset.PitchLinear,
            preset.PitchQuadratic,
            preset.PitchCubic,
            preset.UsableSectors);
        return family switch
        {
            OpticalDiscFamily.Dvd => new ProjectDocument(
                ProjectJsonStore.CurrentSchemaVersion, name, ProjectDocument.DvdFamily,
                geometry, [], DefaultOutputs("DISC_IMAGE", ".iso"), null, null, now, now),
            OpticalDiscFamily.BluRay => new ProjectDocument(
                ProjectJsonStore.CurrentSchemaVersion, name, ProjectDocument.BluRayFamily,
                geometry, [], DefaultOutputs("BD_IMAGE", ".iso"), null, null, now, now,
                BluRay: new ProjectBluRaySettings(preset.InvertMapping)),
            _ => new ProjectDocument(
                ProjectJsonStore.CurrentSchemaVersion, name, ProjectDocument.CdFamily,
                geometry, [], DefaultOutputs("CD_IMAGE", ".wav"), null, null, now, now),
        };
    }

    public static string FamilyLabel(OpticalDiscFamily family) => family switch
    {
        OpticalDiscFamily.Dvd => "DVD",
        OpticalDiscFamily.BluRay => "BD",
        _ => "CD",
    };

    private static ProjectOutputs DefaultOutputs(string volumeLabel, string extension)
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "DiscImageStudio");
        return new ProjectOutputs(
            Path.Combine(directory, $"image{extension}"),
            WriteCue: true,
            DataDirectory: string.Empty,
            VolumeLabel: volumeLabel,
            Interleave: true);
    }
}
