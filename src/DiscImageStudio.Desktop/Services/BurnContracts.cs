namespace DiscImageStudio.Desktop.Services;

public enum BurnMediaKind
{
    CdAudio,
    CdData,
    DvdData,
    BluRayData,
}

public sealed record BurnDevice(string Id, string DisplayName);

public sealed record BurnMediaInfo(
    BurnMediaKind? Kind,
    string Description,
    bool IsBlank,
    long TotalSectors,
    long WritableSectors);

public sealed record BurnProgress(string Stage, string Message, double Fraction)
{
    public static BurnProgress Indeterminate(string stage, string message) => new(stage, message, double.NaN);
}

public sealed record BurnJob(
    string DeviceId,
    BurnMediaKind Kind,
    string SourcePath,
    double? SpeedMultiplier = null);

/// <summary>刻录后端的共同形状：Windows 走 IMAPI，Linux 走 cdrecord / growisofs。</summary>
public interface IDiscBurner
{
    string BackendName { get; }

    Task<IReadOnlyList<BurnDevice>> GetDevicesAsync(CancellationToken cancellationToken = default);

    Task<BurnMediaInfo?> GetMediaAsync(string deviceId, CancellationToken cancellationToken = default);

    Task BurnAsync(BurnJob job, IProgress<BurnProgress>? progress = null, CancellationToken cancellationToken = default);
}

public static class BurnerFactory
{
    public static IDiscBurner Create()
    {
#if WINDOWS
        return new ImapiDiscBurner();
#else
        return new LinuxOpticalBurner();
#endif
    }
}
