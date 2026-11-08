using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using DiscImageStudio.Core;

namespace DiscImageStudio.Cd;

/// <summary>一条随盘音乐轨：任意 ffmpeg 能解的音频文件，生成时转成 CD-DA 的 44.1 kHz / 16 bit / 立体声。</summary>
public sealed record CdMusicTrack(string FilePath, string Title);

/// <summary>多轨音频 CD：前面是用户音乐轨，最后一轨（可选）是盘面图像编码轨。</summary>
public sealed record CdMultiTrackRequest(
    string? SourceImage,
    string OutputWavePath,
    IReadOnlyList<CdMusicTrack> MusicTracks,
    CdDiscParameters Parameters,
    ToneProfile Tone,
    bool Interleave = true,
    bool WriteCue = true,
    /// <summary>false（默认）：音乐和图案拼成同一轨，CUE 只有一条 TRACK 01，任何播放器都当一轨放。
    /// true：每条音乐和图案各占一轨（依赖刻录工具严格按 CUE 处理，否则轨间 pre-gap 会让图案偏移）。</summary>
    bool SplitTracks = false);

public sealed record CdMultiTrackSummary(
    string OutputWave,
    IReadOnlyList<long> TrackStartSectors,
    IReadOnlyList<long> TrackSectors,
    long PatternStartSector,
    bool HasPatternTrack,
    TimeSpan Elapsed)
{
    public string? CueSheetPath { get; init; }
}

/// <summary>
/// 写一张"音乐 + 图案"的音频 CD：
/// 轨 1..N 是音乐（CD 机/车载能正常播放），最后一轨是盘面图像编码得到的噪声轨，
/// 图案因此只出现在音乐结束后的外圈。所有轨拼进同一个 WAV，CUE 记录每条轨的起始扇区。
/// </summary>
public static class CdMultiTrackGenerator
{
    private const int BytesPerSector = CdDiscParameters.BytesPerSector;

    public static CdMultiTrackSummary Generate(
        CdMultiTrackRequest request,
        Action<CdProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Parameters);
        request.Parameters.Validate();
        request.Tone.Validate();
        if (request.MusicTracks.Count == 0 && string.IsNullOrWhiteSpace(request.SourceImage))
        {
            throw new ArgumentException("Multi-track CD needs at least one music track or a source image.");
        }

        string outputPath = Path.GetFullPath(request.OutputWavePath);
        if (!Path.GetExtension(outputPath).Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Multi-track CD output must be a .wav file.", nameof(request));
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        string workspace = Path.Combine(Path.GetTempPath(), $"disc-image-studio-multitrack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);

        try
        {
            // 第一步：把每条音乐轨解码成裸 PCM，顺便对齐到扇区边界。
            List<string> musicPcmFiles = [];
            List<long> trackStartSectors = [];
            List<long> trackSectors = [];
            long cursorSectors = 0;

            for (int index = 0; index < request.MusicTracks.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CdMusicTrack track = request.MusicTracks[index];
                string pcmPath = Path.Combine(workspace, $"music-{index:D3}.pcm");
                long bytes = DecodeToPcm(track.FilePath, pcmPath, cancellationToken);
                long sectors = Math.Max(1, (bytes + BytesPerSector - 1) / BytesPerSector);
                PadToSectorBoundary(pcmPath, sectors);

                trackStartSectors.Add(cursorSectors);
                trackSectors.Add(sectors);
                musicPcmFiles.Add(pcmPath);
                cursorSectors += sectors;
                progress?.Invoke(new CdProgress(
                    index + 1, request.MusicTracks.Count,
                    TimeSpan.FromSeconds(stopwatch.Elapsed.TotalSeconds)));
            }

            // 第二步：图案轨从音乐结束的位置开始，一直画到参数里的外半径。
            long patternStartSector = cursorSectors;
            string? patternPcmPath = null;
            string? patternMetadataPath = null;
            long patternSectors = 0;
            if (!string.IsNullOrWhiteSpace(request.SourceImage))
            {
                long remaining = request.Parameters.OutputSectors - cursorSectors;
                if (remaining <= 0)
                {
                    throw new InvalidOperationException(
                        "音乐已经占满整张盘，没有扇区留给图案轨。减少音乐曲目或调大扇区数。");
                }

                long patternStartByte = checked(cursorSectors * BytesPerSector);
                double innerRadiusMm = request.Parameters.RadiusFromByte(patternStartByte);
                if (innerRadiusMm >= request.Parameters.OuterRadiusMm)
                {
                    throw new InvalidOperationException(
                        "音乐长度超出可用半径范围，图案轨没有位置。");
                }

                CdDiscParameters patternParameters = request.Parameters with
                {
                    InnerRadiusMm = innerRadiusMm,
                    Sectors = remaining,
                    UsableSectors = null,
                    SourceImageOuterRadiusMm = null,
                };
                patternParameters.Validate();
                patternPcmPath = Path.Combine(workspace, "pattern.pcm");
                patternMetadataPath = Path.Combine(workspace, "pattern.json");
                using FileStream patternStream = new(
                    patternPcmPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    bufferSize: 1024 * 1024, FileOptions.SequentialScan);
                CdGenerationSummary summary = CdTrackGenerator.GenerateToStream(
                    request.SourceImage!, patternStream, patternParameters, request.Interleave,
                    progress: null,
                    cancellationToken,
                    "multitrack-pattern",
                    CdAudioByteOrder.LittleEndian,
                    request.Tone);
                patternSectors = summary.Sectors;
            }

            // 第三步：写整张 WAV（header 先留位，长度已知后再回填）。
            long totalAudioBytes = checked(
                (cursorSectors + patternSectors) * BytesPerSector);
            CdWaveFile.ValidateLength(totalAudioBytes);
            using (FileStream output = new(
                outputPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 1024 * 1024, FileOptions.SequentialScan))
            {
                CdWaveFile.WriteHeader(output, totalAudioBytes);
                foreach (string pcmPath in musicPcmFiles)
                {
                    using FileStream pcm = File.OpenRead(pcmPath);
                    pcm.CopyTo(output, 1024 * 1024);
                }

                if (patternPcmPath is not null)
                {
                    using FileStream pcm = File.OpenRead(patternPcmPath);
                    pcm.CopyTo(output, 1024 * 1024);
                }
            }

            string? cuePath = null;
            if (request.WriteCue)
            {
                cuePath = request.SplitTracks
                    ? WriteSplitCue(outputPath, request.MusicTracks, trackStartSectors,
                        patternStartSector, patternPcmPath is not null)
                    : WriteSingleTrackCue(outputPath);
            }

            return new CdMultiTrackSummary(outputPath, trackStartSectors, trackSectors,
                patternStartSector, patternPcmPath is not null, stopwatch.Elapsed)
            {
                CueSheetPath = cuePath,
            };
        }
        finally
        {
            try
            {
                Directory.Delete(workspace, recursive: true);
            }
            catch (IOException)
            {
                // 临时目录清理失败不影响产物。
            }
        }
    }

    /// <summary>用 ffmpeg 把任意可解音频转成 CD-DA 裸 PCM（s16le / 44.1 kHz / 双声道）。</summary>
    private static long DecodeToPcm(string inputPath, string pcmPath, CancellationToken cancellationToken)
    {
        string fullInput = Path.GetFullPath(inputPath);
        if (!File.Exists(fullInput))
        {
            throw new FileNotFoundException("找不到音乐文件。", fullInput);
        }

        ProcessStartInfo startInfo = new("ffmpeg")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (string argument in new[]
                 {
                     "-v", "error", "-y", "-i", fullInput,
                     "-map", "a:0", "-f", "s16le", "-acodec", "pcm_s16le",
                     "-ac", "2", "-ar", "44100", "-",
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 ffmpeg，请先确认它已安装。");
        using (FileStream output = new(pcmPath, FileMode.Create, FileAccess.Write, FileShare.None,
                   bufferSize: 1024 * 1024, FileOptions.SequentialScan))
        {
            process.StandardOutput.BaseStream.CopyTo(output, 1024 * 1024);
        }

        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        cancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
        {
            string name = Path.GetFileName(fullInput);
            throw new InvalidDataException(
                $"无法把「{name}」转成 CD 音轨（ffmpeg 退出码 {process.ExitCode}）：{error.Trim()}");
        }

        return new FileInfo(pcmPath).Length;
    }

    /// <summary>CD 每轨按扇区对齐，不足的部分补静音。</summary>
    private static void PadToSectorBoundary(string pcmPath, long sectors)
    {
        long expected = checked(sectors * BytesPerSector);
        using FileStream stream = new(pcmPath, FileMode.Open, FileAccess.Write, FileShare.None);
        if (stream.Length >= expected)
        {
            return;
        }

        stream.SetLength(expected);
    }

    /// <summary>拼接成一条轨：播放器看到的是一个 80 分钟的音轨，音乐在前、图案编码在后。</summary>
    private static string WriteSingleTrackCue(string wavePath)
    {
        string cuePath = Path.ChangeExtension(wavePath, ".cue");
        StringBuilder builder = new();
        builder.Append("FILE \"").Append(Path.GetFileName(wavePath)).AppendLine("\" WAVE");
        AppendCueTrack(builder, 1, null, 0);
        File.WriteAllText(cuePath, builder.ToString(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return cuePath;
    }

    private static string WriteSplitCue(
        string wavePath,
        IReadOnlyList<CdMusicTrack> musicTracks,
        IReadOnlyList<long> trackStartSectors,
        long patternStartSector,
        bool hasPattern)
    {
        string cuePath = Path.ChangeExtension(wavePath, ".cue");
        StringBuilder builder = new();
        builder.Append("FILE \"").Append(Path.GetFileName(wavePath)).AppendLine("\" WAVE");

        int number = 1;
        for (int index = 0; index < musicTracks.Count; index++)
        {
            AppendCueTrack(builder, number++, musicTracks[index].Title, trackStartSectors[index]);
        }

        if (hasPattern)
        {
            AppendCueTrack(builder, number, null, patternStartSector);
        }

        File.WriteAllText(cuePath, builder.ToString(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return cuePath;
    }

    private static void AppendCueTrack(StringBuilder builder, int number, string? title, long startSector)
    {
        builder.Append("  TRACK ").Append(number.ToString("D2", CultureInfo.InvariantCulture))
            .AppendLine(" AUDIO");
        if (!string.IsNullOrWhiteSpace(title))
        {
            builder.Append("    TITLE \"").Append(title.Replace("\"", "''", StringComparison.Ordinal))
                .AppendLine("\"");
        }

        builder.Append("    INDEX 01 ").AppendLine(FormatCueTime(startSector));
    }

    /// <summary>扇区 → mm:ss:ff（75 帧/秒）。</summary>
    public static string FormatCueTime(long sectors)
    {
        long minutes = sectors / (75 * 60);
        long seconds = sectors / 75 % 60;
        long frames = sectors % 75;
        return string.Create(CultureInfo.InvariantCulture,
            $"{minutes:D2}:{seconds:D2}:{frames:D2}");
    }
}
