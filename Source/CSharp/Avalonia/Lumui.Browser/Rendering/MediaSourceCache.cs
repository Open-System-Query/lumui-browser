using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Lumui.Browser.Configuration;

namespace Lumui.Browser.Rendering;

internal static class MediaSourceCache
{
    private const Int64 MaximumMediaBytes = 512L * 1024L * 1024L;
    private static readonly ConcurrentDictionary<String, SemaphoreSlim> Gates =
        new ConcurrentDictionary<String, SemaphoreSlim>(StringComparer.Ordinal);
    private static readonly HttpClient Client = CreateClient();

    public static async Task<Uri> ResolveAsync(
        Uri source,
        Action<Int32>? progress,
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

        String path = CachePath(source);
        if (Valid(path))
        {
            progress?.Invoke(100);
            return new Uri(path, UriKind.Absolute);
        }

        SemaphoreSlim gate = Gates.GetOrAdd(
            path,
            _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Valid(path))
            {
                progress?.Invoke(100);
                return new Uri(path, UriKind.Absolute);
            }

            Directory.CreateDirectory(BrowserPaths.MediaCacheFolder);
            String temporary = path + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                using HttpResponseMessage response = await Client.GetAsync(
                    source,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using (Stream input = await response.Content.ReadAsStreamAsync(
                    cancellationToken).ConfigureAwait(false))
                {
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
                        Int32 reported = -1;
                        while (true)
                        {
                            Int32 read = await input.ReadAsync(
                                buffer,
                                cancellationToken).ConfigureAwait(false);
                            if (read == 0)
                            {
                                break;
                            }
                            total += read;
                            if (total > MaximumMediaBytes)
                            {
                                throw new InvalidDataException("The media source is too large.");
                            }
                            await output.WriteAsync(
                                buffer.AsMemory(0, read),
                                cancellationToken).ConfigureAwait(false);
                            if (expected > 0L)
                            {
                                Int32 percentage = Math.Clamp(
                                    (Int32)Math.Round(total * 100D / expected.Value),
                                    0,
                                    100);
                                if (percentage != reported)
                                {
                                    reported = percentage;
                                    progress?.Invoke(percentage);
                                }
                            }
                        }
                        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                File.Move(temporary, path, true);
                progress?.Invoke(100);
            }
            catch
            {
                DeleteFile(temporary);
                throw;
            }
            return new Uri(path, UriKind.Absolute);
        }
        finally
        {
            gate.Release();
        }
    }

    private static HttpClient CreateClient()
    {
        HttpClient client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5D)
        })
        {
            Timeout = TimeSpan.FromMinutes(5D)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LUMUI-Browser-Avalonia/1.0");
        return client;
    }

    private static String CachePath(Uri source)
    {
        Byte[] identity = SHA256.HashData(
            Encoding.UTF8.GetBytes(source.AbsoluteUri));
        String extension = Path.GetExtension(source.AbsolutePath);
        if (extension.Length is < 2 or > 12
            || extension.Skip(1).Any(character => !Char.IsLetterOrDigit(character)))
        {
            extension = ".media";
        }
        return Path.Combine(
            BrowserPaths.MediaCacheFolder,
            Convert.ToHexString(identity).ToLowerInvariant() + extension.ToLowerInvariant());
    }

    private static Boolean Valid(String path)
    {
        try
        {
            return File.Exists(path) && new FileInfo(path).Length > 0L;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void DeleteFile(String path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
