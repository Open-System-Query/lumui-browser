using System.Collections.Concurrent;
using Lumui.Cli.Configuration;

namespace Lumui.Cli.Rendering;

public static class TerminalMediaService
{
    private const String PreparationVersion = "terminal-ppm-pcm-v5";
    private const String PreviewVersion = "terminal-preview-v1";
    private const Int64 MaximumMediaBytes = 512L * 1024L * 1024L;
    private static readonly ConcurrentDictionary<String, SemaphoreSlim> Gates =
        new ConcurrentDictionary<String, SemaphoreSlim>(StringComparer.Ordinal);
    private static readonly HttpClient Client = CreateClient();

    public static async Task<TerminalPixelFrame> PreparePreviewFrameAsync(
        MediaSourceDescriptor source,
        Boolean video,
        CancellationToken cancellationToken)
    {
        Uri local = await ResolveSourceAsync(source.Uri, _ => { }, cancellationToken).ConfigureAwait(false);
        if (!local.IsFile)
        {
            throw new InvalidDataException("The media preview source could not be cached locally.");
        }
        FileInfo file = new FileInfo(local.LocalPath);
        if (!file.Exists || file.Length == 0L)
        {
            throw new FileNotFoundException("The cached media preview source is unavailable.", file.FullName);
        }
        String identity = String.Join(
            "|",
            PreviewVersion,
            source.Uri.AbsoluteUri,
            source.MimeType,
            file.Length,
            file.LastWriteTimeUtc.Ticks,
            video);
        String key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        String folder = Path.Combine(CliPaths.TerminalMediaCacheFolder, "previews");
        String output = Path.Combine(folder, key + ".ppm");
        TerminalPixelFrame? cached = TryLoadFrame(output);
        if (cached is not null)
        {
            return cached;
        }

        SemaphoreSlim gate = Gates.GetOrAdd(output, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = TryLoadFrame(output);
            if (cached is not null)
            {
                return cached;
            }
            Directory.CreateDirectory(folder);
            String temporary = output + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                Int32 width = video ? 72 : 96;
                if (!video && SvgTerminalRasterizer.TryRender(local.LocalPath, temporary, width))
                {
                    File.Move(temporary, output, true);
                    return LoadFrame(output);
                }
                List<String> arguments = BaseArguments(local.LocalPath);
                arguments.AddRange(new[]
                {
                    "-map", "0:v:0",
                    "-an",
                    "-sn",
                    "-dn",
                    "-frames:v", "1",
                    "-vf", "scale=" + width.ToString(CultureInfo.InvariantCulture)
                        + ":-2:force_original_aspect_ratio=decrease,format=rgb24",
                    "-c:v", "ppm",
                    "-f", "image2",
                    "-update", "1",
                    temporary
                });
                await RunFfmpegAsync(
                    ResolveFfmpeg(),
                    arguments,
                    TimeSpan.Zero,
                    _ => { },
                    false,
                    cancellationToken).ConfigureAwait(false);
                if (!File.Exists(temporary) || new FileInfo(temporary).Length == 0L)
                {
                    throw new InvalidDataException("The media preview decoder produced no frame.");
                }
                File.Move(temporary, output, true);
                return LoadFrame(output);
            }
            catch
            {
                DeleteFile(temporary);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public static async Task<PreparedTerminalMedia> PrepareAsync(
        MediaSourceDescriptor source,
        Boolean video,
        Boolean image,
        TimeSpan durationHint,
        Action<TerminalMediaProgress> progress,
        CancellationToken cancellationToken)
    {
        progress(new TerminalMediaProgress("Downloading", null));
        Uri local = await ResolveSourceAsync(
            source.Uri,
            value => progress(new TerminalMediaProgress("Downloading", value)),
            cancellationToken).ConfigureAwait(false);
        if (!local.IsFile)
        {
            throw new InvalidDataException("The media source could not be cached locally.");
        }
        FileInfo file = new FileInfo(local.LocalPath);
        if (!file.Exists || file.Length == 0L)
        {
            throw new FileNotFoundException("The cached media source is unavailable.", file.FullName);
        }
        String key = CacheKey(source, file, video, image);
        String folder = Path.Combine(CliPaths.TerminalMediaCacheFolder, key);
        PreparedTerminalMedia? cached = Load(folder);
        if (cached is not null)
        {
            progress(new TerminalMediaProgress("Ready", 100));
            return cached;
        }
        SemaphoreSlim gate = Gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = Load(folder);
            if (cached is not null)
            {
                progress(new TerminalMediaProgress("Ready", 100));
                return cached;
            }
            Directory.CreateDirectory(CliPaths.TerminalMediaCacheFolder);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
            String temporary = folder + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                progress(new TerminalMediaProgress("Preparing", null));
                TerminalMediaManifest manifest = await DecodeAsync(
                    local.LocalPath,
                    temporary,
                    video,
                    image,
                    durationHint,
                    value => progress(new TerminalMediaProgress("Preparing", value)),
                    cancellationToken).ConfigureAwait(false);
                await File.WriteAllTextAsync(
                    Path.Combine(temporary, "manifest.json"),
                    JsonSerializer.Serialize(
                        manifest,
                        LumuiJsonSerializerContext.Default.TerminalMediaManifest),
                    cancellationToken).ConfigureAwait(false);
                await File.WriteAllTextAsync(
                    Path.Combine(temporary, "ready"),
                    PreparationVersion,
                    cancellationToken).ConfigureAwait(false);
                Directory.Move(temporary, folder);
            }
            catch
            {
                DeleteDirectory(temporary);
                throw;
            }
            return Load(folder) ?? throw new InvalidDataException("The prepared media cache is incomplete.");
        }
        finally
        {
            gate.Release();
        }
    }

    public static TerminalPixelFrame LoadFrame(String path)
    {
        Byte[] data = File.ReadAllBytes(path);
        Int32 cursor = 0;
        String magic = ReadToken(data, ref cursor);
        if (magic != "P6"
            || !Int32.TryParse(ReadToken(data, ref cursor), NumberStyles.None, CultureInfo.InvariantCulture, out Int32 width)
            || !Int32.TryParse(ReadToken(data, ref cursor), NumberStyles.None, CultureInfo.InvariantCulture, out Int32 height)
            || !Int32.TryParse(ReadToken(data, ref cursor), NumberStyles.None, CultureInfo.InvariantCulture, out Int32 maximum)
            || width <= 0
            || height <= 0
            || maximum != 255)
        {
            throw new InvalidDataException("The decoded media frame is invalid.");
        }
        if (cursor < data.Length && data[cursor] == (Byte)'\r')
        {
            cursor++;
            if (cursor < data.Length && data[cursor] == (Byte)'\n')
            {
                cursor++;
            }
        }
        else if (cursor < data.Length && Char.IsWhiteSpace((Char)data[cursor]))
        {
            cursor++;
        }
        Int32 length = checked(width * height * 3);
        if (data.Length - cursor < length)
        {
            throw new InvalidDataException("The decoded media frame is incomplete.");
        }
        Byte[] rgb = new Byte[length];
        Buffer.BlockCopy(data, cursor, rgb, 0, length);
        return new TerminalPixelFrame(width, height, rgb);
    }

    private static TerminalPixelFrame? TryLoadFrame(String path)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            return LoadFrame(path);
        }
        catch (Exception exception) when (
            exception is IOException
            or InvalidDataException
            or OverflowException)
        {
            DeleteFile(path);
            return null;
        }
    }

    private static async Task<Uri> ResolveSourceAsync(
        Uri source,
        Action<Int32> progress,
        CancellationToken cancellationToken)
    {
        if (source.IsFile)
        {
            return source;
        }
        if (!source.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !source.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }
        String path = SourceCachePath(source);
        if (File.Exists(path) && new FileInfo(path).Length > 0L)
        {
            progress(100);
            return new Uri(path, UriKind.Absolute);
        }
        SemaphoreSlim gate = Gates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length > 0L)
            {
                progress(100);
                return new Uri(path, UriKind.Absolute);
            }
            Directory.CreateDirectory(CliPaths.MediaCacheFolder);
            String temporary = path + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                using HttpResponseMessage response = await Client.GetAsync(
                    source,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using (Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (FileStream output = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    true))
                {
                    Byte[] buffer = new Byte[81920];
                    Int64 total = 0L;
                    Int64? expected = response.Content.Headers.ContentLength;
                    Int32 lastProgress = -1;
                    while (true)
                    {
                        Int32 read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                        if (read == 0)
                        {
                            break;
                        }
                        total += read;
                        if (total > MaximumMediaBytes)
                        {
                            throw new InvalidDataException("The media source is too large.");
                        }
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        if (expected > 0L)
                        {
                            Int32 percentage = Math.Clamp(
                                (Int32)Math.Round(total * 100D / expected.Value),
                                0,
                                100);
                            if (percentage != lastProgress)
                            {
                                lastProgress = percentage;
                                progress(percentage);
                            }
                        }
                    }
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                File.Move(temporary, path, true);
            }
            catch
            {
                DeleteFile(temporary);
                throw;
            }
            progress(100);
            return new Uri(path, UriKind.Absolute);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<TerminalMediaManifest> DecodeAsync(
        String input,
        String output,
        Boolean video,
        Boolean image,
        TimeSpan durationHint,
        Action<Int32> progress,
        CancellationToken cancellationToken)
    {
        const Double frameRate = 15D;
        const Int32 imageWidth = 96;
        const Int32 videoWidth = 72;
        const Int32 sampleRate = 48000;
        const Int32 channels = 2;
        const Int32 bitsPerSample = 16;
        String executable = ResolveFfmpeg();
        Directory.CreateDirectory(output);
        String framesFolder = Path.Combine(output, "frames");
        String audioPath = Path.Combine(output, "audio.pcm");
        String[] frames = Array.Empty<String>();
        if (video || image)
        {
            Directory.CreateDirectory(framesFolder);
            String firstFrame = Path.Combine(framesFolder, "frame-000000.ppm");
            if (image && SvgTerminalRasterizer.TryRender(input, firstFrame, imageWidth))
            {
                progress(100);
            }
            else
            {
                List<String> arguments = BaseArguments(input);
                arguments.AddRange(image
                    ? new[]
                    {
                        "-map", "0:v:0",
                        "-an",
                        "-frames:v", "1",
                        "-vf", "scale=" + imageWidth.ToString(CultureInfo.InvariantCulture)
                            + ":-2:force_original_aspect_ratio=decrease,format=rgb24",
                        "-c:v", "ppm",
                        "-f", "image2",
                        "-start_number", "0",
                        Path.Combine(framesFolder, "frame-%06d.ppm")
                    }
                    : new[]
                {
                    "-map", "0:v:0",
                    "-an",
                    "-sn",
                    "-dn",
                    "-vf", "fps=" + frameRate.ToString(CultureInfo.InvariantCulture)
                        + ",scale=" + videoWidth.ToString(CultureInfo.InvariantCulture)
                        + ":-2:force_original_aspect_ratio=decrease,format=rgb24",
                    "-c:v", "ppm",
                    "-f", "image2",
                    "-start_number", "0",
                    Path.Combine(framesFolder, "frame-%06d.ppm")
                });
                await RunFfmpegAsync(
                    executable,
                    arguments,
                    durationHint,
                    value => progress(image ? value : (Int32)Math.Round(value * 0.72D)),
                    false,
                    cancellationToken).ConfigureAwait(false);
            }
            frames = Directory.GetFiles(framesFolder, "frame-*.ppm")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(Path.GetFileName)
                .Where(name => name is not null)
                .Cast<String>()
                .ToArray();
            if (frames.Length == 0)
            {
                throw new InvalidDataException(image
                    ? "The image decoder produced no frame."
                    : "The video decoder produced no frames.");
            }
        }
        if (!image)
        {
            List<String> audioArguments = BaseArguments(input);
            audioArguments.AddRange(new[]
            {
                "-map", "0:a:0",
                "-vn",
                "-ac", channels.ToString(CultureInfo.InvariantCulture),
                "-ar", sampleRate.ToString(CultureInfo.InvariantCulture),
                "-acodec", "pcm_s16le",
                "-f", "s16le",
                audioPath
            });
            Boolean audioDecoded = await RunFfmpegAsync(
                executable,
                audioArguments,
                durationHint,
                value => progress(video ? 72 + (Int32)Math.Round(value * 0.26D) : (Int32)Math.Round(value * 0.98D)),
                video,
                cancellationToken).ConfigureAwait(false);
            if (!audioDecoded || !File.Exists(audioPath) || new FileInfo(audioPath).Length == 0L)
            {
                DeleteFile(audioPath);
                audioPath = String.Empty;
            }
        }
        else
        {
            audioPath = String.Empty;
        }
        if (!video && !image && audioPath.Length == 0)
        {
            throw new InvalidDataException("The audio decoder produced no samples.");
        }
        TimeSpan videoDuration = frames.Length > 0 ? TimeSpan.FromSeconds(frames.Length / frameRate) : TimeSpan.Zero;
        TimeSpan audioDuration = audioPath.Length > 0
            ? TimeSpan.FromSeconds(new FileInfo(audioPath).Length / (Double)(sampleRate * channels * bitsPerSample / 8))
            : TimeSpan.Zero;
        TimeSpan duration = videoDuration > audioDuration ? videoDuration : audioDuration;
        if (duration <= TimeSpan.Zero)
        {
            duration = image ? TimeSpan.FromSeconds(1) : durationHint;
        }
        progress(100);
        return new TerminalMediaManifest
        {
            HasVideo = video || image,
            Frames = frames,
            FrameRate = video ? frameRate : image ? 1D : 0D,
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

    private static async Task<Boolean> RunFfmpegAsync(
        String executable,
        IReadOnlyList<String> arguments,
        TimeSpan duration,
        Action<Int32> progress,
        Boolean allowFailure,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (String argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using Process process = new Process { StartInfo = start };
        if (!process.Start())
        {
            throw new InvalidOperationException("The media decoder could not be started.");
        }
        using CancellationTokenRegistration registration = cancellationToken.Register(() => Kill(process));
        Task<String> errorReader = process.StandardError.ReadToEndAsync();
        Task progressReader = ReadProgressAsync(process.StandardOutput, duration, progress, cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await progressReader.ConfigureAwait(false);
        }
        catch
        {
            Kill(process);
            throw;
        }
        String error = (await errorReader.ConfigureAwait(false)).Trim();
        if (process.ExitCode != 0)
        {
            if (allowFailure)
            {
                return false;
            }
            throw new InvalidDataException(error.Length > 0 ? error : $"The media decoder exited with code {process.ExitCode}.");
        }
        return true;
    }

    private static async Task ReadProgressAsync(
        StreamReader reader,
        TimeSpan duration,
        Action<Int32> progress,
        CancellationToken cancellationToken)
    {
        Int32 lastProgress = -1;
        while (true)
        {
            String? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                return;
            }
            if (line.StartsWith("out_time=", StringComparison.Ordinal)
                && duration > TimeSpan.Zero
                && TimeSpan.TryParse(line.AsSpan("out_time=".Length), CultureInfo.InvariantCulture, out TimeSpan position))
            {
                Int32 percentage = Math.Clamp(
                    (Int32)Math.Round(position.TotalMilliseconds * 100D / duration.TotalMilliseconds),
                    0,
                    99);
                if (percentage != lastProgress)
                {
                    lastProgress = percentage;
                    progress(percentage);
                }
            }
        }
    }

    private static PreparedTerminalMedia? Load(String folder)
    {
        try
        {
            String marker = Path.Combine(folder, "ready");
            String manifestPath = Path.Combine(folder, "manifest.json");
            if (!File.Exists(marker)
                || File.ReadAllText(marker) != PreparationVersion
                || !File.Exists(manifestPath))
            {
                return null;
            }
            TerminalMediaManifest? manifest = JsonSerializer.Deserialize(
                File.ReadAllText(manifestPath),
                LumuiJsonSerializerContext.Default.TerminalMediaManifest);
            if (manifest is null || manifest.DurationTicks <= 0L)
            {
                return null;
            }
            String framesFolder = Path.Combine(folder, "frames");
            String[] frames = manifest.Frames.Select(name => Path.Combine(framesFolder, name)).ToArray();
            if (manifest.HasVideo
                && (manifest.FrameRate <= 0D || frames.Length == 0 || frames.Any(path => !File.Exists(path))))
            {
                return null;
            }
            String? audio = manifest.AudioFile is null ? null : Path.Combine(folder, manifest.AudioFile);
            if (audio is not null && (!File.Exists(audio) || new FileInfo(audio).Length == 0L))
            {
                return null;
            }
            return new PreparedTerminalMedia(
                manifest.HasVideo,
                frames,
                manifest.FrameRate,
                audio,
                TimeSpan.FromTicks(manifest.DurationTicks));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static String CacheKey(
        MediaSourceDescriptor source,
        FileInfo file,
        Boolean video,
        Boolean image)
    {
        String identity = String.Join(
            "|",
            PreparationVersion,
            source.Uri.AbsoluteUri,
            source.MimeType,
            file.Length,
            file.LastWriteTimeUtc.Ticks,
            video,
            image);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    private static String SourceCachePath(Uri source)
    {
        String extension = Path.GetExtension(source.AbsolutePath);
        if (extension.Length is < 2 or > 12 || extension.Skip(1).Any(character => !Char.IsLetterOrDigit(character)))
        {
            extension = ".media";
        }
        String name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.AbsoluteUri))).ToLowerInvariant();
        return Path.Combine(CliPaths.MediaCacheFolder, name + extension.ToLowerInvariant());
    }

    private static String ResolveFfmpeg()
    {
        String executableName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        List<String> candidates = new List<String>();
        String? configured = Environment.GetEnvironmentVariable("LUMUI_BROWSER_FFMPEG_PATH");
        if (!String.IsNullOrWhiteSpace(configured))
        {
            candidates.Add(configured);
        }
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "Resources", executableName));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "ffmpeg", executableName));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, executableName));
        String? path = Environment.GetEnvironmentVariable("PATH");
        if (!String.IsNullOrWhiteSpace(path))
        {
            candidates.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(folder => Path.Combine(folder.Trim(), executableName)));
        }
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("The bundled LUMUI browser media decoder was not found.");
    }

    private static String ReadToken(Byte[] data, ref Int32 cursor)
    {
        while (cursor < data.Length)
        {
            while (cursor < data.Length && Char.IsWhiteSpace((Char)data[cursor]))
            {
                cursor++;
            }
            if (cursor < data.Length && data[cursor] == '#')
            {
                while (cursor < data.Length && data[cursor] is not (Byte)'\n' and not (Byte)'\r')
                {
                    cursor++;
                }
                continue;
            }
            break;
        }
        Int32 start = cursor;
        while (cursor < data.Length && !Char.IsWhiteSpace((Char)data[cursor]))
        {
            cursor++;
        }
        return Encoding.ASCII.GetString(data, start, cursor - start);
    }

    private static HttpClient CreateClient()
    {
        HttpClient client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LUMUI-Browser-TerminalGui/1.0");
        return client;
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
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static void DeleteFile(String path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void DeleteDirectory(String path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
