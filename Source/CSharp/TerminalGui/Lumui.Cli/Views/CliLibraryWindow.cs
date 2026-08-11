using Lumui.Cli.Configuration;
using Lumui.Cli.Data;
using Lumui.Cli.Downloads;
using Lumui.Cli.Security;

namespace Lumui.Cli.Views;

public sealed class CliLibraryWindow : Dialog
{
    private readonly CliBrowserServices _services;
    private readonly ObservableCollection<BookmarkEntry> _bookmarks = new ObservableCollection<BookmarkEntry>();
    private readonly ObservableCollection<HistoryEntry> _history = new ObservableCollection<HistoryEntry>();
    private readonly ObservableCollection<DownloadItem> _downloads = new ObservableCollection<DownloadItem>();
    private readonly ObservableCollection<CredentialRecord> _credentials = new ObservableCollection<CredentialRecord>();
    private readonly ListView _bookmarkList;
    private readonly ListView _historyList;
    private readonly ListView _downloadList;
    private readonly ListView _credentialList;
    private readonly TextField _bookmarkSearch;
    private readonly TextField _historySearch;
    private readonly Tabs _tabs;
    private readonly Dictionary<View, View> _actionBars = new Dictionary<View, View>();

    public CliLibraryWindow(CliBrowserServices services, String section = "Bookmarks")
    {
        _services = services;
        Title = "Library | LUMUI Browser";
        Width = Dim.Percent(92);
        Height = Dim.Percent(90);
        _tabs = new Tabs
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(3)
        };

        View bookmarkPage = Page("Bookmarks");
        _bookmarkSearch = SearchField(bookmarkPage);
        _bookmarkList = List(bookmarkPage);
        _bookmarkList.SetSource(_bookmarks);
        Button bookmarkFind = SmallButton(bookmarkPage, "Find");
        Button bookmarkOpen = SmallButton(bookmarkPage, "Open");
        Button bookmarkAdd = SmallButton(bookmarkPage, "Add");
        Button bookmarkEdit = SmallButton(bookmarkPage, "Edit");
        Button bookmarkRemove = SmallButton(bookmarkPage, "Remove");
        bookmarkFind.Accepting += (_, _) => RefreshBookmarks();
        _bookmarkSearch.Accepted += (_, _) => RefreshBookmarks();
        bookmarkOpen.Accepting += (_, _) => OpenBookmark();
        bookmarkAdd.Accepting += (_, _) => AddBookmark();
        bookmarkEdit.Accepting += (_, _) => EditBookmark();
        bookmarkRemove.Accepting += (_, _) => RemoveBookmark();
        _bookmarkList.Accepted += (_, _) => OpenBookmark();

        View historyPage = Page("History");
        _historySearch = SearchField(historyPage);
        _historyList = List(historyPage);
        _historyList.SetSource(_history);
        Button historyFind = SmallButton(historyPage, "Find");
        Button historyOpen = SmallButton(historyPage, "Open");
        Button historyRemove = SmallButton(historyPage, "Remove");
        Button historyClear = SmallButton(historyPage, "Clear all");
        historyFind.Accepting += (_, _) => RefreshHistory();
        _historySearch.Accepted += (_, _) => RefreshHistory();
        historyOpen.Accepting += (_, _) => OpenHistory();
        historyRemove.Accepting += (_, _) => RemoveHistory();
        historyClear.Accepting += (_, _) => ClearHistory();
        _historyList.Accepted += (_, _) => OpenHistory();

        View downloadsPage = Page("Downloads");
        _downloadList = List(downloadsPage, 0);
        _downloadList.SetSource(_downloads);
        Button downloadCancel = SmallButton(downloadsPage, "Cancel");
        Button downloadRetry = SmallButton(downloadsPage, "Retry");
        Button downloadRemove = SmallButton(downloadsPage, "Remove");
        Button downloadClear = SmallButton(downloadsPage, "Clear finished");
        downloadCancel.Accepting += (_, _) => CancelDownload();
        downloadRetry.Accepting += (_, _) => _ = RetryDownloadAsync();
        downloadRemove.Accepting += (_, _) => RemoveDownload();
        downloadClear.Accepting += (_, _) =>
        {
            _services.Downloads.ClearFinished();
            RefreshDownloads();
        };

        View passwordsPage = Page("Passwords");
        _credentialList = List(passwordsPage, 0);
        _credentialList.SetSource(_credentials);
        Button passwordAdd = SmallButton(passwordsPage, "Add");
        Button passwordEdit = SmallButton(passwordsPage, "Edit");
        Button passwordRemove = SmallButton(passwordsPage, "Remove");
        Button passwordClear = SmallButton(passwordsPage, "Clear all");
        passwordAdd.Accepting += (_, _) => AddCredential();
        passwordEdit.Accepting += (_, _) => EditCredential();
        passwordRemove.Accepting += (_, _) => RemoveCredential();
        passwordClear.Accepting += (_, _) => ClearCredentials();

        _tabs.Add(bookmarkPage, historyPage, downloadsPage, passwordsPage);
        View? initial = _tabs.TabCollection.FirstOrDefault(view =>
            view.Title.Equals(section, StringComparison.OrdinalIgnoreCase));
        if (initial is not null)
        {
            _tabs.Value = initial;
        }

        Button close = new CliButton
        {
            Text = "Close",
            X = Pos.AnchorEnd(11),
            Y = Pos.AnchorEnd(2),
            IsDefault = true,
            SchemeName = "Accent"
        };
        close.Accepting += (_, _) => App?.RequestStop(this);
        Add(_tabs, close);

        RefreshBookmarks();
        RefreshHistory();
        RefreshDownloads();
        RefreshCredentials();
        _services.Downloads.Changed += DownloadsChanged;
        Disposing += (_, _) => _services.Downloads.Changed -= DownloadsChanged;
        Initialized += (_, _) => FocusInitial(section);
    }

    public Uri? SelectedAddress { get; private set; }

    private void OpenBookmark()
    {
        BookmarkEntry? entry = Selected(_bookmarkList, _bookmarks);
        if (entry is not null)
        {
            SelectAddress(entry.Address);
        }
    }

    private void AddBookmark()
    {
        if (App is null)
        {
            return;
        }
        String? addressText = CliDialogs.Prompt(App, "Add bookmark", "Address");
        if (String.IsNullOrWhiteSpace(addressText))
        {
            return;
        }
        try
        {
            Uri address = LumuiClient.NormalizeAddress(addressText);
            String? title = CliDialogs.Prompt(App, "Add bookmark", "Title", address.Host);
            String? folder = CliDialogs.Prompt(App, "Add bookmark", "Folder", "Bookmarks");
            if (title is not null && folder is not null)
            {
                _services.Bookmarks.Add(address, title, folder);
                RefreshBookmarks();
            }
        }
        catch (Exception exception) when (exception is UriFormatException or LumuiProtocolException)
        {
            CliDialogs.Show(App, "Add bookmark", exception.Message);
        }
    }

    private void EditBookmark()
    {
        if (App is null || Selected(_bookmarkList, _bookmarks) is not BookmarkEntry entry)
        {
            return;
        }
        String? addressText = CliDialogs.Prompt(App, "Edit bookmark", "Address", entry.Address.AbsoluteUri);
        String? title = addressText is null ? null : CliDialogs.Prompt(App, "Edit bookmark", "Title", entry.Title);
        String? folder = title is null ? null : CliDialogs.Prompt(App, "Edit bookmark", "Folder", entry.Folder);
        if (addressText is null || title is null || folder is null)
        {
            return;
        }
        try
        {
            _services.Bookmarks.Update(entry.Address, LumuiClient.NormalizeAddress(addressText), title, folder);
            RefreshBookmarks();
        }
        catch (Exception exception) when (exception is UriFormatException or LumuiProtocolException)
        {
            CliDialogs.Show(App, "Edit bookmark", exception.Message);
        }
    }

    private void RemoveBookmark()
    {
        BookmarkEntry? entry = Selected(_bookmarkList, _bookmarks);
        if (entry is not null)
        {
            _services.Bookmarks.Remove(entry.Address);
            RefreshBookmarks();
        }
    }

    private void OpenHistory()
    {
        HistoryEntry? entry = Selected(_historyList, _history);
        if (entry is not null)
        {
            SelectAddress(entry.Address);
        }
    }

    private void RemoveHistory()
    {
        HistoryEntry? entry = Selected(_historyList, _history);
        if (entry is not null)
        {
            _services.History.Remove(entry);
            RefreshHistory();
        }
    }

    private void ClearHistory()
    {
        if (App is not null && CliDialogs.Confirm(App, "Clear history", "Remove all browsing history?"))
        {
            _services.History.Clear();
            RefreshHistory();
        }
    }

    private void CancelDownload()
    {
        DownloadItem? item = Selected(_downloadList, _downloads);
        if (item is not null)
        {
            _services.Downloads.Cancel(item.Id);
        }
    }

    private async Task RetryDownloadAsync()
    {
        DownloadItem? item = Selected(_downloadList, _downloads);
        if (item is null)
        {
            return;
        }
        try
        {
            await _services.Downloads.RetryAsync(item.Id, _services.Preferences.DownloadFolder).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException or InvalidOperationException)
        {
            App?.Invoke(() =>
            {
                if (App is not null)
                {
                    CliDialogs.Show(App, "Retry download", exception.Message);
                }
            });
        }
    }

    private void RemoveDownload()
    {
        DownloadItem? item = Selected(_downloadList, _downloads);
        if (item is not null)
        {
            _services.Downloads.Remove(item.Id);
            RefreshDownloads();
        }
    }

    private void AddCredential()
    {
        if (App is null || !_services.Credentials.IsAvailable)
        {
            return;
        }
        String? originText = CliDialogs.Prompt(App, "Add password", "Website");
        if (String.IsNullOrWhiteSpace(originText))
        {
            return;
        }
        try
        {
            Uri origin = LumuiClient.NormalizeAddress(originText);
            String? userName = CliDialogs.Prompt(App, "Add password", "User name");
            String? password = userName is null ? null : CliDialogs.Prompt(App, "Add password", "Password", secret: true);
            if (userName is not null && password is not null)
            {
                _services.Credentials.Save(origin, userName, password);
                RefreshCredentials();
            }
        }
        catch (Exception exception) when (exception is UriFormatException or LumuiProtocolException or CryptographicException)
        {
            CliDialogs.Show(App, "Add password", exception.Message);
        }
    }

    private void EditCredential()
    {
        if (App is null || Selected(_credentialList, _credentials) is not CredentialRecord entry)
        {
            return;
        }
        String? userName = CliDialogs.Prompt(App, "Edit password", "User name", entry.UserName);
        String? password = userName is null ? null : CliDialogs.Prompt(App, "Edit password", "Password", entry.Password, true);
        if (userName is null || password is null)
        {
            return;
        }
        _services.Credentials.Remove(entry.Origin, entry.UserName);
        _services.Credentials.Save(entry.Origin, userName, password);
        RefreshCredentials();
    }

    private void RemoveCredential()
    {
        CredentialRecord? entry = Selected(_credentialList, _credentials);
        if (entry is not null)
        {
            _services.Credentials.Remove(entry.Origin, entry.UserName);
            RefreshCredentials();
        }
    }

    private void ClearCredentials()
    {
        if (App is not null
            && _services.Credentials.IsAvailable
            && CliDialogs.Confirm(App, "Clear passwords", "Remove every saved password?"))
        {
            _services.Credentials.Clear();
            RefreshCredentials();
        }
    }

    private void SelectAddress(Uri address)
    {
        SelectedAddress = address;
        App?.RequestStop(this);
    }

    private void RefreshBookmarks()
    {
        String query = _bookmarkSearch.Text.Trim();
        Replace(_bookmarks, _services.Bookmarks.Entries.Where(entry =>
            query.Length == 0
            || entry.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || entry.Folder.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || entry.Address.AbsoluteUri.Contains(query, StringComparison.OrdinalIgnoreCase)));
    }

    private void RefreshHistory()
    {
        String query = _historySearch.Text.Trim();
        Replace(_history, _services.History.Entries.Where(entry =>
            query.Length == 0
            || entry.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || entry.Address.AbsoluteUri.Contains(query, StringComparison.OrdinalIgnoreCase)));
    }

    private void RefreshDownloads() => Replace(_downloads, _services.Downloads.Items);

    private void RefreshCredentials() => Replace(_credentials, _services.Credentials.GetAll());

    private void DownloadsChanged() => App?.Invoke(RefreshDownloads);

    private void FocusInitial(String section)
    {
        if (section.Equals("History", StringComparison.OrdinalIgnoreCase))
        {
            _historySearch.SetFocus();
        }
        else if (section.Equals("Downloads", StringComparison.OrdinalIgnoreCase))
        {
            _downloadList.SetFocus();
        }
        else if (section.Equals("Passwords", StringComparison.OrdinalIgnoreCase))
        {
            _credentialList.SetFocus();
        }
        else
        {
            _bookmarkSearch.SetFocus();
        }
    }

    private static View Page(String title) => new View
    {
        Title = title,
        CanFocus = true,
        TabStop = TabBehavior.NoStop
    };

    private static TextField SearchField(View page)
    {
        TextField search = new TextField
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(2)
        };
        page.Add(search);
        return search;
    }

    private static ListView List(View page, Int32 y = 2)
    {
        FrameView frame = new FrameView
        {
            Title = "Entries",
            X = 1,
            Y = y,
            Width = Dim.Fill(2),
            Height = Dim.Fill(4),
            CanFocus = true,
            TabStop = TabBehavior.NoStop
        };
        ListView list = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        frame.Add(list);
        page.Add(frame);
        return list;
    }

    private Button SmallButton(View page, String text)
    {
        if (!_actionBars.TryGetValue(page, out View? bar))
        {
            bar = new View
            {
                X = 1,
                Y = Pos.AnchorEnd(3),
                Width = Dim.Fill(2),
                Height = 2,
                SchemeName = "Menu",
                CanFocus = true,
                TabStop = TabBehavior.NoStop
            };
            _actionBars[page] = bar;
            bar.FrameChanged += (_, _) => LayoutActionBar(bar);
            page.Add(bar);
        }
        Button button = new CliButton
        {
            Text = text,
            Width = text.Length + 4
        };
        bar.Add(button);
        LayoutActionBar(bar);
        return button;
    }

    private static void LayoutActionBar(View bar)
    {
        Int32 available = Math.Max(1, bar.Viewport.Width);
        Int32 x = 0;
        Int32 y = 0;
        foreach (View child in bar.SubViews)
        {
            Int32 width = child.Text.Length + 4;
            if (x > 0 && x + width > available)
            {
                x = 0;
                y++;
            }
            child.Visible = y < 2;
            child.X = x;
            child.Y = y;
            child.Width = Math.Min(width, available);
            x += width + 1;
        }
    }

    private static T? Selected<T>(ListView list, ObservableCollection<T> values)
        where T : class =>
        list.Value is Int32 index && index >= 0 && index < values.Count ? values[index] : null;

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (T value in values)
        {
            target.Add(value);
        }
    }
}
