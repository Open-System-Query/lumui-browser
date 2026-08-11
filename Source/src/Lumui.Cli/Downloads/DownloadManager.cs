using System.Net.Http.Headers;
using Lumui.Cli.Configuration;
using Lumui.Cli.Data;

namespace Lumui.Cli.Downloads;

public sealed class DownloadManager : IDisposable
{
    private readonly HttpClient _http = new HttpClient(new SocketsHttpHandler
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    })
    {
        Timeout = TimeSpan.FromMinutes(30)
    };
    private readonly List<DownloadItem> _items = new List<DownloadItem>();
    private readonly Dictionary<Guid, CancellationTokenSource> _cancellations =
        new Dictionary<Guid, CancellationTokenSource>();
    private readonly Boolean _persistent;

    public DownloadManager(Boolean privateMode)
    {
        _persistent = !privateMode;
        if (_persistent)
        {
            Load();
        }
    }

    public event Action? Changed;

    public IReadOnlyList<DownloadItem> Items => _items
        .OrderByDescending(item => item.StartedAt)
        .ToArray();

    public async Task<DownloadItem> StartAsync(
        Uri source,
        String targetFolder,
        CancellationToken cancellationToken = default)
    {
        if (!source.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !source.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only HTTP and HTTPS downloads are supported.", nameof(source));
        }
        Directory.CreateDirectory(targetFolder);
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, source);
        using HttpResponseMessage response = await _http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        String targetPath = UniquePath(targetFolder, FileName(response.Content.Headers, source));
        DownloadItem item = new DownloadItem(source, targetPath);
        _items.Add(item);
        CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellations[item.Id] = linked;
        Save();
        Changed?.Invoke();
        await TransferAsync(item, response, linked.Token).ConfigureAwait(false);
        return item;
    }

    public void Cancel(Guid id)
    {
        if (_cancellations.TryGetValue(id, out CancellationTokenSource? cancellation))
        {
            cancellation.Cancel();
        }
    }

    public async Task<DownloadItem> RetryAsync(
        Guid id,
        String fallbackFolder,
        CancellationToken cancellationToken = default)
    {
        DownloadItem previous = _items.FirstOrDefault(item => item.Id == id)
            ?? throw new InvalidOperationException("The download is no longer available.");
        if (previous.Status is DownloadStatus.Queued or DownloadStatus.Downloading)
        {
            throw new InvalidOperationException("The download is already running.");
        }
        DownloadItem retried = await StartAsync(
            previous.Source,
            Path.GetDirectoryName(previous.TargetPath) ?? fallbackFolder,
            cancellationToken).ConfigureAwait(false);
        Remove(id);
        return retried;
    }

    public void ClearFinished()
    {
        _items.RemoveAll(item => item.Status is DownloadStatus.Completed or DownloadStatus.Cancelled or DownloadStatus.Failed);
        Save();
        Changed?.Invoke();
    }

    public void Remove(Guid id)
    {
        if (_items.RemoveAll(item => item.Id == id
            && item.Status is not (DownloadStatus.Queued or DownloadStatus.Downloading)) == 0)
        {
            return;
        }
        Save();
        Changed?.Invoke();
    }

    private async Task TransferAsync(
        DownloadItem item,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        String partialPath = item.TargetPath + ".download";
        item.Status = DownloadStatus.Downloading;
        item.TotalBytes = response.Content.Headers.ContentLength;
        Changed?.Invoke();
        try
        {
            await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (FileStream target = new FileStream(
                partialPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                true))
            {
                Byte[] buffer = new Byte[81920];
                Int64 nextUpdate = Environment.TickCount64 + 100L;
                while (true)
                {
                    Int32 read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    item.BytesReceived += read;
                    if (Environment.TickCount64 >= nextUpdate)
                    {
                        nextUpdate = Environment.TickCount64 + 100L;
                        Changed?.Invoke();
                    }
                }
            }
            File.Move(partialPath, item.TargetPath);
            item.Status = DownloadStatus.Completed;
        }
        catch (OperationCanceledException)
        {
            item.Status = DownloadStatus.Cancelled;
            DeletePartial(partialPath);
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException or UnauthorizedAccessException)
        {
            item.Status = DownloadStatus.Failed;
            item.Error = exception.Message;
            DeletePartial(partialPath);
        }
        finally
        {
            if (_cancellations.Remove(item.Id, out CancellationTokenSource? cancellation))
            {
                cancellation.Dispose();
            }
            Save();
            Changed?.Invoke();
        }
    }

    private static String FileName(HttpContentHeaders headers, Uri source)
    {
        String? suggested = headers.ContentDisposition?.FileNameStar ?? headers.ContentDisposition?.FileName;
        suggested = suggested?.Trim('"');
        if (String.IsNullOrWhiteSpace(suggested))
        {
            suggested = Path.GetFileName(source.LocalPath);
        }
        if (String.IsNullOrWhiteSpace(suggested))
        {
            suggested = "download";
        }
        foreach (Char invalid in Path.GetInvalidFileNameChars())
        {
            suggested = suggested.Replace(invalid, '_');
        }
        return suggested;
    }

    private static String UniquePath(String folder, String fileName)
    {
        String path = Path.Combine(folder, fileName);
        if (!File.Exists(path) && !File.Exists(path + ".download"))
        {
            return path;
        }
        String name = Path.GetFileNameWithoutExtension(fileName);
        String extension = Path.GetExtension(fileName);
        for (Int32 index = 2; ; index++)
        {
            path = Path.Combine(folder, $"{name} ({index}){extension}");
            if (!File.Exists(path) && !File.Exists(path + ".download"))
            {
                return path;
            }
        }
    }

    private static void DeletePartial(String path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void Load()
    {
        if (!File.Exists(CliPaths.DownloadsFile))
        {
            return;
        }
        try
        {
            foreach (String line in File.ReadLines(CliPaths.DownloadsFile))
            {
                String[] parts = line.Split('|');
                if (parts.Length != 9
                    || !Guid.TryParse(parts[0], out Guid id)
                    || !Uri.TryCreate(LocalDataCodec.Decode(parts[1]), UriKind.Absolute, out Uri? source)
                    || !Int64.TryParse(parts[4], out Int64 started)
                    || !Enum.TryParse(parts[5], out DownloadStatus status)
                    || !Int64.TryParse(parts[6], out Int64 received))
                {
                    continue;
                }
                Int64? total = Int64.TryParse(parts[7], out Int64 totalValue) ? totalValue : null;
                _items.Add(new DownloadItem(
                    id,
                    source,
                    LocalDataCodec.Decode(parts[2]),
                    DateTimeOffset.FromUnixTimeSeconds(started),
                    status,
                    received,
                    total,
                    LocalDataCodec.Decode(parts[8])));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
        {
        }
    }

    private void Save()
    {
        if (!_persistent)
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(CliPaths.DataFolder);
            String temporary = CliPaths.DownloadsFile + ".tmp";
            File.WriteAllLines(temporary, _items.Select(item => String.Join(
                "|",
                item.Id,
                LocalDataCodec.Encode(item.Source.AbsoluteUri),
                LocalDataCodec.Encode(item.TargetPath),
                LocalDataCodec.Encode(item.FileName),
                item.StartedAt.ToUnixTimeSeconds(),
                item.Status,
                item.BytesReceived,
                item.TotalBytes?.ToString(CultureInfo.InvariantCulture) ?? String.Empty,
                LocalDataCodec.Encode(item.Error))));
            File.Move(temporary, CliPaths.DownloadsFile, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        foreach (CancellationTokenSource cancellation in _cancellations.Values)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
        _cancellations.Clear();
        _http.Dispose();
    }
}
