using System.Threading;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Lumui.Browser.Configuration;
using Lumui.Browser.Data;
using Lumui.Browser.Downloads;
using Lumui.Browser.Presentation;
using Lumui.Browser.Rendering;
using Lumui.Browser.Security;
using Lumui.Browser.Shell;

namespace Lumui.Browser.Views;

public sealed partial class BrowserLibraryWindow : Window, IDisposable
{
    private readonly BrowserPreferences _preferences;
    private readonly BrowserLibrarySection _section;
    private readonly BrowserShellRenderer? _shellRenderer;
    private readonly BrowserWindowPlacementStore _placementStore =
        new BrowserWindowPlacementStore();
    private readonly DispatcherTimer _searchTimer = new DispatcherTimer
    {
        Interval = TimeSpan.FromMilliseconds(140D)
    };
    private readonly IBrowserLibraryPage _page;
    private CancellationTokenSource? _readingCancellation;
    private Int32 _refreshQueued;
    private Boolean _disposed;

    public BrowserLibraryWindow()
        : this(
            BrowserLibrarySection.Bookmarks,
            BrowserApplicationServices.Current.Bookmarks,
            BrowserApplicationServices.Current.History,
            BrowserApplicationServices.Current.Downloads,
            BrowserApplicationServices.Current.Credentials,
            BrowserApplicationServices.Current.Preferences,
            null)
    {
    }

    public BrowserLibraryWindow(
        BrowserLibrarySection section,
        BookmarkStore bookmarks,
        HistoryStore history,
        DownloadManager downloads,
        ICredentialVault credentials,
        BrowserPreferences preferences,
        BrowserShellRenderer? shellRenderer = null)
    {
        _section = section;
        _preferences = preferences;
        _shellRenderer = shellRenderer;
        _page = CreatePage(
            section,
            bookmarks,
            history,
            downloads,
            credentials,
            preferences);
        InitializeComponent();
        ConnectPageEvents();
        SearchBox.TextChanged += SearchTextChanged;
        PrimaryActionButton.Click += PrimaryActionClicked;
        SecondaryActionButton.Click += SecondaryActionClicked;
        _searchTimer.Tick += SearchTimerTick;
        _page.Changed += PageChanged;
        _placementStore.Apply(this, PlacementKey());
        Closed += WindowClosed;
        KeyDown += WindowKeyDown;
        ConfigurePage();
        ApplyShellSemantics();
        RefreshAll();
    }

    public event Action<Uri>? OpenRequested;

    public event Action<String>? OpenDownloadRequested;

    public event Action<String>? OpenDownloadFolderRequested;

    private String Query => (SearchBox.Text ?? String.Empty).Trim();

    public void ApplyPreferences(BrowserPreferences preferences)
    {
        BrowserWindowAppearance.Apply(this, preferences);
        if (IsVisible)
        {
            ScheduleReadingFormat();
        }
    }

    public void RefreshAll()
    {
        BodyHost.Content = _page.Build(Query);
        PageSummaryText.Text = _page.Summary;
        PrimaryActionButton.IsEnabled = _page.PrimaryActionEnabled;
        SecondaryActionButton.IsEnabled = _page.SecondaryActionEnabled;
        ScheduleReadingFormat();
    }

    private void ScheduleReadingFormat()
    {
        _readingCancellation?.Cancel();
        _readingCancellation?.Dispose();
        CancellationTokenSource request = new CancellationTokenSource();
        _readingCancellation = request;
        _ = ApplyReadingFormatAsync(request);
    }

    private async Task ApplyReadingFormatAsync(
        CancellationTokenSource request)
    {
        try
        {
            await ReadingTextFormatter.ApplyTreeAsync(
                this,
                _preferences.BionicReading,
                request.Token);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_readingCancellation, request))
            {
                _readingCancellation = null;
            }
            request.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _readingCancellation?.Cancel();
        _readingCancellation?.Dispose();
        _readingCancellation = null;
        _searchTimer.Stop();
        _searchTimer.Tick -= SearchTimerTick;
        _page.Changed -= PageChanged;
        _page.Dispose();
    }

    private static IBrowserLibraryPage CreatePage(
        BrowserLibrarySection section,
        BookmarkStore bookmarks,
        HistoryStore history,
        DownloadManager downloads,
        ICredentialVault credentials,
        BrowserPreferences preferences) => section switch
    {
        BrowserLibrarySection.Bookmarks => new BookmarksManagerPage(bookmarks),
        BrowserLibrarySection.History => new HistoryManagerPage(history),
        BrowserLibrarySection.Downloads => new DownloadsManagerPage(
            downloads,
            preferences),
        BrowserLibrarySection.Passwords => new PasswordsManagerPage(credentials),
        _ => throw new ArgumentOutOfRangeException(nameof(section))
    };

    private void ConnectPageEvents()
    {
        if (_page is BookmarksManagerPage bookmarks)
        {
            bookmarks.OpenRequested += (Uri address) => OpenRequested?.Invoke(address);
        }
        else if (_page is HistoryManagerPage history)
        {
            history.OpenRequested += (Uri address) => OpenRequested?.Invoke(address);
        }
        else if (_page is DownloadsManagerPage downloads)
        {
            downloads.OpenFileRequested += (String path) =>
                OpenDownloadRequested?.Invoke(path);
            downloads.OpenFolderRequested += (String path) =>
                OpenDownloadFolderRequested?.Invoke(path);
        }
    }

    private String PlacementKey() =>
        "library." + _section.ToString().ToLowerInvariant();

    private void WindowClosed(Object? sender, EventArgs eventArgs)
    {
        _placementStore.Save(this, PlacementKey());
        Dispose();
    }

    private void ConfigurePage()
    {
        Title = _page.Title + " | Lumi";
        PageTitleText.Text = _page.Title;
        PageDescriptionText.Text = _page.Description;
        SearchBox.PlaceholderText = _page.SearchPlaceholder;
        PrimaryActionButton.IsVisible = _page.PrimaryActionText is not null;
        PrimaryActionButton.Content = _page.PrimaryActionText ?? String.Empty;
        SecondaryActionButton.IsVisible = _page.SecondaryActionText is not null;
        SecondaryActionButton.Content = _page.SecondaryActionText ?? String.Empty;
        AutomationProperties.SetName(this, _page.Title);
        AutomationProperties.SetName(
            PrimaryActionButton,
            _page.PrimaryActionText ?? String.Empty);
        AutomationProperties.SetName(
            SecondaryActionButton,
            _page.SecondaryActionText ?? String.Empty);
    }

    private void ApplyShellSemantics()
    {
        if (_shellRenderer is null)
        {
            return;
        }
        String prefix = _section.ToString().ToLowerInvariant();
        _shellRenderer.ApplyControl(SearchBox, prefix + ".search");
        if (_section == BrowserLibrarySection.Downloads)
        {
            _shellRenderer.ApplyButton(
                PrimaryActionButton,
                "downloads.openFolder",
                false);
        }
        else if (_section == BrowserLibrarySection.Passwords)
        {
            _shellRenderer.ApplyButton(
                PrimaryActionButton,
                "passwords.add",
                false);
        }
    }

    private void SearchTextChanged(
        Object? sender,
        Avalonia.Controls.TextChangedEventArgs eventArgs)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void SearchTimerTick(Object? sender, EventArgs eventArgs)
    {
        _searchTimer.Stop();
        RefreshAll();
    }

    private async void PrimaryActionClicked(
        Object? sender,
        Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        await _page.PrimaryActionAsync(this);
        RefreshAll();
    }

    private async void SecondaryActionClicked(
        Object? sender,
        Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        await _page.SecondaryActionAsync(this);
        RefreshAll();
    }

    private void PageChanged()
    {
        if (_disposed || Interlocked.Exchange(ref _refreshQueued, 1) == 1)
        {
            return;
        }
        Dispatcher.UIThread.Post(
            () =>
            {
                Volatile.Write(ref _refreshQueued, 0);
                if (!_disposed)
                {
                    RefreshAll();
                }
            },
            DispatcherPriority.Background);
    }

    private void WindowKeyDown(Object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyModifiers == KeyModifiers.Control && eventArgs.Key == Key.F)
        {
            eventArgs.Handled = true;
            SearchBox.Focus();
            SearchBox.SelectAll();
        }
        else if (eventArgs.KeyModifiers == KeyModifiers.Control && eventArgs.Key == Key.W)
        {
            eventArgs.Handled = true;
            Close();
        }
        else if (_page.HandleKeyDown(this, eventArgs))
        {
            eventArgs.Handled = true;
        }
    }

}
