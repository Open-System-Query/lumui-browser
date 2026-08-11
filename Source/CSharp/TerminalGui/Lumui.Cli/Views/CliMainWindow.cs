using Lumui.Cli.Configuration;
using Lumui.Cli.Data;
using Lumui.Cli.Downloads;
using Lumui.Cli.Navigation;
using Lumui.Cli.Rendering;
using Lumui.Cli.Security;

namespace Lumui.Cli.Views;

public sealed class CliMainWindow : Runnable
{
    private readonly CliBrowserServices _services;
    private readonly TerminalSurfaceRenderer _renderer;
    private readonly CliTabManager _tabManager = new CliTabManager();
    private readonly Tabs _tabs;
    private readonly Dictionary<CliTabSession, CliTabView> _views =
        new Dictionary<CliTabSession, CliTabView>();
    private readonly List<(CliTabView View, String Address)> _initialLoads =
        new List<(CliTabView View, String Address)>();
    private IApplication? _keyboardApplication;
    private Boolean _completed;

    public CliMainWindow(CliBrowserServices services, String? initialAddress)
    {
        _services = services;
        _renderer = new TerminalSurfaceRenderer(services.Preferences);
        Title = services.IsPrivate ? "LUMUI Browser | Private" : "LUMUI Browser";
        SchemeName = "Base";
        _tabs = new Tabs
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            TabDepth = 2,
            SchemeName = "Base"
        };
        _tabs.ValueChanged += (_, _) =>
        {
            if (_tabs.Value is CliTabView view)
            {
                _tabManager.Activate(view.Session);
                view.Refresh();
            }
        };
        Add(_tabs);
        BuildStartupTabs(initialAddress);
        Initialized += (_, _) =>
        {
            AttachKeyboard();
            _ = InitializeAsync();
        };
        Disposing += (_, _) => DetachKeyboard();
    }

    public Boolean IsBookmarked(Uri address) => _services.Bookmarks.Contains(address);

    public CliPreferences Preferences => _services.Preferences;

    public async Task NavigateAsync(CliTabView view, String address)
    {
        Uri normalized;
        try
        {
            normalized = LumuiClient.NormalizeAddress(address);
        }
        catch (Exception exception) when (exception is LumuiProtocolException or UriFormatException)
        {
            ShowError("Open address", exception.Message);
            return;
        }
        await NavigateUriAsync(view, normalized, true, 0).ConfigureAwait(false);
    }

    public Task GoHomeAsync(CliTabView view) =>
        NavigateAsync(view, _services.Preferences.HomePage);

    public Task ReloadAsync(CliTabView view) =>
        view.Session.Address is null
            ? Task.CompletedTask
            : NavigateUriAsync(view, view.Session.Address, false, 0);

    public Task ReloadOrStopAsync(CliTabView view)
    {
        if (!view.Session.IsBusy)
        {
            return ReloadAsync(view);
        }
        view.Session.CancelPendingWork();
        view.Session.IsBusy = false;
        view.Session.Status = "Cancelled";
        view.Refresh();
        return Task.CompletedTask;
    }

    public Task GoBackAsync(CliTabView view)
    {
        return view.Session.History.TryPeek(-1, out Uri? address) && address is not null
            ? NavigateUriAsync(view, address, false, -1)
            : Task.CompletedTask;
    }

    public Task GoForwardAsync(CliTabView view)
    {
        return view.Session.History.TryPeek(1, out Uri? address) && address is not null
            ? NavigateUriAsync(view, address, false, 1)
            : Task.CompletedTask;
    }

    public void ToggleBookmark(CliTabView view)
    {
        Uri? address = view.Session.Address;
        if (address is null)
        {
            return;
        }
        if (_services.Bookmarks.Contains(address))
        {
            _services.Bookmarks.Remove(address);
        }
        else
        {
            _services.Bookmarks.Add(address, view.Session.Title);
        }
        foreach (CliTabView tab in _views.Values)
        {
            tab.Refresh();
        }
    }

    public void OpenMenu(CliTabView view, String category = "All")
    {
        if (App is null)
        {
            return;
        }
        IReadOnlyList<CliMenuEntry> entries = category switch
        {
            "File" => new[]
            {
                new CliMenuEntry("new", "New tab", "Ctrl+T"),
                new CliMenuEntry("reopen", "Reopen closed tab", "Ctrl+Shift+T"),
                new CliMenuEntry("close", "Close tab", "Ctrl+W"),
                new CliMenuEntry("quit", "Quit browser", "Ctrl+Q")
            },
            "View" => new[]
            {
                new CliMenuEntry("guided", view.Guided ? "Use standard view" : "Use guided view"),
                new CliMenuEntry("outline", view.OutlineVisible ? "Hide page outline" : "Show page outline"),
                new CliMenuEntry("settings", "View and application settings", "F2")
            },
            "Go" => new[]
            {
                new CliMenuEntry("address", "Focus address", "Ctrl+L"),
                new CliMenuEntry("back", "Back", "Alt+Left"),
                new CliMenuEntry("forward", "Forward", "Alt+Right"),
                new CliMenuEntry("reload", "Reload", "Ctrl+R"),
                new CliMenuEntry("home", "Home")
            },
            "Library" => new[]
            {
                new CliMenuEntry(
                    "bookmark",
                    view.Session.Address is not null && IsBookmarked(view.Session.Address)
                        ? "Remove current bookmark"
                        : "Bookmark current page"),
                new CliMenuEntry("bookmarks", "Bookmarks", "Ctrl+B"),
                new CliMenuEntry("history", "History", "Ctrl+H"),
                new CliMenuEntry("downloads", "Downloads", "Ctrl+J"),
                new CliMenuEntry("passwords", "Passwords")
            },
            "Tools" => new[]
            {
                new CliMenuEntry("tools", "Developer tools", "F12"),
                new CliMenuEntry("source", "View source", "Ctrl+U")
            },
            "Help" => new[]
            {
                new CliMenuEntry("shortcuts", "Keyboard shortcuts"),
                new CliMenuEntry("about", "About terminal browser")
            },
            _ => new[]
            {
                new CliMenuEntry("new", "New tab", "Ctrl+T"),
                new CliMenuEntry("guided", view.Guided ? "Standard view" : "Guided view"),
                new CliMenuEntry(
                    "bookmark",
                    view.Session.Address is not null && IsBookmarked(view.Session.Address)
                        ? "Remove current bookmark"
                        : "Bookmark current page"),
                new CliMenuEntry("bookmarks", "Bookmarks"),
                new CliMenuEntry("history", "History"),
                new CliMenuEntry("downloads", "Downloads"),
                new CliMenuEntry("settings", "Settings", "F2"),
                new CliMenuEntry("tools", "Developer tools", "F12"),
                new CliMenuEntry("close", "Close tab", "Ctrl+W"),
                new CliMenuEntry("quit", "Quit browser", "Ctrl+Q")
            }
        };
        CliMenuEntry.Align(entries);
        CliMenuEntry? selected = CliDialogs.Choose(App, category == "All" ? "LUMUI Browser" : category, entries);
        if (selected is null)
        {
            return;
        }
        switch (selected.Key)
        {
            case "new": NewTab(); break;
            case "reopen": ReopenClosedTab(); break;
            case "bookmark": ToggleBookmark(view); break;
            case "bookmarks": OpenLibrary(view, "Bookmarks"); break;
            case "history": OpenLibrary(view, "History"); break;
            case "downloads": OpenLibrary(view, "Downloads"); break;
            case "passwords": OpenLibrary(view, "Passwords"); break;
            case "settings": OpenSettings(); break;
            case "tools": OpenDeveloperTools(view); break;
            case "source": OpenDeveloperTools(view, "Source"); break;
            case "guided": view.ToggleGuided(); break;
            case "outline": view.ToggleOutline(); break;
            case "address": view.FocusAddress(); break;
            case "back": _ = GoBackAsync(view); break;
            case "forward": _ = GoForwardAsync(view); break;
            case "reload": _ = ReloadOrStopAsync(view); break;
            case "home": _ = GoHomeAsync(view); break;
            case "shortcuts": ShowShortcuts(); break;
            case "about": ShowAbout(); break;
            case "close": CloseTab(view); break;
            case "quit": RequestQuit(); break;
        }
    }

    public Task InteractAsync(
        CliTabView view,
        SemanticComponent component,
        TerminalInteraction interaction) =>
        InteractAsync(
            view,
            new TerminalRenderLine(component.Label, TerminalLineRole.Control, component, interaction));

    public async Task InteractAsync(CliTabView view, TerminalRenderLine line)
    {
        SemanticComponent? component = line.Component;
        if (component is null || !component.Enabled)
        {
            return;
        }
        switch (line.Interaction)
        {
            case TerminalInteraction.Navigate:
                if (component.Target is not null)
                {
                    await NavigateUriAsync(view, component.Target, true, 0).ConfigureAwait(false);
                }
                else if (component.ActionId.Length > 0)
                {
                    await InvokeActionAsync(view, component).ConfigureAwait(false);
                }
                break;
            case TerminalInteraction.Action:
                if (component.ActionId.Length > 0)
                {
                    await InvokeActionAsync(view, component).ConfigureAwait(false);
                }
                else if (component.Target is not null)
                {
                    await NavigateUriAsync(view, component.Target, true, 0).ConfigureAwait(false);
                }
                break;
            case TerminalInteraction.Toggle:
                ToggleInput(view, component);
                if (component.ActionId.Length > 0)
                {
                    await InvokeActionAsync(view, component).ConfigureAwait(false);
                }
                break;
            case TerminalInteraction.Choose:
                if (ChooseInput(view, component) && component.ActionId.Length > 0)
                {
                    await InvokeActionAsync(view, component).ConfigureAwait(false);
                }
                break;
            case TerminalInteraction.Edit:
                if (EditInput(view, component) && component.ActionId.Length > 0)
                {
                    await InvokeActionAsync(view, component).ConfigureAwait(false);
                }
                break;
            case TerminalInteraction.Media:
                OpenMedia(component);
                break;
            case TerminalInteraction.Resource:
                OpenResource(component);
                break;
            case TerminalInteraction.Download:
                await DownloadAsync(view, component).ConfigureAwait(false);
                break;
        }
    }

    public void SaveState()
    {
        if (_completed)
        {
            return;
        }
        _completed = true;
        if (!_services.IsPrivate)
        {
            try
            {
                _services.Session.Save(_tabManager.Tabs, _tabManager.Active);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
        _tabManager.Dispose();
    }

    protected override Boolean OnKeyDown(Key key)
    {
        return HandleApplicationKey(key) || base.OnKeyDown(key);
    }

    private Boolean HandleApplicationKey(Key key)
    {
        CliTabView? active = ActiveView;
        if (key == Key.L.WithCtrl)
        {
            active?.FocusAddress();
            return true;
        }
        if (key == Key.F6)
        {
            active?.FocusAddress();
            return true;
        }
        if (key == Key.T.WithCtrl.WithShift)
        {
            ReopenClosedTab();
            return true;
        }
        if (key == Key.T.WithCtrl)
        {
            NewTab();
            return true;
        }
        if (key == Key.W.WithCtrl)
        {
            if (active is not null)
            {
                CloseTab(active);
            }
            return true;
        }
        if (key == Key.R.WithCtrl)
        {
            if (active is not null)
            {
                _ = ReloadOrStopAsync(active);
            }
            return true;
        }
        if (key == Key.CursorLeft.WithAlt)
        {
            if (active is not null)
            {
                _ = GoBackAsync(active);
            }
            return true;
        }
        if (key == Key.CursorRight.WithAlt)
        {
            if (active is not null)
            {
                _ = GoForwardAsync(active);
            }
            return true;
        }
        if (key == Key.Tab.WithCtrl)
        {
            MoveTab(1);
            return true;
        }
        if (key == Key.Tab.WithCtrl.WithShift)
        {
            MoveTab(-1);
            return true;
        }
        if (key == Key.B.WithCtrl)
        {
            if (active is not null)
            {
                OpenLibrary(active, "Bookmarks");
            }
            return true;
        }
        if (key == Key.H.WithCtrl)
        {
            if (active is not null)
            {
                OpenLibrary(active, "History");
            }
            return true;
        }
        if (key == Key.J.WithCtrl)
        {
            if (active is not null)
            {
                OpenLibrary(active, "Downloads");
            }
            return true;
        }
        if (key == Key.F12)
        {
            if (active is not null)
            {
                OpenDeveloperTools(active);
            }
            return true;
        }
        if (key == Key.U.WithCtrl)
        {
            if (active is not null)
            {
                OpenDeveloperTools(active, "Source");
            }
            return true;
        }
        if (key == Key.F1)
        {
            ShowShortcuts();
            return true;
        }
        if (key == Key.F2)
        {
            OpenSettings();
            return true;
        }
        if (key == Key.Q.WithCtrl)
        {
            RequestQuit();
            return true;
        }
        if (key == Key.Esc)
        {
            active?.HandleEscape();
            return true;
        }
        if (key == Key.Tab.WithShift)
        {
            return active?.MoveFocus(-1) == true;
        }
        if (key == Key.Tab)
        {
            return active?.MoveFocus(1) == true;
        }
        if (key == Key.CursorLeft
            || key == Key.CursorRight
            || key == Key.CursorUp
            || key == Key.CursorDown)
        {
            return active?.MoveDirectionalFocus(key) == true;
        }
        return false;
    }

    private void AttachKeyboard()
    {
        if (App is null || ReferenceEquals(_keyboardApplication, App))
        {
            return;
        }
        DetachKeyboard();
        _keyboardApplication = App;
        _keyboardApplication.Keyboard.KeyDown += ApplicationKeyDown;
    }

    private void DetachKeyboard()
    {
        if (_keyboardApplication is null)
        {
            return;
        }
        _keyboardApplication.Keyboard.KeyDown -= ApplicationKeyDown;
        _keyboardApplication = null;
    }

    private void ApplicationKeyDown(Object? sender, Key key)
    {
        if (key.Handled || !IsCurrentTop)
        {
            return;
        }
        if (HandleApplicationKey(key))
        {
            key.Handled = true;
        }
    }

    private CliTabView? ActiveView => _tabs.Value as CliTabView;

    private void BuildStartupTabs(String? initialAddress)
    {
        if (!String.IsNullOrWhiteSpace(initialAddress))
        {
            CliTabView tab = CreateTab();
            _initialLoads.Add((tab, initialAddress));
            return;
        }
        if (_services.Preferences.StartupMode == CliStartupMode.RestorePreviousSession
            && !_services.IsPrivate)
        {
            SessionState state = _services.Session.Load();
            foreach (Uri address in state.Addresses)
            {
                CliTabView tab = CreateTab();
                _initialLoads.Add((tab, address.AbsoluteUri));
            }
            if (_initialLoads.Count > 0)
            {
                Int32 active = Math.Clamp(state.ActiveIndex, 0, _initialLoads.Count - 1);
                _tabs.Value = _initialLoads[active].View;
                return;
            }
        }
        CliTabView home = CreateTab();
        _initialLoads.Add((home, _services.Preferences.HomePage));
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _services.Client.WarmUpAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is LumuiProtocolException or InvalidOperationException)
        {
            ShowError("LUMUI Browser", exception.Message);
        }
        await Task.WhenAll(_initialLoads.Select(item => NavigateAsync(item.View, item.Address))).ConfigureAwait(false);
        _initialLoads.Clear();
    }

    private CliTabView CreateTab()
    {
        CliTabSession session = _tabManager.Create();
        CliTabView view = new CliTabView(this, session);
        _views[session] = view;
        _tabs.Add(view);
        _tabs.Value = view;
        return view;
    }

    private void NewTab()
    {
        CliTabView view = CreateTab();
        switch (_services.Preferences.NewTabMode)
        {
            case CliNewTabMode.Blank:
                view.FocusAddress();
                break;
            case CliNewTabMode.Custom:
                _ = NavigateAsync(view, _services.Preferences.NewTabPage);
                break;
            default:
                _ = NavigateAsync(view, _services.Preferences.HomePage);
                break;
        }
    }

    private void ReopenClosedTab()
    {
        Uri? address = _tabManager.TakeLastClosedAddress();
        if (address is null)
        {
            return;
        }
        CliTabView view = CreateTab();
        _ = NavigateUriAsync(view, address, true, 0);
    }

    private void CloseTab(CliTabView view)
    {
        CliTabSession session = view.Session;
        _tabs.Remove(view);
        _views.Remove(session);
        view.Dispose();
        CliTabSession active = _tabManager.Close(session);
        if (!_views.TryGetValue(active, out CliTabView? activeView))
        {
            activeView = new CliTabView(this, active);
            _views[active] = activeView;
            _tabs.Add(activeView);
        }
        _tabs.Value = activeView;
    }

    private void MoveTab(Int32 offset)
    {
        CliTabSession? session = _tabManager.Move(offset);
        if (session is not null && _views.TryGetValue(session, out CliTabView? view))
        {
            _tabs.Value = view;
        }
    }

    private async Task NavigateUriAsync(
        CliTabView view,
        Uri address,
        Boolean pushHistory,
        Int32 historyOffset)
    {
        if (!_views.ContainsValue(view))
        {
            return;
        }
        CliTabSession session = view.Session;
        CancellationToken cancellationToken = session.BeginNavigation();
        session.IsBusy = true;
        session.Status = "Loading " + address.Host;
        await InvokeUiAsync(view.Refresh).ConfigureAwait(false);
        Stopwatch watch = Stopwatch.StartNew();
        LoadedSurface? loaded = null;
        Boolean transferred = false;
        try
        {
            LoadedSurface fetched = await _services.Client.LoadAsync(address, cancellationToken).ConfigureAwait(false);
            loaded = fetched;
            TerminalSurfaceDocument document = _renderer.Parse(fetched);
            watch.Stop();
            await InvokeUiAsync(() =>
            {
                if (!session.IsCurrentNavigation(cancellationToken) || !_views.ContainsValue(view))
                {
                    return;
                }
                session.Address = fetched.Address;
                session.Title = document.Title;
                session.LoadDuration = watch.Elapsed;
                session.DocumentInfo = DocumentInfo(document.Root);
                session.Status = "Ready";
                session.IsBusy = false;
                session.SetDocument(fetched, document);
                transferred = true;
                if (historyOffset != 0)
                {
                    session.History.TryMove(historyOffset);
                }
                else if (pushHistory)
                {
                    session.History.Push(fetched.Address);
                }
                ApplyAutofill(session);
                if (_services.Preferences.RememberHistory)
                {
                    _services.History.Record(fetched.Address, document.Title);
                }
                view.Refresh();
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (
            exception is LumuiProtocolException
            or HttpRequestException
            or IOException
            or JsonException
            or InvalidDataException)
        {
            await InvokeUiAsync(() =>
            {
                if (!session.IsCurrentNavigation(cancellationToken))
                {
                    return;
                }
                session.IsBusy = false;
                session.Status = exception.Message;
                if (session.Document is null)
                {
                    session.Address = address;
                    session.Title = "Unable to open";
                }
                view.Refresh();
            }).ConfigureAwait(false);
        }
        finally
        {
            if (!transferred)
            {
                loaded?.Dispose();
            }
        }
    }

    private void ToggleInput(CliTabView view, SemanticComponent component)
    {
        if (component.Id.Length == 0 || component.ReadOnly)
        {
            return;
        }
        Object? current = view.Session.Input.TryGetValue(component.Id, out Object? value) ? value : null;
        view.Session.Input[component.Id] = !AsBoolean(current);
        view.Refresh();
    }

    private Boolean ChooseInput(CliTabView view, SemanticComponent component)
    {
        if (App is null || component.Id.Length == 0 || component.ReadOnly)
        {
            return false;
        }
        SemanticOption? selected = CliDialogs.Choose(
            App,
            component.Label.Length > 0 ? component.Label : "Choose",
            component.Options,
            "No choices are available.");
        if (selected is null)
        {
            return false;
        }
        if (component.Kind == LumuiProtocol.ComponentKinds.MultiSelect)
        {
            List<Object?> values = CurrentValues(view.Session, component.Id);
            Int32 existing = values.FindIndex(value => Equals(value, selected.Value));
            if (existing >= 0)
            {
                values.RemoveAt(existing);
            }
            else
            {
                values.Add(selected.Value);
            }
            view.Session.Input[component.Id] = values;
        }
        else
        {
            view.Session.Input[component.Id] = selected.Value;
        }
        view.Refresh();
        return true;
    }

    private Boolean EditInput(CliTabView view, SemanticComponent component)
    {
        if (App is null || component.Id.Length == 0 || component.ReadOnly)
        {
            return false;
        }
        Object? current = view.Session.Input.TryGetValue(component.Id, out Object? value) ? value : null;
        String? entered = CliDialogs.Prompt(
            App,
            component.Label.Length > 0 ? component.Label : "Edit value",
            InputPrompt(component),
            Convert.ToString(current, CultureInfo.CurrentCulture) ?? String.Empty,
            component.Kind == LumuiProtocol.ComponentKinds.PasswordField);
        if (entered is null)
        {
            return false;
        }
        if (component.Kind is
            LumuiProtocol.ComponentKinds.NumberField
            or LumuiProtocol.ComponentKinds.Slider
            or LumuiProtocol.ComponentKinds.Stepper
            or LumuiProtocol.ComponentKinds.Rating)
        {
            if (!Double.TryParse(entered, NumberStyles.Float, CultureInfo.CurrentCulture, out Double number)
                && !Double.TryParse(entered, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            {
                CliDialogs.Show(App, "Input", "Enter a valid number.");
                return false;
            }
            view.Session.Input[component.Id] = number;
        }
        else
        {
            view.Session.Input[component.Id] = entered;
        }
        view.Refresh();
        return true;
    }

    private async Task InvokeActionAsync(CliTabView view, SemanticComponent component)
    {
        CliTabSession session = view.Session;
        LoadedSurface? loaded = session.Loaded;
        if (loaded is null || component.ActionId.Length == 0)
        {
            return;
        }
        if (App is null)
        {
            return;
        }
        Boolean crossSurface = component.OriginSurfaceUri is not null
            && !component.OriginSurfaceUri.AbsoluteUri.Equals(
                loaded.SurfaceUri.AbsoluteUri,
                StringComparison.OrdinalIgnoreCase);
        if (_services.Preferences.AskBeforeSensitivePermissions
            && CredentialSemantics.IsSensitiveAction(loaded.Document.RootElement, component.Id)
            && !CliDialogs.Confirm(App, "Permission", "Allow '" + component.Label + "' for this website?"))
        {
            return;
        }
        SurfaceActionPolicy policy = SurfaceActionPolicy.FromSurface(
            loaded.Document.RootElement,
            component.ActionId);
        Boolean preconfirmed = crossSurface
            || policy.Confirmation is LumuiProtocol.ConfirmationPolicies.Dangerous
                or LumuiProtocol.ConfirmationPolicies.Explicit;
        if (preconfirmed
            && !CliDialogs.Confirm(App, "Confirm action", "Continue with '" + component.Label + "'?"))
        {
            return;
        }
        CancellationToken cancellationToken = session.BeginAction();
        session.IsBusy = true;
        session.Status = "Working";
        view.Refresh();
        ActionResult? result = null;
        LoadedSurface? actionSurface = null;
        try
        {
            if (crossSurface)
            {
                actionSurface = await _services.Client.LoadAsync(
                    component.OriginSurfaceUri!,
                    cancellationToken).ConfigureAwait(false);
                loaded = actionSurface;
            }
            String messageId = Guid.NewGuid().ToString();
            result = await _services.Client.InvokeAsync(
                loaded,
                component.Id,
                component.ActionId,
                session.Input,
                LumuiProtocol.RenderProfiles.WebResponsiveDefault,
                LumuiProtocol.InputMethods.Native,
                messageId: messageId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result.Status == LumuiProtocol.ActionStatuses.RequiresConfirmation)
            {
                String? token = result.ConfirmationToken();
                Boolean approved = preconfirmed || await ConfirmOnUiAsync(
                    "Confirm action",
                    "The website requests confirmation for '" + component.Label + "'.").ConfigureAwait(false);
                if (!approved || String.IsNullOrWhiteSpace(token))
                {
                    return;
                }
                result.Dispose();
                result = await _services.Client.InvokeAsync(
                    loaded,
                    component.Id,
                    component.ActionId,
                    session.Input,
                    LumuiProtocol.RenderProfiles.WebResponsiveDefault,
                    LumuiProtocol.InputMethods.Native,
                    true,
                    messageId,
                    token,
                    cancellationToken).ConfigureAwait(false);
            }
            if (result.Status == LumuiProtocol.ActionStatuses.AcceptedAsync)
            {
                ActionResult completed = await _services.Client.WaitForCompletionAsync(
                    result,
                    cancellationToken).ConfigureAwait(false);
                result.Dispose();
                result = completed;
            }
            Uri? surface = result.SurfaceUri(loaded.SurfaceUri);
            Uri? redirect = result.RedirectUri(loaded.SurfaceUri);
            String message = result.Message() ?? "Action completed";
            await OfferCredentialSaveAsync(session).ConfigureAwait(false);
            await InvokeUiAsync(() =>
            {
                if (session.IsCurrentAction(cancellationToken))
                {
                    session.IsBusy = false;
                    session.Status = message;
                    view.Refresh();
                }
            }).ConfigureAwait(false);
            Uri? next = surface ?? redirect;
            if (next is not null && session.IsCurrentAction(cancellationToken))
            {
                await NavigateUriAsync(view, next, true, 0).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (
            exception is LumuiProtocolException
            or HttpRequestException
            or IOException
            or InvalidDataException)
        {
            await InvokeUiAsync(() =>
            {
                if (session.IsCurrentAction(cancellationToken))
                {
                    session.Status = exception.Message;
                    session.IsBusy = false;
                    view.Refresh();
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            result?.Dispose();
            actionSurface?.Dispose();
            await InvokeUiAsync(() =>
            {
                if (session.IsCurrentAction(cancellationToken) && session.IsBusy)
                {
                    session.IsBusy = false;
                    view.Refresh();
                }
            }).ConfigureAwait(false);
        }
    }

    private void OpenMedia(SemanticComponent component)
    {
        if (App is null)
        {
            return;
        }
        using CliMediaWindow player = new CliMediaWindow(component);
        App.Run(player);
        player.StopPlayback();
    }

    private void OpenResource(SemanticComponent component)
    {
        if (App is null)
        {
            return;
        }
        Uri? address = component.Target ?? component.MediaSources.FirstOrDefault()?.Uri;
        if (address is null)
        {
            return;
        }
        using CliResourceWindow resource = new CliResourceWindow(
            component.Label.Length > 0 ? component.Label : "Resource",
            address);
        App.Run(resource);
    }

    private async Task DownloadAsync(CliTabView view, SemanticComponent component)
    {
        Uri? source = component.Target ?? component.MediaSources.FirstOrDefault()?.Uri;
        if (source is null)
        {
            return;
        }
        String folder = _services.Preferences.DownloadFolder;
        if (_services.Preferences.AskWhereToSaveDownloads && App is not null)
        {
            String? selected = CliDialogs.Prompt(App, "Download", "Save in folder", folder);
            if (String.IsNullOrWhiteSpace(selected))
            {
                return;
            }
            folder = selected;
        }
        view.Session.Status = "Downloading " + Path.GetFileName(source.LocalPath);
        view.Refresh();
        try
        {
            DownloadItem item = await _services.Downloads.StartAsync(source, folder).ConfigureAwait(false);
            await InvokeUiAsync(() =>
            {
                view.Session.Status = item.Status switch
                {
                    DownloadStatus.Completed => "Download completed",
                    DownloadStatus.Cancelled => "Download cancelled",
                    DownloadStatus.Failed => item.Error.Length > 0 ? item.Error : "Download failed",
                    _ => "Downloading " + item.FileName
                };
                view.Refresh();
            }).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException or UnauthorizedAccessException)
        {
            await InvokeUiAsync(() =>
            {
                view.Session.Status = exception.Message;
                view.Refresh();
            }).ConfigureAwait(false);
        }
    }

    private void OpenLibrary(CliTabView view, String section)
    {
        if (App is null)
        {
            return;
        }
        using CliLibraryWindow library = new CliLibraryWindow(_services, section);
        App.Run(library);
        if (library.SelectedAddress is not null)
        {
            _ = NavigateUriAsync(view, library.SelectedAddress, true, 0);
        }
    }

    public void OpenSettings()
    {
        if (App is null)
        {
            return;
        }
        using CliSettingsWindow settings = new CliSettingsWindow(_services);
        Boolean tabsVisible = _tabs.Visible;
        _tabs.Visible = false;
        SetNeedsDraw();
        try
        {
            App.Run(settings);
        }
        finally
        {
            _tabs.Visible = tabsVisible;
            SetNeedsDraw();
        }
        if (settings.Saved)
        {
            foreach (CliTabView view in _views.Values)
            {
                view.ApplyPreferences();
            }
            SetNeedsDraw();
        }
    }

    private void OpenDeveloperTools(CliTabView view, String section = "Overview")
    {
        if (App is null)
        {
            return;
        }
        using CliDeveloperToolsWindow tools = new CliDeveloperToolsWindow(_services, view.Session, section);
        App.Run(tools);
    }

    private void ShowShortcuts()
    {
        if (App is null)
        {
            return;
        }
        CliDialogs.Show(
            App,
            "Keyboard shortcuts",
            String.Join(
                Environment.NewLine,
                new[]
                {
                    ShortcutLine("Tab / Shift+Tab", "Next / previous control"),
                    ShortcutLine("Arrow keys", "Move or select in context"),
                    ShortcutLine("Enter / Space", "Activate control"),
                    ShortcutLine("Esc", "Return to page / leave guided view"),
                    ShortcutLine("PageUp / PageDown", "Scroll content"),
                    ShortcutLine("Ctrl+L", "Address"),
                    ShortcutLine("Ctrl+T", "New tab"),
                    ShortcutLine("Ctrl+W", "Close tab"),
                    ShortcutLine("Ctrl+Shift+T", "Reopen tab"),
                    ShortcutLine("Ctrl+R", "Reload"),
                    ShortcutLine("Alt+Left / Alt+Right", "Navigate"),
                    ShortcutLine("Ctrl+B / Ctrl+H / Ctrl+J", "Libraries"),
                    ShortcutLine("Ctrl+U", "Source"),
                    ShortcutLine("F2", "Settings"),
                    ShortcutLine("F12", "Developer tools"),
                    ShortcutLine("Ctrl+Q", "Quit browser")
                }));
    }

    private void ShowAbout()
    {
        if (App is not null)
        {
            CliDialogs.Show(
                App,
                "About the terminal browser",
                "LUMUI Browser for Terminal.Gui\nA semantic terminal viewer for LUMUI.\nRuntime " + Environment.Version);
        }
    }

    private void RequestQuit()
    {
        if (App is null)
        {
            return;
        }
        if (_tabManager.Tabs.Count > 1
            && _services.Preferences.ConfirmClosingMultipleTabs
            && !CliDialogs.Confirm(App, "Quit browser", "Close all open tabs?"))
        {
            return;
        }
        App.RequestStop(this);
    }

    private void ApplyAutofill(CliTabSession session)
    {
        if (!_services.Preferences.AutoFillPasswords
            || !_services.Credentials.IsAvailable
            || session.Address is null
            || session.Document is null)
        {
            return;
        }
        CredentialRecord? credential = _services.Credentials.FindForOrigin(session.Address);
        if (credential is null)
        {
            return;
        }
        ApplyCredential(session.Document.Root, credential, session.Input);
    }

    private async Task OfferCredentialSaveAsync(CliTabSession session)
    {
        if (!_services.Preferences.OfferToSavePasswords
            || !_services.Credentials.IsAvailable
            || session.Address is null
            || session.Document is null)
        {
            return;
        }
        (String UserName, String Password)? submission = CredentialSemantics.FindSubmission(
            session.Document.Root,
            session.Input);
        if (submission is null)
        {
            return;
        }
        Boolean save = await ConfirmOnUiAsync(
            "Save password",
            "Save the password for " + session.Address.Host + "?").ConfigureAwait(false);
        if (save)
        {
            _services.Credentials.Save(
                session.Address,
                submission.Value.UserName,
                submission.Value.Password);
        }
    }

    private static void ApplyCredential(
        JsonElement value,
        CredentialRecord credential,
        IDictionary<String, Object?> input)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty(LumuiProtocol.Fields.Id, out JsonElement idValue)
                && idValue.ValueKind == JsonValueKind.String
                && idValue.GetString() is String id
                && CredentialSemantics.SuggestedValue(value, credential) is String suggested)
            {
                input[id] = suggested;
            }
            foreach (JsonProperty property in value.EnumerateObject())
            {
                ApplyCredential(property.Value, credential, input);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                ApplyCredential(item, credential, input);
            }
        }
    }

    private Task InvokeUiAsync(Action action)
    {
        TaskCompletionSource completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        IApplication? app = App;
        if (app is null)
        {
            completion.SetCanceled();
            return completion.Task;
        }
        app.Invoke(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        return completion.Task;
    }

    private Task<Boolean> ConfirmOnUiAsync(String title, String message)
    {
        Boolean result = false;
        return InvokeUiAsync(() =>
        {
            if (App is not null)
            {
                result = CliDialogs.Confirm(App, title, message);
            }
        }).ContinueWith(
            task =>
            {
                task.GetAwaiter().GetResult();
                return result;
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void ShowError(String title, String message)
    {
        App?.Invoke(() =>
        {
            if (App is not null)
            {
                CliDialogs.Show(App, title, message);
            }
        });
    }

    private static String DocumentInfo(JsonElement root)
    {
        String app = FieldText(root, LumuiProtocol.Fields.AppId);
        String revision = root.TryGetProperty(LumuiProtocol.Fields.Revision, out JsonElement value)
            ? value.GetRawText()
            : String.Empty;
        return String.Join(
            "  ·  ",
            new[] { app, revision.Length > 0 ? "revision " + revision : String.Empty }
                .Where(item => item.Length > 0));
    }

    private static String InputPrompt(SemanticComponent component)
    {
        return component.Kind switch
        {
            LumuiProtocol.ComponentKinds.FilePicker => "File path",
            LumuiProtocol.ComponentKinds.MediaPicker => "Media path",
            LumuiProtocol.ComponentKinds.ContactPicker => "Contact",
            LumuiProtocol.ComponentKinds.LocationPicker => "Location",
            LumuiProtocol.ComponentKinds.Map => "Latitude, longitude or place",
            LumuiProtocol.ComponentKinds.Calendar => "Date (YYYY-MM-DD)",
            LumuiProtocol.ComponentKinds.Dialer => "Telephone number",
            _ => component.Label.Length > 0 ? component.Label : "Value"
        };
    }

    private static List<Object?> CurrentValues(CliTabSession session, String id)
    {
        if (!session.Input.TryGetValue(id, out Object? value) || value is null)
        {
            return new List<Object?>();
        }
        if (value is IEnumerable<Object?> values && value is not String)
        {
            return values.ToList();
        }
        if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Select(item => (Object?)item.GetRawText()).ToList();
        }
        return new List<Object?> { value };
    }

    private static Boolean AsBoolean(Object? value) => value switch
    {
        Boolean boolean => boolean,
        String text when Boolean.TryParse(text, out Boolean parsed) => parsed,
        JsonElement element when element.ValueKind == JsonValueKind.True => true,
        _ => false
    };

    private static String FieldText(JsonElement element, String field) =>
        element.TryGetProperty(field, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? String.Empty
            : String.Empty;

    private static String ShortcutLine(String shortcut, String action) =>
        shortcut.PadRight(27) + action;
}
