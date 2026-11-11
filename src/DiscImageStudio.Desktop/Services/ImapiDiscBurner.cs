#if WINDOWS
using DiscImageStudio.Burning;

namespace DiscImageStudio.Desktop.Services;

/// <summary>Windows 刻录后端：包装原有 IMAPI2 实现。</summary>
public sealed class ImapiDiscBurner : IDiscBurner
{
    private readonly WindowsImapiBurner _inner = new();

    public string BackendName => "Windows IMAPI2";

    public async Task<IReadOnlyList<BurnDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<OpticalBurnDevice> devices = await _inner.GetDevicesAsync(cancellationToken);
        return [.. devices.Select(device => new BurnDevice(device.Id, device.DisplayName))];
    }

    public async Task<BurnMediaInfo?> GetMediaAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        try
        {
            OpticalMediaCapacity capacity = await _inner.GetMediaCapacityAsync(
                deviceId, OpticalBurnMediaKind.DvdData, cancellationToken);
            return new BurnMediaInfo(null, "已装入介质", true, capacity.TotalSectors, capacity.WritableSectors);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task BurnAsync(BurnJob job, IProgress<BurnProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        OpticalBurnMediaKind kind = job.Kind switch
        {
            BurnMediaKind.CdAudio => OpticalBurnMediaKind.CdAudio,
            BurnMediaKind.CdData => OpticalBurnMediaKind.CdData,
            BurnMediaKind.DvdData => OpticalBurnMediaKind.DvdData,
            BurnMediaKind.BluRayData => OpticalBurnMediaKind.BluRayData,
            _ => throw new ArgumentOutOfRangeException(nameof(job), job.Kind, "未知的刻录类型。"),
        };

        string path = job.SourcePath;
        long contentLength = new FileInfo(path).Length;
        bool stripWaveHeader = kind == OpticalBurnMediaKind.CdAudio
            && Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase);
        if (stripWaveHeader)
        {
            contentLength -= 44;
        }

        OpticalBurnRequest request = new(
            job.DeviceId,
            kind,
            contentLength,
            (stream, token) =>
            {
                using FileStream source = File.OpenRead(path);
                if (stripWaveHeader)
                {
                    source.Seek(44, SeekOrigin.Begin);
                }

                source.CopyTo(stream);
            });

        IProgress<OpticalBurnProgress>? inner = progress is null
            ? null
            : new Progress<OpticalBurnProgress>(value =>
                progress.Report(new BurnProgress(value.Stage, value.Message, value.Fraction)));
        await _inner.BurnAsync(request, inner, cancellationToken);
        progress?.Report(new BurnProgress("done", "刻录完成。", 1.0));
    }
}
#endif
