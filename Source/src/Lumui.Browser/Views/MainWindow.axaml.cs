using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Lumui.Browser.Commands;
using Lumui.Browser.Configuration;
using Lumui.Browser.Controls;
using Lumui.Browser.Data;
using Lumui.Browser.DeveloperTools;
using Lumui.Browser.Downloads;
using Lumui.Browser.Navigation;
using Lumui.Browser.Presentation;
using Lumui.Browser.Rendering;
using Lumui.Browser.Security;
using Lumui.Browser.Shell;
using Lumui.Client;
using LumuiProtocol = Lumui.Client.LumuiProtocol;

namespace Lumui.Browser.Views;

public sealed partial class MainWindow : Window
{
    private const Int32 RetainedTabRenderings = 3;
    private readonly BrowserRequestMonitor _requestMonitor;
    private readonly BrowserApplicationServices _services;
    private readonly DeveloperToolsPanel _developerTools;
    private readonly BrowserMenuPanel _browserMenu;
    private readonly BrowserSettingsPanel _browserSettings;
    private readonly Dictionary<BrowserTabSession, BrowserTabHeader> _tabHeaders =
        new Dictionary<BrowserTabSession, BrowserTabHeader>();
    private readonly Dictionary<BrowserLibrarySection, BrowserLibraryWindow> _libraryWindows =
        new Dictionary<BrowserLibrarySection, BrowserLibraryWindow>();
    private readonly LinkedList<BrowserTabSession> _renderedTabOrder =
        new LinkedList<BrowserTabSession>();
    private readonly LumuiClient _client;
    private readonly BrowserTabManager _tabs;
    private readonly BrowserSessionStore _sessionStore;
    private readonly BookmarkStore _bookmarks;
    private readonly HistoryStore _visitHistory;
    private readonly DownloadManager _downloads;
    private readonly ICredentialVault _credentials;
    private readonly BrowserPreferencesStore _preferencesStore;
    private readonly BrowserPreferences _preferences;
    private readonly BrowserShellSurface _shellSurface;
    private readonly BrowserShellRenderer _shellRenderer;
    private readonly Button _personalizationButton;
    private readonly ContentControl _sidePanelHost;
    private readonly ProgressBar _loadingBar;
    private readonly Boolean _privateMode;
    private DeveloperToolsWindow? _developerToolsWindow;
    private BrowserSettingsWindow? _settingsWindow;
    private LoadedSurface? _displayedToolsSurface;
    private CancellationTokenSource? _preferenceRenderCancellation;
    private Boolean _closeConfirmed;
    private Boolean _closeConfirmationPending;
    private Boolean _cleanedUp;
    private Boolean _restoringSession;

    private BrowserTabSession ActiveTab => _tabs.Active
        ?? throw new InvalidOperationException("No browser tab is active.");

    private String BrowserWindowTitle => _privateMode
        ? "Private" + BrowserDefaults.WindowTitleSeparator + BrowserDefaults.WindowTitle
        : BrowserDefaults.WindowTitle;

    public MainWindow()
        : this(false)
    {
    }

    private MainWindow(Boolean privateMode)
    {
        _privateMode = privateMode;
        _services = BrowserApplicationServices.Current;
        _preferencesStore = _services.PreferencesStore;
        _preferences = _services.Preferences;
        _requestMonitor = new BrowserRequestMonitor();
        _bookmarks = _services.Bookmarks;
        _visitHistory = _services.History;
        _downloads = _services.Downloads;
        _credentials = _services.Credentials;
        _shellSurface = BrowserShellSurface.CreateDefault();
        _shellRenderer = new BrowserShellRenderer(_shellSurface);
        _developerTools = new DeveloperToolsPanel(_shellRenderer);
        PersonalizationPanel personalization = new PersonalizationPanel(
            _preferences,
            _shellRenderer);
        _browserMenu = new BrowserMenuPanel(_shellRenderer);
        _browserSettings = new BrowserSettingsPanel(
            _preferences,
            _shellRenderer,
            personalization);
        _client = new LumuiClient(
            observer: _requestMonitor,
            cookies: _privateMode ? null : _services.Cookies);
        _services.RegisterClient(_client);
        _tabs = new BrowserTabManager();
        _sessionStore = new BrowserSessionStore();
        InitializeComponent();
        _personalizationButton = this.FindControl<Button>(
            "PersonalizationButton")
            ?? throw new InvalidOperationException(
                "The reading settings button is missing from the browser shell.");
        _sidePanelHost = this.FindControl<ContentControl>("SidePanelHost")
            ?? throw new InvalidOperationException(
                "The side panel host is missing from the browser shell.");
        _loadingBar = this.FindControl<ProgressBar>("LoadingBar")
            ?? throw new InvalidOperationException(
                "The loading indicator is missing from the browser shell.");
        ModeText.Text = _privateMode ? "PRIVATE BROWSING" : "LUMUI BROWSER";
        _shellRenderer.ApplyButton(BackButton, "browser.back", false);
        _shellRenderer.ApplyButton(ForwardButton, "browser.forward", false);
        _shellRenderer.ApplyButton(ReloadButton, "browser.reload", false);
        _shellRenderer.ApplyButton(GoButton, "browser.open", false);
        AutomationProperties.SetName(AddressBox, BrowserText.WebsiteAddress);
        _shellRenderer.ApplyButton(HomeButton, "browser.home", false);
        _shellRenderer.ApplyButton(NewTabButton, "browser.newTab", false);
        _shellRenderer.ApplyToggleButton(BookmarkButton, "browser.bookmark", false);
        _shellRenderer.ApplyButton(_personalizationButton, "browser.reading", false);
        _shellRenderer.ApplyButton(DeveloperToolsButton, "browser.tools", false);
        _shellRenderer.ApplyToggleButton(BrowserMenuButton, "browser.menu", false);
        BackButton.Click += BackClicked;
        ForwardButton.Click += ForwardClicked;
        ReloadButton.Click += ReloadClicked;
        GoButton.Click += GoClicked;
        HomeButton.Click += HomeClicked;
        NewTabButton.Click += NewTabClicked;
        BookmarkButton.Click += BookmarkClicked;
        BrowserMenuButton.Click += BrowserMenuClicked;
        AddressBox.KeyDown += AddressKeyDown;
        _personalizationButton.Click += PersonalizationClicked;
        DeveloperToolsButton.Click += DeveloperToolsClicked;
        WorkspaceSplit.PaneClosed += WorkspaceSplitPaneClosed;
        _tabs.Changed += TabsChanged;
        _browserMenu.CommandRequested += BrowserCommandRequested;
        _browserSettings.PreferencesChanged += PreferencesChanged;
        _browserSettings.OpenPasswordsRequested += BrowserSettingsPasswordsRequested;
        _browserSettings.ClearBrowsingDataRequested +=
            BrowserSettingsClearBrowsingDataRequested;
        _developerTools.RequestsCleared += RequestsCleared;
        _requestMonitor.Recorded += RequestRecorded;
        _services.PreferencesChanged += SharedPreferencesChanged;
        _bookmarks.Changed += BookmarksChanged;
        KeyDown += WindowKeyDown;
        Closing += WindowClosing;
        Opened += WindowOpened;
        ApplyBrowserPreferences();
        UpdateHistoryControls();
    }

    private async void WindowOpened(Object? sender, EventArgs eventArgs)
    {
        _ = _client.WarmUpAsync();
        BrowserSessionState session = !_privateMode && _preferences.StartupMode
            == BrowserStartupMode.RestorePreviousSession
                ? await Task.Run(_sessionStore.Load)
                : new BrowserSessionState(Array.Empty<Uri>(), 0);
        IReadOnlyList<Uri> addresses = session.Addresses.Count > 0
            ? session.Addresses
            : new Uri[] { HomeAddress() };
        _restoringSession = true;
        try
        {
            foreach (Uri address in addresses)
            {
                BrowserTabSession tab = _tabs.Create();
                tab.RestoreAddress(address);
                UpdateTabHeader(tab);
            }
            _tabs.ActivateAt(
                session.Addresses.Count > 0 ? session.ActiveIndex : 0);
        }
        finally
        {
            _restoringSession = false;
        }
        EnsureActiveTabLoaded();
    }

    private async void GoClicked(Object? sender, RoutedEventArgs eventArgs) =>
        await OpenAddressAsync(AddressBox.Text, HistoryMode.Push);

    private async void HomeClicked(Object? sender, RoutedEventArgs eventArgs) =>
        await NavigateAsync(HomeAddress(), HistoryMode.Push);

    private async void NewTabClicked(Object? sender, RoutedEventArgs eventArgs) =>
        await CreateTabAsync(NewTabAddress());

    private void BookmarkClicked(Object? sender, RoutedEventArgs eventArgs) =>
        ToggleBookmark();

    private void BrowserMenuClicked(Object? sender, RoutedEventArgs eventArgs)
    {
        if (BrowserMenuButton.IsChecked != true)
        {
            CloseSidePanel();
            return;
        }
        ShowSidePanel(_browserMenu, 390D, BrowserMenuButton);
    }

    private async void AddressKeyDown(Object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Enter)
        {
            eventArgs.Handled = true;
            await OpenAddressAsync(AddressBox.Text, HistoryMode.Push);
        }
    }

    private async void BackClicked(Object? sender, RoutedEventArgs eventArgs) =>
        await MoveHistoryAsync(-1);

    private async void ForwardClicked(Object? sender, RoutedEventArgs eventArgs) =>
        await MoveHistoryAsync(1);

    private async void ReloadClicked(Object? sender, RoutedEventArgs eventArgs)
    {
        if (ActiveTab.History.Current is Uri address)
        {
            await NavigateAsync(address, HistoryMode.Reload);
        }
    }

    private async void WindowKeyDown(Object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyModifiers == KeyModifiers.Control && eventArgs.Key == Key.N)
        {
            eventArgs.Handled = true;
            new MainWindow().Show();
        }
        else if (eventArgs.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)
            && eventArgs.Key is Key.P or Key.N)
        {
            eventArgs.Handled = true;
            new MainWindow(true).Show();
        }
        else if (eventArgs.KeyModifiers == KeyModifiers.Control && eventArgs.Key == Key.T)
        {
            eventArgs.Handled = true;
            await CreateTabAsync(NewTabAddress());
        }
        else if (eventArgs.KeyModifiers == KeyModifiers.Control && eventArgs.Key == Key.W)
        {
            eventArgs.Handled = true;
            CloseTab(ActiveTab);
        }
        else if (eventArgs.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)
            && eventArgs.Key == Key.T)
        {
            eventArgs.Handled = true;
            Uri? address = _tabs.TakeLastClosedAddress();
            await CreateTabAsync(address ?? NewTabAddress());
        }
        else if (eventArgs.KeyModifiers == KeyModifiers.Control && eventArgs.Key == Key.Tab)
        {
            eventArgs.Handled = true;
            _tabs.Move(1);
        }
        else if (eventArgs.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)
            && eventArgs.Key == Key.Tab)
        {
            eventArgs.Handled = true;
            _tabs.Move(-1);
        }
        else if (eventArgs.KeyModifiers == KeyModifiers.Control
            && eventArgs.Key == Key.PageDown)
        {
            eventArgs.Handled = true;
            _tabs.Move(1);
        }
        else if (eventArgs.KeyModifiers == KeyModifiers.Control
            && eventArgs.Key == Key.PageUp)
        {
            eventArgs.Handled = true;
            _tabs.Move(-1);
        }
        else if (eventArgs.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)
            && eventArgs.Key == Key.W)
        {
            eventArgs.Handled = true;
            _closeConfirmed = true;
            Close();
        }
        else if (eventArgs.KeyModifiers == KeyModifiers.Control
            && eventArgs.Key >= Key.D1
            && eventArgs.Key <= Key.D8)
        {
            eventArgs.Handled = true;
            _tabs.ActivateAt((Int32)eventArgs.Key - (Int32)Key.D1);
        }
        else if (eventArgs.KeyModifiers == KeyModifiers.Control && eventArgs.Key == Key.D9)
        {
            eventArgs.Handled = true;
            _tabs.ActivateAt(-1);
        }
        else if (eventArgs.KeyModifiers == KeyModifiers.Control && eventArgs.Key == Key.H)
        {
            eventArgs.Handled = true;
            ShowLibrary(BrowserLibrarySection.History);
        }
        else if (eventArgs.KeyModifiers == KeyModifiers.Control && eventArgs.Key == Key.J)
        {
            eventArgs.Handled = true;
            ShowLibrary(BrowserLibrarySection.Downloads);
        }
        else if (eventArgs.KeyModifiers == KeyModifiers.Control && eventArgs.Key == Key.D)
        {
            eventArgs.Handled = true;
            ToggleBookmark();
        }
        else if (eventArgs.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)
            && eventArgs.Key == Key.O)
        {
            eventArgs.Handled = true;
            ShowLibrary(BrowserLibrarySection.Bookmarks);
        }
        else if (eventArgs.KeyModifiers == KeyModifiers.Control && eventArgs.Key == Key.OemComma)
        {
            eventArgs.Handled = true;
            ShowBrowserSettings();
        }
        else if (eventArgs.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)
            && eventArgs.Key == Key.Delete)
        {
            eventArgs.Handled = true;
            await ClearBrowsingDataAsync();
        }
        else if (eventArgs.KeyModifiers == KeyModifiers.Alt && eventArgs.Key == Key.Home)
        {
            eventArgs.Handled = true;
            await NavigateAsync(HomeAddress(), HistoryMode.Push);
        }
        else if (eventArgs.Key == Key.Escape)
        {
            eventArgs.Handled = true;
            if (WorkspaceSplit.IsPaneOpen)
            {
                CloseSidePanel();
            }
            else
            {
                ActiveTab.CancelNavigation();
            }
        }
        else if (eventArgs.Key == Key.F6)
        {
            eventArgs.Handled = true;
            AddressBox.Focus();
            AddressBox.SelectAll();
        }
        else if (eventArgs.Key == Key.F5)
        {
            eventArgs.Handled = true;
            if (ActiveTab.History.Current is Uri address)
            {
                await NavigateAsync(address, HistoryMode.Reload);
            }
        }
        else if (eventArgs.Key == Key.F11)
        {
            eventArgs.Handled = true;
            ToggleFullScreen();
        }
        else if (eventArgs.KeyModifiers == KeyModifiers.Alt && eventArgs.Key == Key.Left)
        {
            eventArgs.Handled = true;
            await MoveHistoryAsync(-1);
        }
        else if (eventArgs.KeyModifiers == KeyModifiers.Alt && eventArgs.Key == Key.Right)
        {
            eventArgs.Handled = true;
            await MoveHistoryAsync(1);
        }
        else if (eventArgs.KeyModifiers == KeyModifiers.Control && eventArgs.Key == Key.L)
        {
            eventArgs.Handled = true;
            AddressBox.Focus();
            AddressBox.SelectAll();
        }
        else if (eventArgs.KeyModifiers == KeyModifiers.Control && eventArgs.Key == Key.R)
        {
            eventArgs.Handled = true;
            if (ActiveTab.History.Current is Uri address)
            {
                await NavigateAsync(address, HistoryMode.Reload);
            }
        }
        else if (eventArgs.Key == Key.F12)
        {
            eventArgs.Handled = true;
            SetDeveloperToolsVisible(_developerToolsWindow is null);
        }
        else if (eventArgs.KeyModifiers == KeyModifiers.Control && eventArgs.Key == Key.U)
        {
            eventArgs.Handled = true;
            SetDeveloperToolsVisible(true);
            _developerTools.SelectSource();
        }
        else if (eventArgs.KeyModifiers == KeyModifiers.Control
            && eventArgs.Key is Key.OemPlus or Key.Add)
        {
            eventArgs.Handled = true;
            ChangePageZoom(10);
        }
        else if (eventArgs.KeyModifiers == KeyModifiers.Control
            && eventArgs.Key is Key.OemMinus or Key.Subtract)
        {
            eventArgs.Handled = true;
            ChangePageZoom(-10);
        }
        else if (eventArgs.KeyModifiers == KeyModifiers.Control
            && eventArgs.Key == Key.D0)
        {
            eventArgs.Handled = true;
            _preferences.PageZoomPercent = 100;
            PageZoomChanged();
        }
    }

    private async Task OpenAddressAsync(String? text, HistoryMode mode)
    {
        try
        {
            Uri uri = LumuiClient.NormalizeAddress(text ?? String.Empty);
            await NavigateAsync(uri, mode);
        }
        catch (Exception exception) when (exception is LumuiProtocolException or UriFormatException)
        {
            SetStatus(exception.Message);
            _developerTools.AddException(exception);
            ShowError(ActiveTab, BrowserText.AddressUnavailable, exception.Message);
        }
    }

    private async Task<Boolean> NavigateAsync(
        Uri address,
        HistoryMode mode,
        Uri? logicalAddress = null,
        BrowserTabSession? tab = null)
    {
        tab ??= ActiveTab;
        CancellationToken cancellationToken = tab.BeginNavigation();

        SetBusy(tab, true);
        SetStatus(tab, BrowserText.Loading);
        await Dispatcher.Yield(DispatcherPriority.Background);
        cancellationToken.ThrowIfCancellationRequested();
        Stopwatch stopwatch = Stopwatch.StartNew();
        LoadedSurface? loaded = null;
        Boolean adopted = false;
        try
        {
            loaded = logicalAddress is null
                ? await _client.LoadAsync(address, cancellationToken)
                : await _client.LoadRepresentationAsync(address, logicalAddress, cancellationToken);
            stopwatch.Stop();
            await RenderLoadedSurfaceAsync(
                tab,
                loaded,
                stopwatch.Elapsed,
                true,
                cancellationToken);
            adopted = true;
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            if (!tab.IsCurrentNavigation(cancellationToken))
            {
                return false;
            }
            tab.Address = loaded.Address;

            if (mode == HistoryMode.Push)
            {
                tab.History.Push(loaded.Address);
            }
            if (!_privateMode && _preferences.RememberHistory)
            {
                _visitHistory.Record(loaded.Address, tab.Title);
            }
            SetStatus(tab, BrowserText.Ready);
            if (ReferenceEquals(tab, _tabs.Active))
            {
                ShowActiveTab();
            }
            UpdateTabHeader(tab);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception) when (exception is LumuiProtocolException or HttpRequestException or TaskCanceledException)
        {
            stopwatch.Stop();
            SetStatus(tab, exception.Message);
            _developerTools.AddException(exception);
            ShowError(tab, BrowserText.PageUnavailable, exception.Message);
            return false;
        }
        finally
        {
            if (!adopted)
            {
                loaded?.Dispose();
                if (tab.IsCurrentNavigation(cancellationToken))
                {
                    tab.Renderer?.ResumeDeferredWork();
                }
            }
            if (tab.IsCurrentNavigation(cancellationToken))
            {
                SetBusy(tab, false);
            }
        }
    }

private async Task OpenExternalAsync(BrowserTabSession tab, Uri uri)
    {
        ILauncher? launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (launcher is not null && await launcher.LaunchUriAsync(uri))
        {
            return;
        }
        SetStatus(tab, BrowserText.NoAddressHandler);
    }

    private async Task InvokeLoadedActionAsync(
        LoadedSurface loaded,
        String componentId,
        String actionId,
        IReadOnlyDictionary<String, Object?> input)
    {
        BrowserTabSession tab = _tabs.Tabs.FirstOrDefault(
            (BrowserTabSession candidate) => ReferenceEquals(candidate.Loaded, loaded))
            ?? ActiveTab;
        CredentialSubmission? submittedCredential =
            CredentialFieldResolver.FindSubmission(
                loaded.Document.RootElement,
                input);
        SurfaceActionPolicy actionPolicy = SurfaceActionPolicy.FromSurface(
            loaded.Document.RootElement,
            actionId);
        Boolean sensitivePermission = _preferences.AskBeforeSensitivePermissions
            && SensitiveComponentDetector.IsSensitiveAction(
                loaded.Document.RootElement,
                componentId);
        if ((actionPolicy.Confirmation
                == LumuiProtocol.ConfirmationPolicies.Implicit
                || sensitivePermission)
            && !await ConfirmAsync(
                sensitivePermission ? "Allow access" : BrowserText.ConfirmAction,
                sensitivePermission
                    ? "Allow this page to use the requested device capability?"
                    : BrowserText.ContinueQuestion))
        {
            SetStatus(tab, BrowserText.ActionCancelled);
            return;
        }

        SetStatus(tab, BrowserText.Working);
        CancellationToken cancellationToken = tab.BeginAction();
        RendererSettings settings = CurrentSettings();
        try
        {
            using ActionResult first = await _client.InvokeAsync(
                loaded,
                componentId,
                actionId,
                input,
                settings.Profile.Id,
                LumuiProtocol.InputMethods.Native,
                cancellationToken: cancellationToken);
            ActionResult result = first;
            ActionResult? confirmedResult = null;
            ActionResult? completedResult = null;
            try
            {
                if (first.Status == LumuiProtocol.ActionStatuses.RequiresConfirmation)
                {
                    if (!await ConfirmAsync(
                            BrowserText.ConfirmAction,
                            BrowserText.ContinueQuestion))
                    {
                        SetStatus(tab, BrowserText.ActionCancelled);
                        return;
                    }
                    String? token = first.ConfirmationToken();
                    if (String.IsNullOrWhiteSpace(token))
                    {
                        throw new LumuiProtocolException(
                            BrowserText.MissingConfirmation);
                    }
                    confirmedResult = await _client.InvokeAsync(
                        loaded,
                        componentId,
                        actionId,
                        input,
                        settings.Profile.Id,
                        LumuiProtocol.InputMethods.Native,
                        confirmed: true,
                        messageId: first.CorrelationId,
                        confirmationToken: token,
                        cancellationToken: cancellationToken);
                    result = confirmedResult;
                }

                if (result.Status == LumuiProtocol.ActionStatuses.AcceptedAsync)
                {
                    SetStatus(tab, BrowserText.Waiting);
                    completedResult = await _client.WaitForCompletionAsync(result, cancellationToken);
                    result = completedResult;
                }

                await OfferToSaveCredentialAsync(
                    tab,
                    loaded.Address,
                    submittedCredential);

                Uri? redirect = result.RedirectUri(result.ResponseUri);
                if (redirect is not null)
                {
                    await NavigateAsync(redirect, HistoryMode.Push, tab: tab);
                    return;
                }
                Uri? surface = result.SurfaceUri(result.ResponseUri);
                if (surface is not null)
                {
                    await NavigateAsync(
                        surface,
                        HistoryMode.Reload,
                        loaded.Address,
                        tab);
                    return;
                }
                SetStatus(tab, result.Message() ?? BrowserText.ActionCompleted);
            }
            finally
            {
                completedResult?.Dispose();
                confirmedResult?.Dispose();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is LumuiProtocolException or HttpRequestException)
        {
            SetStatus(tab, exception.Message);
            _developerTools.AddException(exception);
        }
    }

    private async Task<Boolean> ConfirmAsync(String title, String message)
    {
        return await BrowserConfirmationDialog.ShowAsync(
            this,
            title,
            message,
            BrowserText.Continue);
    }

    private async Task OfferToSaveCredentialAsync(
        BrowserTabSession tab,
        Uri origin,
        CredentialSubmission? submission)
    {
        if (_privateMode
            || !_preferences.OfferToSavePasswords
            || !_credentials.IsAvailable
            || submission is null
            || String.IsNullOrWhiteSpace(submission.UserName))
        {
            return;
        }
        CredentialRecord? existing = _credentials.Find(
            origin,
            submission.UserName);
        if (existing?.Password == submission.Password)
        {
            return;
        }
        String verb = existing is null ? "Save" : "Update";
        if (!await ConfirmAsync(
                verb + " password",
                $"{verb} the password for {submission.UserName} on {origin.Host}?"))
        {
            return;
        }
        try
        {
            _credentials.Save(
                origin,
                submission.UserName,
                submission.Password);
            RefreshLibraryWindows();
            SetStatus(tab, "Password saved");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or System.Security.Cryptography.CryptographicException)
        {
            SetStatus(tab, "The password could not be saved.");
            _developerTools.AddException(exception);
        }
    }

    private async Task CreateTabAsync(Uri? address)
    {
        BrowserTabSession tab = _tabs.Create();
        if (address is null)
        {
            tab.SetBlank();
            AddressBox.Focus();
            return;
        }
        await Dispatcher.Yield(DispatcherPriority.Background);
        if (!_tabs.Tabs.Contains(tab))
        {
            return;
        }
        await NavigateAsync(address, HistoryMode.Push, tab: tab);
    }

    private async void CloseTab(BrowserTabSession tab)
    {
        Boolean replacingLastTab = _tabs.Tabs.Count == 1;
        tab.CancelPendingWork();
        BrowserTabSession active = _tabs.Close(tab);
        QueueTabDisposal(tab);
        if (replacingLastTab)
        {
            Uri? address = NewTabAddress();
            if (address is null)
            {
                AddressBox.Focus();
            }
            else
            {
                await Dispatcher.Yield(DispatcherPriority.Background);
                if (!_tabs.Tabs.Contains(active))
                {
                    return;
                }
                await NavigateAsync(address, HistoryMode.Push, tab: active);
            }
        }
    }

    private void TabsChanged()
    {
        SynchronizeTabs();
        UpdateRenderedTabSet();
        ShowActiveTab();
        if (!_restoringSession)
        {
            EnsureActiveTabLoaded();
        }
    }

    private void UpdateRenderedTabSet()
    {
        HashSet<BrowserTabSession> live = _tabs.Tabs.ToHashSet();
        LinkedListNode<BrowserTabSession>? node = _renderedTabOrder.First;
        while (node is not null)
        {
            LinkedListNode<BrowserTabSession>? next = node.Next;
            if (!live.Contains(node.Value))
            {
                _renderedTabOrder.Remove(node);
            }
            node = next;
        }
        if (_tabs.Active is BrowserTabSession active)
        {
            _renderedTabOrder.Remove(active);
            _renderedTabOrder.AddFirst(active);
        }
        foreach (BrowserTabSession tab in _tabs.Tabs)
        {
            if (ReferenceEquals(tab, _tabs.Active))
            {
                tab.Renderer?.ResumeDeferredWork();
                continue;
            }
            tab.Renderer?.PauseDeferredWork();
            if (tab.Content is not null && !_renderedTabOrder.Contains(tab))
            {
                _renderedTabOrder.AddLast(tab);
            }
        }
        while (_renderedTabOrder.Count > RetainedTabRenderings)
        {
            BrowserTabSession tab = _renderedTabOrder.Last!.Value;
            _renderedTabOrder.RemoveLast();
            if (ReferenceEquals(tab, _tabs.Active))
            {
                _renderedTabOrder.AddFirst(tab);
                continue;
            }
            tab.ViewportOffset = tab.Renderer?.DocumentViewport?.Offset
                ?? tab.ViewportOffset;
            tab.ReleaseRendering();
        }
    }

    private Boolean ShouldRetainRendering(BrowserTabSession tab)
    {
        Int32 position = 0;
        foreach (BrowserTabSession candidate in _renderedTabOrder)
        {
            if (ReferenceEquals(candidate, tab))
            {
                return position < RetainedTabRenderings;
            }
            position++;
        }
        return false;
    }

    private void EnsureActiveTabLoaded()
    {
        BrowserTabSession? tab = _tabs.Active;
        if (tab is null || tab.IsBusy)
        {
            return;
        }
        if (tab.Loaded is LoadedSurface loaded && tab.Content is null)
        {
            _ = RestoreTabRenderingAsync(tab, loaded);
            return;
        }
        if (tab.Address is not Uri address
            || tab.IsBusy
            || tab.Loaded is not null
            || tab.Content is not null)
        {
            return;
        }
        _ = NavigateAsync(
            address,
            HistoryMode.Reload,
            tab: tab);
    }

    private async Task RestoreTabRenderingAsync(
        BrowserTabSession tab,
        LoadedSurface loaded)
    {
        CancellationToken cancellationToken = tab.BeginNavigation();
        SetBusy(tab, true);
        try
        {
            await RenderLoadedSurfaceAsync(
                tab,
                loaded,
                TimeSpan.Zero,
                false,
                cancellationToken);
            if (ReferenceEquals(tab, _tabs.Active))
            {
                ShowActiveTab();
            }
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (tab.IsCurrentNavigation(cancellationToken))
            {
                SetBusy(tab, false);
            }
        }
    }

    private void SynchronizeTabs()
    {
        HashSet<BrowserTabSession> live = _tabs.Tabs.ToHashSet();
        foreach (BrowserTabSession removed in _tabHeaders.Keys
            .Where((BrowserTabSession tab) => !live.Contains(tab))
            .ToArray())
        {
            BrowserTabHeader header = _tabHeaders[removed];
            TabsHost.Children.Remove(header.Root);
            _tabHeaders.Remove(removed);
        }
        for (Int32 index = 0; index < _tabs.Tabs.Count; index++)
        {
            BrowserTabSession tab = _tabs.Tabs[index];
            if (!_tabHeaders.TryGetValue(tab, out BrowserTabHeader? header))
            {
                header = new BrowserTabHeader(
                    tab,
                    (BrowserTabSession selected) => _tabs.Activate(selected),
                    CloseTab);
                _tabHeaders.Add(tab, header);
                TabsHost.Children.Insert(
                    Math.Min(index, TabsHost.Children.Count),
                    header.Root);
            }
            Int32 currentIndex = TabsHost.Children.IndexOf(header.Root);
            if (currentIndex != index)
            {
                TabsHost.Children.Remove(header.Root);
                TabsHost.Children.Insert(index, header.Root);
            }
            header.Update(
                ReferenceEquals(tab, _tabs.Active),
                _preferences.BionicReading);
        }
    }

    private void UpdateTabHeader(BrowserTabSession tab)
    {
        if (_tabHeaders.TryGetValue(tab, out BrowserTabHeader? header))
        {
            header.Update(
                ReferenceEquals(tab, _tabs.Active),
                _preferences.BionicReading);
        }
    }

    private static void QueueTabDisposal(BrowserTabSession tab)
    {
        Dispatcher.UIThread.Post(tab.Dispose, DispatcherPriority.Background);
    }

    private void ShowActiveTab()
    {
        BrowserTabSession? tab = _tabs.Active;
        if (tab is null)
        {
            return;
        }
        if (!ReferenceEquals(SurfaceHost.Content, tab.Content))
        {
            ApplyDocumentViewport(tab.Loaded, tab.Renderer);
            SurfaceHost.Content = tab.Content;
        }
        String address = tab.Address?.AbsoluteUri ?? String.Empty;
        if (!String.Equals(AddressBox.Text, address, StringComparison.Ordinal))
        {
            AddressBox.Text = address;
        }
        if (!String.Equals(StatusText.Text, tab.Status, StringComparison.Ordinal))
        {
            StatusText.Text = tab.Status;
        }
        if (!String.Equals(
                DocumentInfoText.Text,
                tab.DocumentInfo,
                StringComparison.Ordinal))
        {
            DocumentInfoText.Text = tab.DocumentInfo;
        }
        Title = (String.IsNullOrWhiteSpace(tab.Title) ? "New tab" : tab.Title)
            + BrowserDefaults.WindowTitleSeparator
            + BrowserWindowTitle;
        SetBusy(tab, tab.IsBusy);
        UpdateHistoryControls();
        UpdateBookmarkButton();
        if (tab.Loaded is not null
            && !ReferenceEquals(_displayedToolsSurface, tab.Loaded))
        {
            _developerTools.SetSurface(
                tab.Loaded,
                CurrentSettings(),
                tab.LoadDuration);
            _displayedToolsSurface = tab.Loaded;
        }
        ReadingTextFormatter.Apply(
            StatusText,
            tab.Status,
            _preferences.BionicReading);
        ReadingTextFormatter.Apply(
            DocumentInfoText,
            tab.DocumentInfo,
            _preferences.BionicReading);
    }

    private void ToggleBookmark()
    {
        BrowserTabSession tab = ActiveTab;
        if (tab.Address is null)
        {
            return;
        }
        try
        {
            if (_bookmarks.Contains(tab.Address))
            {
                _bookmarks.Remove(tab.Address);
                SetStatus(tab, "Bookmark removed");
            }
            else
            {
                _bookmarks.Add(tab.Address, tab.Title);
                SetStatus(tab, "Bookmark saved");
            }
            UpdateBookmarkButton();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            SetStatus(tab, "The bookmark could not be saved.");
            _developerTools.AddException(exception);
        }
    }

    private void BookmarksChanged() =>
        Dispatcher.UIThread.Post(() =>
        {
            if (_tabs.Active is not null)
            {
                UpdateBookmarkButton();
            }
        });

    private void UpdateBookmarkButton()
    {
        Boolean bookmarked = ActiveTab.Address is Uri address
            && _bookmarks.Contains(address);
        BookmarkButton.IsChecked = bookmarked;
        BookmarkButton.Content = new FontAwesomeIcon
        {
            Icon = bookmarked
                ? BrowserIcons.BookmarkFilled
                : BrowserIcons.Bookmark,
            IconSize = 16D
        };
    }

    private Uri HomeAddress() => LumuiClient.NormalizeAddress(_preferences.HomePage);

    private Uri? NewTabAddress() => _preferences.NewTabMode switch
    {
        BrowserNewTabMode.Blank => null,
        BrowserNewTabMode.Custom => LumuiClient.NormalizeAddress(_preferences.NewTabPage),
        _ => HomeAddress()
    };

    private async void BrowserCommandRequested(BrowserCommand command)
    {
        switch (command)
        {
            case BrowserCommand.NewTab:
                CloseSidePanel();
                await CreateTabAsync(NewTabAddress());
                break;
            case BrowserCommand.NewWindow:
                CloseSidePanel();
                new MainWindow().Show();
                break;
            case BrowserCommand.NewPrivateWindow:
                CloseSidePanel();
                new MainWindow(true).Show();
                break;
            case BrowserCommand.ZoomOut:
                ChangePageZoom(-10);
                break;
            case BrowserCommand.ZoomReset:
                _preferences.PageZoomPercent = 100;
                PageZoomChanged();
                break;
            case BrowserCommand.ZoomIn:
                ChangePageZoom(10);
                break;
            case BrowserCommand.FullScreen:
                ToggleFullScreen();
                CloseSidePanel();
                break;
            case BrowserCommand.Bookmarks:
                ShowLibrary(BrowserLibrarySection.Bookmarks);
                break;
            case BrowserCommand.History:
                ShowLibrary(BrowserLibrarySection.History);
                break;
            case BrowserCommand.Downloads:
                ShowLibrary(BrowserLibrarySection.Downloads);
                break;
            case BrowserCommand.Passwords:
                ShowLibrary(BrowserLibrarySection.Passwords);
                break;
            case BrowserCommand.Settings:
                ShowBrowserSettings();
                break;
            case BrowserCommand.DeveloperTools:
                SetDeveloperToolsVisible(true);
                break;
        }
    }

    private async void LibraryOpenRequested(Uri address)
    {
        CloseSidePanel();
        await CreateTabAsync(address);
        RestoreAndActivate(this);
    }

    private async Task StartDownloadAsync(Uri address)
    {
        String folder = _preferences.DownloadFolder;
        if (_preferences.AskWhereToSaveDownloads)
        {
            IReadOnlyList<IStorageFolder> selected = await StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "Choose a download folder",
                    AllowMultiple = false
                });
            if (selected.Count == 0)
            {
                return;
            }
            folder = selected[0].Path.LocalPath;
        }
        ShowLibrary(BrowserLibrarySection.Downloads);
        SetStatus("Downloading " + Path.GetFileName(address.LocalPath));
        try
        {
            DownloadItem item = await _downloads.StartAsync(address, folder);
            SetStatus(item.Status == DownloadStatus.Completed
                ? "Download complete"
                : "Download stopped");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or HttpRequestException)
        {
            SetStatus("The download could not be started. " + exception.Message);
            _developerTools.AddException(exception);
        }
    }

    private async void OpenDownloadedFile(String path)
    {
        if (!File.Exists(path)
            || !await Launcher.LaunchFileInfoAsync(new FileInfo(path)))
        {
            SetStatus("The downloaded file could not be opened.");
        }
    }

    private async void OpenDownloadFolder(String path)
    {
        if (!Directory.Exists(path)
            || !await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path)))
        {
            SetStatus("The download folder could not be opened.");
        }
    }

    private void ShowLibrary(BrowserLibrarySection section)
    {
        CloseSidePanel();
        if (_libraryWindows.TryGetValue(section, out BrowserLibraryWindow? existing))
        {
            RestoreAndActivate(existing);
            return;
        }
        BrowserLibraryWindow window = new BrowserLibraryWindow(
            section,
            _bookmarks,
            _visitHistory,
            _downloads,
            _credentials,
            _preferences,
            _shellRenderer);
        window.OpenRequested += LibraryOpenRequested;
        window.OpenDownloadRequested += OpenDownloadedFile;
        window.OpenDownloadFolderRequested += OpenDownloadFolder;
        window.ApplyPreferences(_preferences);
        window.Closed += (_, _) =>
        {
            window.OpenRequested -= LibraryOpenRequested;
            window.OpenDownloadRequested -= OpenDownloadedFile;
            window.OpenDownloadFolderRequested -= OpenDownloadFolder;
            _libraryWindows.Remove(section);
        };
        _libraryWindows[section] = window;
        window.Show(this);
    }

    private void ShowBrowserSettings()
    {
        CloseSidePanel();
        _browserSettings.Refresh();
        if (_settingsWindow is not null)
        {
            RestoreAndActivate(_settingsWindow);
            return;
        }
        BrowserSettingsWindow window = new BrowserSettingsWindow(_browserSettings);
        _settingsWindow = window;
        window.ApplyPreferences(_preferences);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_settingsWindow, window))
            {
                _settingsWindow = null;
            }
        };
        window.Show(this);
    }

    private void ToggleFullScreen()
    {
        WindowState = WindowState == WindowState.FullScreen
            ? WindowState.Normal
            : WindowState.FullScreen;
    }

    private async Task ClearBrowsingDataAsync(Window? owner = null)
    {
        if (!await BrowserConfirmationDialog.ShowAsync(
                owner ?? this,
                "Clear browsing data",
                "Remove local history, cookies and temporary network data?",
                "Clear data",
                true))
        {
            return;
        }
        _visitHistory.Clear();
        _services.ClearBrowsingData();
        RefreshLibraryWindows();
        SetStatus("Browsing data cleared");
        CloseSidePanel();
    }

    private async Task MoveHistoryAsync(Int32 offset)
    {
        BrowserTabSession tab = ActiveTab;
        if (!tab.History.TryPeek(offset, out Uri? address) || address is null)
        {
            return;
        }
        if (await NavigateAsync(address, HistoryMode.History))
        {
            tab.History.TryMove(offset);
            UpdateHistoryControls();
        }
    }

    private void UpdateHistoryControls()
    {
        BrowserTabSession? tab = _tabs.Active;
        BackButton.IsEnabled = tab?.History.CanMoveBack == true;
        ForwardButton.IsEnabled = tab?.History.CanMoveForward == true;
    }

    private void SetBusy(BrowserTabSession tab, Boolean busy)
    {
        tab.IsBusy = busy;
        if (ReferenceEquals(tab, _tabs.Active))
        {
            ReloadButton.IsEnabled = !busy && tab.History.Current is not null;
            GoButton.IsEnabled = true;
            AddressBox.IsEnabled = true;
            _loadingBar.IsIndeterminate = busy && !_preferences.ReducedMotion;
            _loadingBar.Value = busy && _preferences.ReducedMotion ? 40D : 0D;
            _loadingBar.IsVisible = busy;
        }
    }

    private void SetStatus(String value) => SetStatus(ActiveTab, value);

    private void SetStatus(BrowserTabSession tab, String value)
    {
        tab.Status = value;
        if (ReferenceEquals(tab, _tabs.Active))
        {
            StatusText.Text = value;
            ReadingTextFormatter.ApplyTree(
                StatusText,
                _preferences.BionicReading);
        }
    }

    private RendererSettings CurrentSettings()
    {
        AppearanceDefinition appearance = AppearanceCatalog.ForBrowser(
            _preferences.ColorScheme,
            _preferences.Font,
            _preferences.FontFamily,
            _preferences.ColorVision,
            _preferences.HighContrast,
            _preferences.AccentColor);
        OutputModeDefinition output = _preferences.SimpleReadingView
            ? OutputModeCatalog.ScreenReader
            : OutputModeCatalog.Visual;
        InteractionModeDefinition interaction = _preferences.SeniorMode
            ? InteractionModeCatalog.Guided
            : InteractionModeCatalog.Standard;
        return new RendererSettings(
            DeviceProfileCatalog.Desktop,
            appearance,
            output,
            interaction,
            _preferences.TextScale,
            _preferences.PageScale,
            _preferences.HighContrast,
            _preferences.ReducedMotion,
            _preferences.BionicReading,
            _preferences.ColorVision);
    }

    private void PreferencesChanged()
    {
        ApplyBrowserPreferences();
        PersistPreferences();
        _browserSettings.Refresh();
        ScheduleActiveSurfaceRender();
    }

    private void PersistPreferences()
    {
        _ = PersistPreferencesAsync();
    }

    private async Task PersistPreferencesAsync()
    {
        try
        {
            await _preferencesStore.SaveAsync(_preferences);
            SetStatus(BrowserText.PreferencesSaved);
            _services.NotifyPreferencesChanged(this);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus(BrowserText.PreferencesNotSaved);
            _developerTools.AddException(exception);
        }
    }

    private void SharedPreferencesChanged(Object source)
    {
        if (ReferenceEquals(source, this))
        {
            return;
        }
        ApplyBrowserPreferences();
        _browserSettings.Refresh();
        ScheduleActiveSurfaceRender();
    }

    private void ScheduleActiveSurfaceRender()
    {
        _preferenceRenderCancellation?.Cancel();
        CancellationTokenSource request = new CancellationTokenSource();
        _preferenceRenderCancellation = request;
        _ = RenderPreferencesAsync(request);
    }

    private async Task RenderPreferencesAsync(CancellationTokenSource request)
    {
        try
        {
            await Task.Delay(80, request.Token);
            if (!_cleanedUp
                && _tabs.Active is BrowserTabSession tab
                && tab.Loaded is LoadedSurface loaded)
            {
                await RenderLoadedSurfaceAsync(
                    tab,
                    loaded,
                    TimeSpan.Zero,
                    false,
                    request.Token);
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_preferenceRenderCancellation, request))
            {
                _preferenceRenderCancellation = null;
            }
            request.Dispose();
        }
    }

    private async Task RenderLoadedSurfaceAsync(
        BrowserTabSession tab,
        LoadedSurface loaded,
        TimeSpan loadDuration,
        Boolean newDocument,
        CancellationToken cancellationToken)
    {
        if (newDocument)
        {
            _preferenceRenderCancellation?.Cancel();
        }
        RendererSettings settings = CurrentSettings();
        Vector previousOffset = tab.Renderer?.DocumentViewport?.Offset
            ?? tab.ViewportOffset;
        CredentialRecord? credential = !_privateMode && _preferences.AutoFillPasswords
            ? await Task.Run(
                () => _credentials.FindForOrigin(loaded.Address),
                cancellationToken)
            : null;
        LumuiRenderer renderer = new LumuiRenderer(
            _client,
            loaded.SurfaceUri,
            settings,
            async (Uri uri) =>
            {
                await NavigateAsync(uri, HistoryMode.Push, tab: tab);
            },
            (Uri uri) => OpenExternalAsync(tab, uri),
            (String componentId, String actionId, IReadOnlyDictionary<String, Object?> input) =>
                InvokeLoadedActionAsync(loaded, componentId, actionId, input),
            (String value) => SetStatus(tab, value),
            inputSuggestion: credential is null
                ? null
                : (JsonElement component) =>
                    CredentialFieldResolver.SuggestedValue(component, credential),
            download: StartDownloadAsync);
        Boolean committed = false;
        try
        {
            Control content = await renderer.RenderAsync(
                loaded.Document.RootElement,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (newDocument)
            {
                tab.LoadDuration = loadDuration;
                tab.SetDocument(loaded, renderer, content);
            }
            else
            {
                tab.ReplaceRendering(renderer, content);
            }
            committed = true;
            tab.Title = renderer.DocumentTitle;
            tab.DocumentInfo =
                loaded.Address.Host
                + (settings.Interaction.Mode == InteractionMode.Guided
                    ? BrowserText.ContextSeparator + settings.Interaction.Label
                    : String.Empty)
                + (settings.Output.Mode == OutputMode.ScreenReader
                    ? BrowserText.ContextSeparator + settings.Output.Label
                    : String.Empty);
            if (ReferenceEquals(tab, _tabs.Active))
            {
                Boolean ownsViewport = ApplyDocumentViewport(loaded, renderer);
                SurfaceHost.Content = content;
                if (newDocument)
                {
                    tab.ViewportOffset = default(Vector);
                    if (renderer.DocumentViewport is ScrollViewer initialViewport)
                    {
                        initialViewport.Offset = default(Vector);
                    }
                    ContentScroll.Offset = default(Vector);
                }
                else if (renderer.DocumentViewport is ScrollViewer viewport)
                {
                    Dispatcher.UIThread.Post(
                        () =>
                        {
                            viewport.Offset = previousOffset;
                            tab.ViewportOffset = previousOffset;
                        },
                        DispatcherPriority.Loaded);
                }
                else if (!ownsViewport)
                {
                    ContentScroll.Offset = previousOffset;
                }
                Title = tab.Title
                    + BrowserDefaults.WindowTitleSeparator
                    + BrowserWindowTitle;
                DocumentInfoText.Text = tab.DocumentInfo;
            }
            if (!newDocument && ReferenceEquals(tab, _tabs.Active))
            {
                _developerTools.SetSettings(loaded, settings);
            }
            if (!ReferenceEquals(tab, _tabs.Active))
            {
                renderer.PauseDeferredWork();
                if (!ShouldRetainRendering(tab))
                {
                    tab.ViewportOffset = renderer.DocumentViewport?.Offset
                        ?? tab.ViewportOffset;
                    tab.ReleaseRendering();
                }
            }
        }
        finally
        {
            if (!committed)
            {
                renderer.Dispose();
            }
        }
    }

    private void ApplyBrowserPreferences()
    {
        _client.SetDoNotTrack(_preferences.SendDoNotTrack);
        _browserMenu.SetZoom(_preferences.PageZoomPercent);
        ReadingTextFormatter.ApplyTree(
            _browserMenu,
            _preferences.BionicReading);
        BrowserWindowAppearance.Apply(this, _preferences);
        AddressBox.FontSize = 16D;
        _developerTools.SetDarkMode(
            _preferences.ColorScheme == BrowserColorScheme.Dark,
            _preferences.HighContrast);
        _browserMenu.SetDarkMode(
            _preferences.ColorScheme == BrowserColorScheme.Dark,
            _preferences.HighContrast);
        _browserSettings.SetDarkMode(
            _preferences.ColorScheme == BrowserColorScheme.Dark,
            _preferences.HighContrast);
        _developerToolsWindow?.ApplyPreferences(_preferences);
        _settingsWindow?.ApplyPreferences(_preferences);
        foreach (BrowserLibraryWindow window in _libraryWindows.Values)
        {
            window.ApplyPreferences(_preferences);
        }
    }

    private void ChangePageZoom(Int32 amount)
    {
        _preferences.PageZoomPercent = PageZoomCatalog.Next(
            _preferences.PageZoomPercent,
            amount);
        PageZoomChanged();
    }

    private void PageZoomChanged()
    {
        _browserMenu.SetZoom(_preferences.PageZoomPercent);
        ReadingTextFormatter.ApplyTree(
            _browserMenu,
            _preferences.BionicReading);
        PersistPreferences();
        ScheduleActiveSurfaceRender();
        if (_tabs.Active?.Loaded is LoadedSurface loaded)
        {
            _developerTools.SetSettings(loaded, CurrentSettings());
        }
    }

    private void PersonalizationClicked(Object? sender, RoutedEventArgs eventArgs)
    {
        ShowReadingSettings();
    }

    private void ShowReadingSettings()
    {
        _browserSettings.ShowTextSettings();
        ShowBrowserSettings();
    }

    private void DeveloperToolsClicked(Object? sender, RoutedEventArgs eventArgs)
    {
        SetDeveloperToolsVisible(true);
    }

    private void SetDeveloperToolsVisible(Boolean visible)
    {
        if (!visible)
        {
            _developerToolsWindow?.Close();
            return;
        }
        CloseSidePanel();
        if (_developerToolsWindow is not null)
        {
            RestoreAndActivate(_developerToolsWindow);
            return;
        }
        DeveloperToolsWindow window = new DeveloperToolsWindow(_developerTools);
        _developerToolsWindow = window;
        _developerTools.SetRequests(_requestMonitor.Snapshot());
        window.ApplyPreferences(_preferences);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_developerToolsWindow, window))
            {
                _developerToolsWindow = null;
            }
        };
        window.Show(this);
    }

    private void BrowserSettingsPasswordsRequested()
    {
        _settingsWindow?.Close();
        ShowLibrary(BrowserLibrarySection.Passwords);
    }

    private async void BrowserSettingsClearBrowsingDataRequested()
    {
        await ClearBrowsingDataAsync(_settingsWindow);
    }

    private void CloseSidePanel()
    {
        WorkspaceSplit.IsPaneOpen = false;
        BrowserMenuButton.IsChecked = false;
    }

    private void ShowSidePanel(
        Control content,
        Double width,
        ToggleButton? selectedButton = null)
    {
        BrowserMenuButton.IsChecked = false;
        if (selectedButton is not null)
        {
            selectedButton.IsChecked = true;
        }
        _sidePanelHost.Content = content;
        WorkspaceSplit.OpenPaneLength = width;
        WorkspaceSplit.IsPaneOpen = true;
    }

    private void WorkspaceSplitPaneClosed(
        Object? sender,
        RoutedEventArgs eventArgs)
    {
        BrowserMenuButton.IsChecked = false;
    }

    private void RefreshLibraryWindows()
    {
        foreach (BrowserLibraryWindow window in _libraryWindows.Values)
        {
            window.RefreshAll();
        }
    }

    private static void RestoreAndActivate(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }
        window.Activate();
    }

    private void RequestRecorded(LumuiRequestTrace trace)
    {
        if (_developerToolsWindow is null)
        {
            return;
        }
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_developerToolsWindow is not null)
                {
                    _developerTools.AddRequest(trace);
                }
            });
    }

    private void RequestsCleared()
    {
        _requestMonitor.Clear();
    }

    private void ShowError(
        BrowserTabSession tab,
        String title,
        String message)
    {
        StackPanel panel = new StackPanel
        {
            Margin = new Thickness(28),
            MaxWidth = 760,
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontSize = 28,
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.Parse(
                        _preferences.HighContrast
                            ? "#FFF200"
                            : _preferences.ColorScheme == BrowserColorScheme.Dark
                                ? "#FFB4AB"
                                : "#A40000"))
                },
                new TextBlock
                {
                    Text = message,
                    FontSize = 16,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                }
            }
        };
        AutomationProperties.SetName(panel, title + ". " + message);
        tab.SetError(panel);
        if (ReferenceEquals(tab, _tabs.Active))
        {
            ApplyDocumentViewport(null);
            SurfaceHost.Content = panel;
        }
    }

    private Boolean ApplyDocumentViewport(
        LoadedSurface? loaded,
        LumuiRenderer? renderer = null)
    {
        Boolean workspace = loaded is not null
            && ViewerWorkspaceRenderer.Matches(loaded.Document.RootElement);
        Boolean ownsViewport = workspace || renderer?.DocumentViewport is not null;
        ContentScroll.VerticalScrollBarVisibility = ownsViewport
            ? ScrollBarVisibility.Disabled
            : ScrollBarVisibility.Auto;
        SurfaceHost.VerticalContentAlignment = ownsViewport
            ? VerticalAlignment.Stretch
            : VerticalAlignment.Top;
        if (ownsViewport)
        {
            ContentScroll.Offset = default(Vector);
        }
        return ownsViewport;
    }

    private async void WindowClosing(Object? sender, WindowClosingEventArgs eventArgs)
    {
        if (!_closeConfirmed
            && _preferences.ConfirmClosingMultipleTabs
            && _tabs.Tabs.Count > 1)
        {
            eventArgs.Cancel = true;
            if (_closeConfirmationPending)
            {
                return;
            }
            _closeConfirmationPending = true;
            try
            {
                if (await ConfirmAsync(
                        "Close tabs",
                        $"Close all {_tabs.Tabs.Count} tabs?"))
                {
                    _closeConfirmed = true;
                    Close();
                }
            }
            finally
            {
                _closeConfirmationPending = false;
            }
            return;
        }
        if (_cleanedUp)
        {
            return;
        }
        _cleanedUp = true;
        _preferenceRenderCancellation?.Cancel();
        _requestMonitor.Recorded -= RequestRecorded;
        _services.PreferencesChanged -= SharedPreferencesChanged;
        _bookmarks.Changed -= BookmarksChanged;
        WorkspaceSplit.PaneClosed -= WorkspaceSplitPaneClosed;
        _tabs.Changed -= TabsChanged;
        _browserMenu.CommandRequested -= BrowserCommandRequested;
        _browserSettings.PreferencesChanged -= PreferencesChanged;
        _browserSettings.OpenPasswordsRequested -= BrowserSettingsPasswordsRequested;
        _developerTools.RequestsCleared -= RequestsCleared;
        _developerToolsWindow?.Close();
        _settingsWindow?.Close();
        foreach (BrowserLibraryWindow window in _libraryWindows.Values.ToArray())
        {
            window.Close();
        }
        if (!_privateMode && _preferences.ClearBrowsingDataOnExit)
        {
            _visitHistory.Clear();
            _services.ClearBrowsingData();
        }
        try
        {
            if (!_privateMode)
            {
                _sessionStore.Save(_tabs.Tabs, _tabs.Active);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            _developerTools.AddException(exception);
        }
        _tabs.Dispose();
        _services.UnregisterClient(_client);
        _client.Dispose();
        _shellSurface.Dispose();
    }
}
