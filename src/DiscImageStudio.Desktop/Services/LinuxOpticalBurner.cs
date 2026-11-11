using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace DiscImageStudio.Desktop.Services;

/// <summary>Linux 刻录后端：CD 音频走 cdrecord，数据盘（ISO）走 growisofs。</summary>
public sealed partial class LinuxOpticalBurner : IDiscBurner
{
    public string BackendName => "cdrecord / growisofs";

    public Task<IReadOnlyList<BurnDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        List<BurnDevice> devices = [];
        foreach (string path in Directory.GetFiles("/dev", "sr*"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // /dev/sr0、/dev/sr1…（跳过 /dev/sr0 之类的非块设备判断靠文件名即可，udev 下这里是设备节点）
            string name = Path.GetFileName(path);
            if (name.Length <= 2 || !char.IsDigit(name[^1]))
            {
                continue;
            }

            string? model = ReadBlockModel(path);
            devices.Add(new BurnDevice(path, model is null ? path : $"{path}  {model}"));
        }

        return Task.FromResult<IReadOnlyList<BurnDevice>>(devices);
    }

    public async Task<BurnMediaInfo?> GetMediaAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        (int exitCode, string output) = await RunAsync("dvd+rw-mediainfo", [deviceId], cancellationToken);
        if (output.Contains("non-DVD media mounted", StringComparison.Ordinal))
        {
            // dvd+rw-mediainfo 只认 DVD；CD 介质换 cdrecord -atip 兜底。
            return await ReadCdMediaAsync(deviceId, cancellationToken);
        }

        if (exitCode != 0 && output.Length == 0)
        {
            return null;
        }

        string? media = MediaRegex().Match(output) is { Success: true } match ? match.Groups[1].Value.Trim() : null;
        bool blank = output.Contains("Disc status:           blank", StringComparison.Ordinal)
            || output.Contains("Disc status: blank", StringComparison.Ordinal);
        long totalSectors = 0;
        Match legacy = LegacyLeadOutRegex().Match(output);
        if (legacy.Success && long.TryParse(legacy.Groups[1].Value, out long sectors))
        {
            totalSectors = sectors;
        }

        BurnMediaKind? kind = media switch
        {
            null => null,
            var value when value.Contains("DVD-R", StringComparison.OrdinalIgnoreCase)
                || value.Contains("DVD+R", StringComparison.OrdinalIgnoreCase)
                || value.Contains("DVD-RAM", StringComparison.OrdinalIgnoreCase) => BurnMediaKind.DvdData,
            var value when value.Contains("BD", StringComparison.OrdinalIgnoreCase) => BurnMediaKind.BluRayData,
            var value when value.Contains("CD-R", StringComparison.OrdinalIgnoreCase)
                || value.Contains("CD-RW", StringComparison.OrdinalIgnoreCase) => BurnMediaKind.CdAudio,
            _ => null,
        };

        return new BurnMediaInfo(kind, media ?? "未知介质", blank, totalSectors, totalSectors);
    }

    /// <summary>CD 介质：cdrecord -atip 能看出可擦写性和总扇区。</summary>
    private async Task<BurnMediaInfo?> ReadCdMediaAsync(string deviceId, CancellationToken cancellationToken)
    {
        (int exitCode, string output) = await RunAsync("cdrecord", ["-dev=" + deviceId, "-atip"], cancellationToken);
        if (exitCode != 0 && output.Length == 0)
        {
            return null;
        }

        bool erasable = output.Contains("Is erasable", StringComparison.Ordinal);
        long sectors = 0;
        Match leadOut = AtipLeadOutRegex().Match(output);
        if (leadOut.Success && long.TryParse(leadOut.Groups[1].Value, out long parsed))
        {
            sectors = parsed;
        }

        if (output.Contains("atip", StringComparison.OrdinalIgnoreCase) || sectors > 0 || erasable)
        {
            string description = erasable ? "CD-RW（可擦写）" : "CD-R";
            return new BurnMediaInfo(BurnMediaKind.CdAudio, description, true, sectors, sectors);
        }

        return new BurnMediaInfo(null, "无法识别的介质", false, 0, 0);
    }

    public async Task BurnAsync(BurnJob job, IProgress<BurnProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrWhiteSpace(job.DeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(job.SourcePath);
        if (!File.Exists(job.SourcePath))
        {
            throw new FileNotFoundException("找不到要刻录的文件。", job.SourcePath);
        }

        progress?.Report(BurnProgress.Indeterminate("prepare", "准备刻录…"));
        switch (job.Kind)
        {
            case BurnMediaKind.CdAudio:
                await BurnCdAudioAsync(job, progress, cancellationToken);
                break;
            case BurnMediaKind.DvdData:
            case BurnMediaKind.BluRayData:
            case BurnMediaKind.CdData:
                await BurnImageAsync(job, progress, cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(job), job.Kind, "未知的刻录类型。");
        }
    }

    /// <summary>CD-DA：cdrecord 直接吃 WAV（单轨），不需要 CUE。</summary>
    private static async Task BurnCdAudioAsync(BurnJob job, IProgress<BurnProgress>? progress,
        CancellationToken cancellationToken)
    {
        List<string> arguments = ["-v", "-audio", "-pad", $"dev={job.DeviceId}"];
        if (job.SpeedMultiplier is { } speed)
        {
            arguments.Add($"-speed={speed.ToString("0.#", CultureInfo.InvariantCulture)}");
        }

        arguments.Add(job.SourcePath);
        await RunStreamingAsync("cdrecord", arguments, progress, cancellationToken);
    }

    /// <summary>DVD / BD 数据盘：growisofs 写 ISO。</summary>
    private static async Task BurnImageAsync(BurnJob job, IProgress<BurnProgress>? progress,
        CancellationToken cancellationToken)
    {
        List<string> arguments = [];
        if (job.SpeedMultiplier is { } speed)
        {
            arguments.Add($"-speed={speed.ToString("0.#", CultureInfo.InvariantCulture)}");
        }

        if (job.Kind == BurnMediaKind.DvdData)
        {
            // DVD-R 尽量按 DVD-ROM 兼容方式收尾，读回兼容性更好。
            arguments.Add("-dvd-compat");
        }

        arguments.AddRange(["-Z", $"{job.DeviceId}={job.SourcePath}"]);
        await RunStreamingAsync("growisofs", arguments, progress, cancellationToken);
    }

    /// <summary>边跑边把标准输出/错误接到进度回调，结束时按退出码抛错。</summary>
    private static async Task RunStreamingAsync(string fileName, IReadOnlyList<string> arguments,
        IProgress<BurnProgress>? progress, CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"无法启动 {fileName}，请确认已安装。");

        object gate = new();
        double lastFraction = double.NaN;
        Task reader = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync(cancellationToken) is { } line)
            {
                double? fraction = ParseFraction(line);
                if (fraction is { } value)
                {
                    lock (gate)
                    {
                        lastFraction = value;
                    }

                    progress?.Report(new BurnProgress("write", line.Trim(), value));
                }
                else if (line.Length > 0 && progress is not null)
                {
                    progress.Report(BurnProgress.Indeterminate("write", line.Trim()));
                }
            }
        }, cancellationToken);

        Task output = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
            {
                if (line.Length > 0)
                {
                    progress?.Report(BurnProgress.Indeterminate("write", line.Trim()));
                }
            }
        }, cancellationToken);

        await Task.WhenAll(reader, output).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{fileName} 退出码 {process.ExitCode}，刻录没有完成。");
        }

        progress?.Report(new BurnProgress("done", "刻录完成。", 1.0));
    }

    /// <summary>从 cdrecord / growisofs 的输出里抓百分比。</summary>
    private static double? ParseFraction(string line)
    {
        Match percent = PercentRegex().Match(line);
        if (percent.Success && double.TryParse(percent.Groups[1].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double value))
        {
            return Math.Clamp(value / 100.0, 0, 1);
        }

        Match written = WrittenRegex().Match(line);
        if (written.Success
            && double.TryParse(written.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double done)
            && double.TryParse(written.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double total)
            && total > 0)
        {
            return Math.Clamp(done / total, 0, 1);
        }

        return null;
    }

    private static string? ReadBlockModel(string devicePath)
    {
        try
        {
            string modelPath = $"/sys/class/block/{Path.GetFileName(devicePath)}/device/model";
            return File.Exists(modelPath) ? File.ReadAllText(modelPath).Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string fileName,
        IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"无法启动 {fileName}，请确认已安装。");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (process.ExitCode, await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false));
    }

    [GeneratedRegex(@"Mounted Media:\s+[0-9A-Fa-f]+h,\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex MediaRegex();

    [GeneratedRegex(@"ATIP start of lead out:\s*(\d+)")]
    private static partial Regex AtipLeadOutRegex();

    [GeneratedRegex(@"Legacy lead-out at:\s*(\d+)")]
    private static partial Regex LegacyLeadOutRegex();

    [GeneratedRegex(@"([0-9]+(?:\.[0-9]+)?)%")]
    private static partial Regex PercentRegex();

    [GeneratedRegex(@"Track\s+\d+:\s+([0-9]+(?:\.[0-9]+)?)\s+of\s+([0-9]+(?:\.[0-9]+)?)\s+MB")]
    private static partial Regex WrittenRegex();
}
