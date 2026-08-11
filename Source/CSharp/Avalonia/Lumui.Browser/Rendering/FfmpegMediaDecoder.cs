using System.Diagnostics;
using System.Globalization;

namespace Lumui.Browser.Rendering;

internal static class FfmpegMediaDecoder
{
    private const Double FrameRate = 12D;
    private const Int32 SampleRate = 48000;
    private const Int32 Channels = 2;
    private const Int32 BitsPerSample = 16;
    private const Int64 MaximumAudioBytes = 256L * 1024L * 1024L;
    private const Int64 MaximumPreparedBytes = 1024L * 1024L * 1024L;

    public static async Task<PreparedMediaManifest> PrepareAsync(
        String input,
        String output,
        Boolean video,
        TimeSpan durationHint,
        Action<Int32> progress,
        CancellationToken cancellationToken)
    {
        String executable = ResolveExecutable();
        Directory.CreateDirectory(output);
        String framesFolder = Path.Combine(output, "frames");
        String audioPath = Path.Combine(output, "audio.pcm");
        String[] frames = Array.Empty<String>();

        if (video)
        {
            Directory.CreateDirectory(framesFolder);
            List<String> arguments = BaseArguments(input);
            arguments.Add("-map");
            arguments.Add("0:v:0");
            arguments.Add("-vf");
            arguments.Add("fps=12,scale=960:-2:force_original_aspect_ratio=decrease");
            arguments.Add("-q:v");
            arguments.Add("4");
            arguments.Add("-start_number");
            arguments.Add("0");
            arguments.Add(Path.Combine(framesFolder, "frame-%06d.jpg"));
            await RunAsync(
                executable,
                arguments,
                durationHint,
                value => progress((Int32)Math.Round(value * 0.72D)),
                false,
                cancellationToken).ConfigureAwait(false);
            frames = Directory.GetFiles(framesFolder, "frame-*.jpg")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => Path.GetFileName(path))
                .Where(name => name is not null)
                .Cast<String>()
                .ToArray();
            if (frames.Length == 0)
            {
                throw new InvalidDataException("The video decoder produced no frames.");
            }
        }

        List<String> audioArguments = BaseArguments(input);
        audioArguments.Add("-map");
        audioArguments.Add("0:a:0");
        audioArguments.Add("-vn");
        audioArguments.Add("-ac");
        audioArguments.Add(Channels.ToString(CultureInfo.InvariantCulture));
        audioArguments.Add("-ar");
        audioArguments.Add(SampleRate.ToString(CultureInfo.InvariantCulture));
        audioArguments.Add("-acodec");
        audioArguments.Add("pcm_s16le");
        audioArguments.Add("-f");
        audioArguments.Add("s16le");
        audioArguments.Add(audioPath);
        Boolean audioDecoded = await RunAsync(
            executable,
            audioArguments,
            durationHint,
            value => progress(video
                ? 72 + (Int32)Math.Round(value * 0.26D)
                : (Int32)Math.Round(value * 0.98D)),
            video,
            cancellationToken).ConfigureAwait(false);
        if (!audioDecoded || !File.Exists(audioPath) || new FileInfo(audioPath).Length == 0L)
        {
            File.Delete(audioPath);
            audioPath = String.Empty;
        }
        if (audioPath.Length > 0 && new FileInfo(audioPath).Length > MaximumAudioBytes)
        {
            throw new InvalidDataException("The decoded audio is too large.");
        }
        if (!video && audioPath.Length == 0)
        {
            throw new InvalidDataException("The audio decoder produced no samples.");
        }

        TimeSpan videoDuration = frames.Length > 0
            ? TimeSpan.FromSeconds(frames.Length / FrameRate)
            : TimeSpan.Zero;
        TimeSpan audioDuration = audioPath.Length > 0
            ? TimeSpan.FromSeconds(
                new FileInfo(audioPath).Length
                / (Double)(SampleRate * Channels * BitsPerSample / 8))
            : TimeSpan.Zero;
        TimeSpan duration = videoDuration > audioDuration
            ? videoDuration
            : audioDuration;
        if (duration <= TimeSpan.Zero)
        {
            duration = durationHint;
        }
        EnsurePreparedSize(output);
        progress(100);
        return new PreparedMediaManifest
        {
            HasVideo = video,
            Frames = frames,
            FrameRate = video ? FrameRate : 0D,
            AudioFile = audioPath.Length > 0 ? "audio.pcm" : null,
            DurationTicks = Math.Max(0L, duration.Ticks)
        };
    }

    private static List<String> BaseArguments(String input) => new List<String>
    {
        "-y",
        "-nostdin",
        "-hide_banner",
        "-loglevel",
        "error",
        "-progress",
        "pipe:1",
        "-i",
        input
    };

    private static async Task<Boolean> RunAsync(
        String executable,
        IReadOnlyList<String> arguments,
        TimeSpan duration,
        Action<Int32> progress,
        Boolean allowFailure,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (String argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("The media decoder could not be started.");
        }
        using CancellationTokenRegistration registration = cancellationToken.Register(
            () => Kill(process));
        Task<String> errors = process.StandardError.ReadToEndAsync();
        Task progressReader = ReadProgressAsync(
            process.StandardOutput,
            duration,
            progress,
            cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await progressReader.ConfigureAwait(false);
        }
        catch
        {
            Kill(process);
            try
            {
                await process.WaitForExitAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
            }
            throw;
        }
        String error = (await errors.ConfigureAwait(false)).Trim();
        if (process.ExitCode != 0)
        {
            if (allowFailure)
            {
                return false;
            }
            throw new InvalidDataException(error.Length > 0
                ? error
                : $"The media decoder exited with code {process.ExitCode}.");
        }
        return true;
    }

    private static async Task ReadProgressAsync(
        StreamReader reader,
        TimeSpan duration,
        Action<Int32> progress,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            String? line = await reader.ReadLineAsync(cancellationToken)
                .ConfigureAwait(false);
            if (line is null)
            {
                return;
            }
            if (!line.StartsWith("out_time=", StringComparison.Ordinal)
                || duration <= TimeSpan.Zero
                || !TimeSpan.TryParse(
                    line.AsSpan("out_time=".Length),
                    CultureInfo.InvariantCulture,
                    out TimeSpan position))
            {
                continue;
            }
            progress(Math.Clamp(
                (Int32)Math.Round(position.TotalMilliseconds * 100D
                    / duration.TotalMilliseconds),
                0,
                99));
        }
    }

    private static String ResolveExecutable()
    {
        String? configured = Environment.GetEnvironmentVariable("LUMUI_BROWSER_FFMPEG_PATH");
        List<String> candidates = new List<String>();
        if (!String.IsNullOrWhiteSpace(configured))
        {
            candidates.Add(configured);
        }
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "Resources", "ffmpeg.exe"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"));
        String? path = Environment.GetEnvironmentVariable("PATH");
        if (!String.IsNullOrWhiteSpace(path))
        {
            candidates.AddRange(path
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(folder => Path.Combine(folder.Trim(), "ffmpeg.exe")));
        }
        String? executable = candidates.FirstOrDefault(File.Exists);
        return executable ?? throw new FileNotFoundException(
            "The bundled media decoder was not found.");
    }

    private static void EnsurePreparedSize(String folder)
    {
        Int64 size = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length);
        if (size > MaximumPreparedBytes)
        {
            throw new InvalidDataException("The prepared media is too large.");
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
