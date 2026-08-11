using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lumui.Browser.Configuration;

namespace Lumui.Browser.Rendering;

internal static class MediaPreparationService
{
    private const String PreparationVersion = "frames-pcm-v1";
    private static readonly ConcurrentDictionary<String, SemaphoreSlim> Gates =
        new ConcurrentDictionary<String, SemaphoreSlim>(StringComparer.Ordinal);

    public static async Task<PreparedMedia> PrepareAsync(
        MediaSourceDescriptor source,
        Boolean video,
        TimeSpan durationHint,
        Action<MediaPreparationProgress> progress,
        CancellationToken cancellationToken)
    {
        progress(new MediaPreparationProgress("DOWNLOADING", null));
        Uri resolved = await MediaSourceCache.ResolveAsync(
            source.Uri,
            value => progress(new MediaPreparationProgress(
                "DOWNLOADING",
                value)),
            cancellationToken).ConfigureAwait(false);
        if (!resolved.IsFile)
        {
            throw new InvalidDataException("The media source could not be cached locally.");
        }

        String input = resolved.LocalPath;
        FileInfo sourceFile = new FileInfo(input);
        if (!sourceFile.Exists || sourceFile.Length == 0L)
        {
            throw new FileNotFoundException("The cached media source is unavailable.", input);
        }
        String key = CacheKey(source, sourceFile, video);
        String folder = Path.Combine(BrowserPaths.PreparedMediaCacheFolder, key);
        PreparedMedia? cached = Load(folder);
        if (cached is not null)
        {
            progress(new MediaPreparationProgress("READY", 100));
            return cached;
        }

        SemaphoreSlim gate = Gates.GetOrAdd(
            key,
            _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = Load(folder);
            if (cached is not null)
            {
                progress(new MediaPreparationProgress("READY", 100));
                return cached;
            }

            Directory.CreateDirectory(BrowserPaths.PreparedMediaCacheFolder);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
            String temporary = folder + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                progress(new MediaPreparationProgress("PREPARING", null));
                PreparedMediaManifest manifest = await FfmpegMediaDecoder.PrepareAsync(
                    input,
                    temporary,
                    video,
                    durationHint,
                    value => progress(new MediaPreparationProgress(
                        "PREPARING",
                        value)),
                    cancellationToken).ConfigureAwait(false);
                String manifestPath = Path.Combine(temporary, "manifest.json");
                await File.WriteAllTextAsync(
                    manifestPath,
                    JsonSerializer.Serialize(
                        manifest,
                        BrowserJsonSerializerContext.Default.PreparedMediaManifest),
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
            return Load(folder)
                ?? throw new InvalidDataException("The prepared media cache is incomplete.");
        }
        finally
        {
            gate.Release();
        }
    }

    private static PreparedMedia? Load(String folder)
    {
        try
        {
            String marker = Path.Combine(folder, "ready");
            String manifestPath = Path.Combine(folder, "manifest.json");
            if (!File.Exists(marker)
                || !File.ReadAllText(marker).Equals(
                    PreparationVersion,
                    StringComparison.Ordinal)
                || !File.Exists(manifestPath))
            {
                return null;
            }
            PreparedMediaManifest? manifest = JsonSerializer.Deserialize(
                File.ReadAllText(manifestPath),
                BrowserJsonSerializerContext.Default.PreparedMediaManifest);
            if (manifest is null || manifest.DurationTicks <= 0L)
            {
                return null;
            }
            String framesFolder = Path.Combine(folder, "frames");
            String[] frames = (manifest.Frames ?? Array.Empty<String>())
                .Select(name => Path.Combine(framesFolder, name))
                .ToArray();
            if (manifest.HasVideo
                && (manifest.FrameRate <= 0D
                    || frames.Length == 0
                    || frames.Any(path => !File.Exists(path))))
            {
                return null;
            }
            String? audio = manifest.AudioFile is null
                ? null
                : Path.Combine(folder, manifest.AudioFile);
            if (audio is not null
                && (!File.Exists(audio) || new FileInfo(audio).Length == 0L))
            {
                return null;
            }
            if (!manifest.HasVideo && audio is null)
            {
                return null;
            }
            return new PreparedMedia(
                manifest.HasVideo,
                frames,
                manifest.FrameRate,
                audio,
                TimeSpan.FromTicks(manifest.DurationTicks));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static String CacheKey(
        MediaSourceDescriptor source,
        FileInfo file,
        Boolean video)
    {
        String identity = String.Join(
            "|",
            PreparationVersion,
            source.Uri.AbsoluteUri,
            source.MimeType,
            file.Length,
            file.LastWriteTimeUtc.Ticks,
            video);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
            .ToLowerInvariant();
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
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
