using System.Net.Http.Headers;
using Lumui.Browser.Configuration;
using Lumui.Browser.Data;

namespace Lumui.Browser.Downloads;

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

    public DownloadManager()
    {
        Load();
    }

    public event Action? Changed;

    public IReadOnlyList<DownloadItem> Items => _items
        .OrderByDescending((DownloadItem item) => item.StartedAt)
        .ToArray();

    public async Task<DownloadItem> StartAsync(
        Uri source,
        String targetFolder,
        CancellationToken cancellationToken = default)
    {
        if (!source.Scheme.Equals(
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase)
            && !source.Scheme.Equals(
                Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only HTTP and HTTPS downloads are supported.", nameof(source));
        }
        Directory.CreateDirectory(targetFolder);
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, source);
        using HttpResponseMessage response = await _http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        String fileName = FileName(response.Content.Headers, source);
        String targetPath = UniquePath(targetFolder, fileName);
        DownloadItem item = new DownloadItem(source, targetPath);
        _items.Add(item);
        CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        _cancellations[item.Id] = linked;
        Save();
        Changed?.Invoke();
        await TransferAsync(item, response, linked.Token);
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
        DownloadItem previous = _items.FirstOrDefault(
            (DownloadItem item) => item.Id == id)
            ?? throw new InvalidOperationException("The download is no longer available.");
        if (previous.Status is DownloadStatus.Queued or DownloadStatus.Downloading)
        {
            throw new InvalidOperationException("The download is already running.");
        }
        String folder = Path.GetDirectoryName(previous.TargetPath) ?? fallbackFolder;
        Uri source = previous.Source;
        DownloadItem retried = await StartAsync(source, folder, cancellationToken);
        Remove(id);
        return retried;
    }

    public void ClearFinished()
    {
        _items.RemoveAll((DownloadItem item) =>
            item.Status is DownloadStatus.Completed
                or DownloadStatus.Cancelled
                or DownloadStatus.Failed);
        Save();
        Changed?.Invoke();
    }

    public void Remove(Guid id)
    {
        Int32 removed = _items.RemoveAll((DownloadItem item) =>
            item.Id == id
            && item.Status is not (DownloadStatus.Queued or DownloadStatus.Downloading));
        if (removed == 0)
        {
            return;
        }
        Save();
        Changed?.Invoke();
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
            await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using FileStream target = new FileStream(
                partialPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                true);
            Byte[] buffer = new Byte[81920];
            Int64 nextProgressUpdate = Environment.TickCount64 + 100L;
            while (true)
            {
                Int32 read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                item.BytesReceived += read;
                if (Environment.TickCount64 >= nextProgressUpdate)
                {
                    nextProgressUpdate = Environment.TickCount64 + 100L;
                    Changed?.Invoke();
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
        catch (Exception exception) when (
            exception is IOException or HttpRequestException or UnauthorizedAccessException)
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
        String? suggested = headers.ContentDisposition?.FileNameStar
            ?? headers.ContentDisposition?.FileName;
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
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void Load()
    {
        if (!File.Exists(BrowserPaths.DownloadsFile))
        {
            return;
        }
        try
        {
            foreach (String line in File.ReadLines(BrowserPaths.DownloadsFile))
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
                Int64? total = Int64.TryParse(parts[7], out Int64 totalValue)
                    ? totalValue
                    : null;
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
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (FormatException)
        {
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(BrowserPaths.DataFolder);
            String temporary = BrowserPaths.DownloadsFile + ".tmp";
            File.WriteAllLines(
                temporary,
                _items.Select((DownloadItem item) => String.Join(
                    "|",
                    item.Id,
                    LocalDataCodec.Encode(item.Source.AbsoluteUri),
                    LocalDataCodec.Encode(item.TargetPath),
                    LocalDataCodec.Encode(item.FileName),
                    item.StartedAt.ToUnixTimeSeconds(),
                    item.Status,
                    item.BytesReceived,
                    item.TotalBytes?.ToString() ?? String.Empty,
                    LocalDataCodec.Encode(item.Error))));
            File.Move(temporary, BrowserPaths.DownloadsFile, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
