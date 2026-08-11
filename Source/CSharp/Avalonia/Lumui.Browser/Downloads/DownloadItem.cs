namespace Lumui.Browser.Downloads;

public sealed class DownloadItem
{
    public DownloadItem(Uri source, String targetPath)
    {
        Id = Guid.NewGuid();
        Source = source;
        TargetPath = targetPath;
        StartedAt = DateTimeOffset.Now;
    }

    internal DownloadItem(
        Guid id,
        Uri source,
        String targetPath,
        DateTimeOffset startedAt,
        DownloadStatus status,
        Int64 bytesReceived,
        Int64? totalBytes,
        String error)
    {
        Id = id;
        Source = source;
        TargetPath = targetPath;
        StartedAt = startedAt;
        Status = status is DownloadStatus.Downloading or DownloadStatus.Queued
            ? DownloadStatus.Cancelled
            : status;
        BytesReceived = bytesReceived;
        TotalBytes = totalBytes;
        Error = error;
    }

    public Guid Id { get; }

    public Uri Source { get; }

    public String TargetPath { get; }

    public DateTimeOffset StartedAt { get; }

    public DownloadStatus Status { get; internal set; } = DownloadStatus.Queued;

    public Int64 BytesReceived { get; internal set; }

    public Int64? TotalBytes { get; internal set; }

    public String Error { get; internal set; } = String.Empty;

    public Int32 ProgressPercent => TotalBytes is > 0
        ? (Int32)Math.Clamp(BytesReceived * 100L / TotalBytes.Value, 0L, 100L)
        : 0;

    public String FileName => Path.GetFileName(TargetPath);
}
