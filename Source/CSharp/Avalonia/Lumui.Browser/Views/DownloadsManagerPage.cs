using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Lumui.Browser.Configuration;
using Lumui.Browser.Downloads;
using Lumui.Browser.Rendering;

namespace Lumui.Browser.Views;

internal sealed class DownloadsManagerPage : IBrowserLibraryPage
{
    private const Int32 PageSize = 200;
    private readonly DownloadManager _downloads;
    private readonly BrowserPreferences _preferences;
    private String? _notice;
    private String _query = String.Empty;
    private Int32 _visibleCount = PageSize;

    public DownloadsManagerPage(
        DownloadManager downloads,
        BrowserPreferences preferences)
    {
        _downloads = downloads;
        _preferences = preferences;
        _downloads.Changed += DownloadsChanged;
    }

    public String Title => "Downloads";

    public String Description => "Files saved by this browser.";

    public String SearchPlaceholder => "Search downloads";

    public String Summary { get; private set; } = String.Empty;

    public String? PrimaryActionText => "Open folder";

    public String? SecondaryActionText => "Clear finished";

    public Boolean PrimaryActionEnabled => true;

    public Boolean SecondaryActionEnabled => _downloads.Items.Any(
        (DownloadItem item) => item.Status is not (
            DownloadStatus.Queued or DownloadStatus.Downloading));

    public event Action? Changed;

    public event Action<String>? OpenFileRequested;

    public event Action<String>? OpenFolderRequested;

    public Control Build(String query)
    {
        if (!String.Equals(_query, query, StringComparison.Ordinal))
        {
            _visibleCount = PageSize;
        }
        _query = query;
        DownloadItem[] allItems = _downloads.Items
            .Where((DownloadItem item) => Matches(
                query,
                item.FileName,
                item.Source.AbsoluteUri))
            .ToArray();
        DownloadItem[] items = allItems.Take(_visibleCount).ToArray();
        Int32 activeCount = allItems.Count((DownloadItem item) =>
            item.Status is DownloadStatus.Queued or DownloadStatus.Downloading);
        Summary = _notice ?? (activeCount > 0
            ? activeCount + (activeCount == 1 ? " active download" : " active downloads")
            : allItems.Length == 1
                ? "1 download"
                : allItems.Length + " downloads");
        _notice = null;
        List<ManagerListItem> rows = new List<ManagerListItem>();
        AddGroup(
            rows,
            "IN PROGRESS",
            items.Where((DownloadItem item) =>
                item.Status is DownloadStatus.Queued or DownloadStatus.Downloading));
        AddGroup(
            rows,
            "RECENT",
            items.Where((DownloadItem item) =>
                item.Status is not (DownloadStatus.Queued or DownloadStatus.Downloading)));
        if (items.Length == 0)
        {
            String title = query.Length == 0
                ? "No downloads yet"
                : "No downloads found";
            String message = query.Length == 0
                ? "Downloaded files and their progress will appear here."
                : "Try another file name or website.";
            rows.Add(new ManagerListItem(
                () => BrowserManagerControls.EmptyState(title, message)));
        }
        else if (items.Length < allItems.Length)
        {
            Int32 nextCount = Math.Min(
                PageSize,
                allItems.Length - items.Length);
            rows.Add(new ManagerListItem(() =>
            {
                Button more = BrowserManagerControls.TextButton(
                    "Show " + nextCount + " more");
                more.Margin = new Thickness(0D, 18D, 0D, 0D);
                more.HorizontalAlignment = HorizontalAlignment.Center;
                more.Click += (_, _) =>
                {
                    _visibleCount += PageSize;
                    Changed?.Invoke();
                };
                return more;
            }));
        }
        return BrowserManagerControls.VirtualizedPage(rows);
    }

    public Task PrimaryActionAsync(Window owner)
    {
        OpenFolderRequested?.Invoke(_preferences.DownloadFolder);
        return Task.CompletedTask;
    }

    public Task SecondaryActionAsync(Window owner)
    {
        _downloads.ClearFinished();
        return Task.CompletedTask;
    }

    public Boolean HandleKeyDown(Window owner, KeyEventArgs eventArgs) => false;

    public void Dispose()
    {
        _downloads.Changed -= DownloadsChanged;
    }

    private void AddGroup(
        ICollection<ManagerListItem> host,
        String title,
        IEnumerable<DownloadItem> source)
    {
        DownloadItem[] items = source.ToArray();
        if (items.Length == 0)
        {
            return;
        }
        host.Add(new ManagerListItem(
            () => BrowserManagerControls.SectionLabel(title)));
        foreach (DownloadItem item in items)
        {
            DownloadItem value = item;
            host.Add(new ManagerListItem(() => DownloadRow(value)));
        }
    }

    private Border DownloadRow(DownloadItem item)
    {
        StackPanel details = new StackPanel
        {
            Spacing = 4D,
            VerticalAlignment = VerticalAlignment.Center
        };
        details.Children.Add(new TextBlock
        {
            Text = item.FileName,
            FontSize = 15D,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        details.Children.Add(new TextBlock
        {
            Text = DownloadDescription(item),
            Classes = { "subtle" },
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (item.Status == DownloadStatus.Downloading)
        {
            details.Children.Add(new ProgressBar
            {
                Minimum = 0D,
                Maximum = 100D,
                Value = item.ProgressPercent,
                IsIndeterminate = item.TotalBytes is null,
                Height = 4D,
                Margin = new Thickness(0D, 5D, 0D, 0D)
            });
        }
        Border icon = BrowserManagerControls.ItemIcon(
            FileTypeLabel(item.FileName),
            String.Empty);
        if (item.Status is DownloadStatus.Cancelled or DownloadStatus.Failed)
        {
            icon.Classes.Add("failed");
        }
        Grid body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 14D
        };
        body.Children.Add(icon);
        Grid.SetColumn(details, 1);
        body.Children.Add(details);
        Control main = body;
        if (item.Status == DownloadStatus.Completed)
        {
            Button open = new Button { Content = body };
            open.Classes.Add("item-link");
            AutomationProperties.SetName(open, "Open " + item.FileName);
            open.Click += (_, _) => OpenFileRequested?.Invoke(item.TargetPath);
            main = open;
        }
        StackPanel actions = BrowserManagerControls.Actions();
        if (item.Status is DownloadStatus.Queued or DownloadStatus.Downloading)
        {
            Button cancel = BrowserManagerControls.IconButton(
                BrowserIcons.Close,
                "Cancel download");
            cancel.Click += (_, _) => _downloads.Cancel(item.Id);
            actions.Children.Add(cancel);
        }
        else
        {
            if (item.Status == DownloadStatus.Completed)
            {
                Button folder = BrowserManagerControls.IconButton(
                    BrowserIcons.FolderOpen,
                    "Show in folder");
                folder.Click += (_, _) => OpenFolderRequested?.Invoke(
                    Path.GetDirectoryName(item.TargetPath) ?? _preferences.DownloadFolder);
                actions.Children.Add(folder);
            }
            else
            {
                Button retry = BrowserManagerControls.IconButton(
                    BrowserIcons.Reload,
                    "Try download again");
                retry.Click += async (_, _) => await RetryAsync(item);
                actions.Children.Add(retry);
            }
            Button remove = BrowserManagerControls.IconButton(
                BrowserIcons.Clear,
                "Remove from downloads",
                true);
            remove.Click += (_, _) => _downloads.Remove(item.Id);
            actions.Children.Add(remove);
        }
        Border row = BrowserManagerControls.ItemRow(main, actions);
        if (item.Status == DownloadStatus.Failed && item.Error.Length > 0)
        {
            ToolTip.SetTip(row, item.Error);
        }
        return row;
    }

    private async Task RetryAsync(DownloadItem item)
    {
        try
        {
            await _downloads.RetryAsync(item.Id, _preferences.DownloadFolder);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or HttpRequestException
                or InvalidOperationException)
        {
            _notice = exception.Message;
            Changed?.Invoke();
        }
    }

    private void DownloadsChanged() => Changed?.Invoke();

    private static Boolean Matches(
        String query,
        String first,
        String second) => query.Length == 0
        || first.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || second.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    private static String DownloadDescription(DownloadItem item)
    {
        String source = item.Source.Host;
        String time = DownloadTimeLabel(item.StartedAt);
        if (item.Status == DownloadStatus.Downloading)
        {
            if (item.TotalBytes is > 0)
            {
                return FormatBytes(item.BytesReceived) + " of "
                    + FormatBytes(item.TotalBytes.Value) + "   " + item.ProgressPercent + "%   " + source;
            }
            return FormatBytes(item.BytesReceived) + " received   " + source;
        }
        if (item.Status == DownloadStatus.Queued)
        {
            return "Waiting   " + source;
        }
        if (item.Status == DownloadStatus.Completed)
        {
            Int64 bytes = item.TotalBytes ?? item.BytesReceived;
            String size = bytes > 0 ? FormatBytes(bytes) + "   " : String.Empty;
            return size + source + "   " + time;
        }
        if (item.Status == DownloadStatus.Cancelled)
        {
            return "Cancelled   " + source + "   " + time;
        }
        return "Download failed   " + source + "   " + time;
    }

    private static String DownloadTimeLabel(DateTimeOffset startedAt)
    {
        DateTimeOffset local = startedAt.ToLocalTime();
        return local.Date == DateTimeOffset.Now.Date
            ? "Today at " + local.ToString("t")
            : local.ToString("g");
    }

    private static String FileTypeLabel(String fileName)
    {
        String extension = Path.GetExtension(fileName).TrimStart('.');
        if (extension.Length == 0)
        {
            return "FILE";
        }
        return extension.Length <= 4
            ? extension.ToUpperInvariant()
            : extension[..4].ToUpperInvariant();
    }

    private static String FormatBytes(Int64 bytes)
    {
        if (bytes >= 1_073_741_824L)
        {
            return (bytes / 1_073_741_824D).ToString("0.0") + " GB";
        }
        if (bytes >= 1_048_576L)
        {
            return (bytes / 1_048_576D).ToString("0.0") + " MB";
        }
        if (bytes >= 1_024L)
        {
            return (bytes / 1_024D).ToString("0.0") + " KB";
        }
        return bytes + " B";
    }
}
