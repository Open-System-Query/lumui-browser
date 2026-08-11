using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Lumui.Browser.Presentation;
using Lumui.Client;
using LumuiProtocol = Lumui.Client.LumuiProtocol;

namespace Lumui.Browser.Rendering;

public sealed class ViewerWorkspaceSlot : IDisposable
{
    private readonly LumuiClient _client;
    private readonly RendererSettings _hostSettings;
    private readonly Func<Uri, Task> _openExternal;
    private readonly Func<Uri, Task> _download;
    private readonly IReadOnlySet<String> _ancestry;
    private readonly Func<JsonElement, String?>? _inputSuggestion;
    private readonly List<Uri> _history = new List<Uri>();
    private CancellationTokenSource _loadCancellation =
        new CancellationTokenSource();
    private CancellationTokenSource _renderCancellation =
        new CancellationTokenSource();
    private LoadedSurface? _loaded;
    private LumuiRenderer? _renderer;
    private Int32 _historyIndex = -1;
    private Boolean _disposed;
    private AppearanceDefinition _appearance;
    private Double _textScale;
    private Boolean _highContrast;
    private Boolean _reducedMotion;
    private Boolean _bionicReading;
    private InteractionModeDefinition _interaction;
    private readonly ContentControl _content = new ContentControl();
    private readonly Border _notice = new Border();
    private readonly ProgressBar _loading = new ProgressBar();
    private Int32 _noticeVersion;

    public ViewerWorkspaceSlot(
        LumuiClient client,
        RendererSettings hostSettings,
        DeviceProfileDefinition profile,
        Func<Uri, Task> openExternal,
        Func<Uri, Task> download,
        IReadOnlySet<String> ancestry,
        Func<JsonElement, String?>? inputSuggestion)
    {
        _client = client;
        _hostSettings = hostSettings;
        _appearance = hostSettings.Appearance;
        _textScale = hostSettings.TextScale;
        _highContrast = hostSettings.HighContrast;
        _reducedMotion = hostSettings.ReducedMotion;
        _bionicReading = hostSettings.BionicReading;
        _interaction = hostSettings.Interaction;
        Profile = profile;
        _openExternal = openExternal;
        _download = download;
        _ancestry = ancestry;
        _inputSuggestion = inputSuggestion;
        Host = new Grid
        {
            Background = Brushes.White
        };
        _content.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _content.VerticalContentAlignment = VerticalAlignment.Stretch;
        Host.Children.Add(_content);
        _notice.HorizontalAlignment = HorizontalAlignment.Right;
        _notice.VerticalAlignment = VerticalAlignment.Bottom;
        _notice.Margin = new Thickness(12D);
        _notice.Padding = new Thickness(12D, 9D);
        _notice.CornerRadius = new CornerRadius(0D);
        _notice.Background = Brush(_appearance.Accent);
        _notice.IsVisible = false;
        Host.Children.Add(_notice);
        _loading.Height = 3D;
        _loading.HorizontalAlignment = HorizontalAlignment.Stretch;
        _loading.VerticalAlignment = VerticalAlignment.Top;
        _loading.IsHitTestVisible = false;
        _loading.IsVisible = false;
        Host.Children.Add(_loading);
    }

    public event Action<ViewerWorkspaceSlot>? Changed;

    public event Action<ViewerWorkspaceSlot, String, Boolean>? StatusChanged;

    public event Action<ViewerWorkspaceSlot>? ViewportResetRequested;

    public Grid Host { get; }

    public DeviceProfileDefinition Profile { get; private set; }

    public Uri? Address => _historyIndex >= 0 && _historyIndex < _history.Count
        ? _history[_historyIndex]
        : _loaded?.SurfaceUri;

    public Boolean CanGoBack => _historyIndex > 0;

    public Boolean CanGoForward =>
        _historyIndex >= 0 && _historyIndex < _history.Count - 1;

    public String? Problem { get; private set; }

    public async Task OpenAsync(Uri uri)
    {
        if (_disposed)
        {
            return;
        }
        if (_historyIndex < _history.Count - 1)
        {
            _history.RemoveRange(
                _historyIndex + 1,
                _history.Count - _historyIndex - 1);
        }
        _history.Add(uri);
        _historyIndex = _history.Count - 1;
        await LoadAsync(uri);
    }

    public async Task GoBackAsync()
    {
        if (!CanGoBack)
        {
            return;
        }
        _historyIndex--;
        await LoadAsync(_history[_historyIndex]);
    }

    public async Task GoForwardAsync()
    {
        if (!CanGoForward)
        {
            return;
        }
        _historyIndex++;
        await LoadAsync(_history[_historyIndex]);
    }

    public Task ReloadAsync() => Address is Uri uri
        ? LoadAsync(uri)
        : Task.CompletedTask;

    public async Task SetProfileAsync(DeviceProfileDefinition profile)
    {
        if (ReferenceEquals(Profile, profile)
            || String.Equals(Profile.Id, profile.Id, StringComparison.Ordinal))
        {
            return;
        }
        Profile = profile;
        if (_loaded is LoadedSurface loaded)
        {
            CancellationTokenSource request = BeginRenderRequest();
            await RerenderAsync(loaded, request.Token);
            return;
        }
        Changed?.Invoke(this);
    }

    public void SetAppearance(AppearanceDefinition appearance)
    {
        _appearance = appearance;
        Rerender();
    }

    public void SetTextScale(Double value)
    {
        _textScale = Math.Clamp(value, 0.8D, 1.8D);
        Rerender();
    }

    public void SetHighContrast(Boolean value)
    {
        _highContrast = value;
        Rerender();
    }

    public void SetReducedMotion(Boolean value)
    {
        _reducedMotion = value;
        Rerender();
    }

    public void SetBionicReading(Boolean value)
    {
        _bionicReading = value;
        Rerender();
    }

    public void SetGuided(Boolean value)
    {
        _interaction = value
            ? InteractionModeCatalog.Guided
            : InteractionModeCatalog.Standard;
        Rerender();
    }

    private void Rerender()
    {
        if (_loaded is not LoadedSurface loaded)
        {
            return;
        }
        CancellationTokenSource request = BeginRenderRequest();
        _ = RerenderAsync(loaded, request.Token);
    }

    private CancellationTokenSource BeginRenderRequest()
    {
        CancellationTokenSource previous = _renderCancellation;
        CancellationTokenSource request = new CancellationTokenSource();
        _renderCancellation = request;
        previous.Cancel();
        previous.Dispose();
        return request;
    }

    private async Task RerenderAsync(
        LoadedSurface loaded,
        CancellationToken cancellationToken)
    {
        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed || !ReferenceEquals(_loaded, loaded))
            {
                return;
            }
            await RenderLoadedAsync(loaded, cancellationToken);
            Changed?.Invoke(this);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Problem = exception.Message.Length == 0
                ? "The view could not be updated."
                : exception.Message;
            StatusChanged?.Invoke(this, Problem, true);
        }
    }

    private async Task NavigateAsync(Uri uri)
    {
        await OpenAsync(uri);
    }

    private async Task LoadAsync(Uri uri)
    {
        BeginRenderRequest();
        CancellationTokenSource previous = _loadCancellation;
        CancellationTokenSource request = new CancellationTokenSource();
        _loadCancellation = request;
        previous.Cancel();
        previous.Dispose();
        _noticeVersion++;
        _notice.IsVisible = false;
        if (_loaded is null)
        {
            _content.Content = LoadingView();
            ViewportResetRequested?.Invoke(this);
        }
        _loading.IsIndeterminate = !_reducedMotion;
        _loading.Value = _reducedMotion ? 40D : 0D;
        _loading.IsVisible = true;
        Problem = null;
        StatusChanged?.Invoke(this, "Opening application…", false);
        Changed?.Invoke(this);
        await Dispatcher.Yield(DispatcherPriority.Background);
        request.Token.ThrowIfCancellationRequested();

        LoadedSurface? loaded = null;
        LumuiRenderer? renderer = null;
        try
        {
            loaded = await _client.LoadAsync(
                uri,
                request.Token);
            if (_disposed
                || request.IsCancellationRequested
                || !ReferenceEquals(_loadCancellation, request))
            {
                loaded.Dispose();
                return;
            }
            renderer = CreateRenderer(loaded);
            Control content = await renderer.RenderAsync(
                loaded.Document.RootElement,
                request.Token);
            request.Token.ThrowIfCancellationRequested();
            LoadedSurface? previousSurface = _loaded;
            LumuiRenderer? previousRenderer = _renderer;
            _loaded = loaded;
            loaded = null;
            _renderer = renderer;
            renderer = null;
            _content.Content = content;
            previousRenderer?.Dispose();
            previousSurface?.Dispose();
            ViewportResetRequested?.Invoke(this);
            StatusChanged?.Invoke(this, "Ready", false);
            Changed?.Invoke(this);
        }
        catch (OperationCanceledException) when (
            request.IsCancellationRequested)
        {
            renderer?.Dispose();
            loaded?.Dispose();
        }
        catch (Exception exception)
        {
            renderer?.Dispose();
            loaded?.Dispose();
            Problem = exception.Message.Length == 0
                ? "The page could not be reached."
                : exception.Message;
            if (_loaded is null)
            {
                _content.Content = ErrorView(exception.Message);
            }
            else
            {
                ShowNotice("The page could not be reached.", true);
            }
            StatusChanged?.Invoke(this, "The page could not be reached.", true);
            Changed?.Invoke(this);
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, request))
            {
                _loading.IsVisible = false;
            }
        }
    }

    private async Task RenderLoadedAsync(
        LoadedSurface loaded,
        CancellationToken cancellationToken)
    {
        LumuiRenderer renderer = CreateRenderer(loaded);
        Boolean committed = false;
        try
        {
            Control content = await renderer.RenderAsync(
                loaded.Document.RootElement,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            LumuiRenderer? previousRenderer = _renderer;
            _renderer = renderer;
            _content.Content = content;
            committed = true;
            previousRenderer?.Dispose();
        }
        finally
        {
            if (!committed)
            {
                renderer.Dispose();
            }
        }
    }

    private LumuiRenderer CreateRenderer(LoadedSurface loaded)
    {
        RendererSettings settings = new RendererSettings(
            Profile,
            _highContrast
                ? AppearanceCatalog.HighContrast(_appearance)
                : _appearance,
            _hostSettings.Output,
            _interaction,
            _textScale,
            1D,
            _highContrast,
            _reducedMotion,
            _bionicReading,
            _hostSettings.ColorVision);
        HashSet<String> ancestry = new HashSet<String>(
            _ancestry,
            StringComparer.OrdinalIgnoreCase)
        {
            loaded.SurfaceUri.AbsoluteUri.TrimEnd('/')
        };
        return new LumuiRenderer(
            _client,
            loaded.SurfaceUri,
            settings,
            NavigateAsync,
            _openExternal,
            (String componentId, String actionId,
                IReadOnlyDictionary<String, Object?> input) =>
                InvokeActionAsync(loaded, componentId, actionId, input),
            message => StatusChanged?.Invoke(this, message, false),
            ancestry,
            _inputSuggestion,
            _download);
    }

    private async Task InvokeActionAsync(
        LoadedSurface loaded,
        String componentId,
        String actionId,
        IReadOnlyDictionary<String, Object?> input)
    {
        StatusChanged?.Invoke(this, "Working…", false);
        CancellationToken actionToken = _loadCancellation.Token;
        try
        {
            using ActionResult result = await _client.InvokeAsync(
                loaded,
                componentId,
                actionId,
                input,
                ActionProfileId(),
                LumuiProtocol.InputMethods.Native,
                cancellationToken: actionToken);
            if (result.Status == LumuiProtocol.ActionStatuses.RequiresConfirmation)
            {
                StatusChanged?.Invoke(this, "Confirmation is required.", true);
                return;
            }
            ActionResult effective = result;
            ActionResult? completed = null;
            try
            {
                if (result.Status == LumuiProtocol.ActionStatuses.AcceptedAsync)
                {
                    StatusChanged?.Invoke(this, "Waiting…", false);
                    completed = await _client.WaitForCompletionAsync(
                        result,
                        actionToken);
                    effective = completed;
                }
                Uri? target = effective.RedirectUri(effective.ResponseUri)
                    ?? effective.SurfaceUri(effective.ResponseUri);
                if (target is not null)
                {
                    await OpenAsync(target);
                    return;
                }
                StatusChanged?.Invoke(
                    this,
                    effective.Message() ?? "Done",
                    false);
                ShowNotice(effective.Message() ?? "Done", false);
            }
            finally
            {
                completed?.Dispose();
            }
        }
        catch (OperationCanceledException) when (
            actionToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Problem = exception.Message;
            StatusChanged?.Invoke(this, "The action could not be completed.", true);
            ShowNotice("The action could not be completed.", true);
        }
    }

    private void ShowNotice(String message, Boolean error)
    {
        _noticeVersion++;
        Int32 version = _noticeVersion;
        _notice.Background = Brush(error ? "#842E2E" : _appearance.Accent);
        TextBlock noticeText = new TextBlock
        {
            Text = message,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 320D
        };
        ReadingTextFormatter.ApplyTree(noticeText, _bionicReading);
        _notice.Child = noticeText;
        _notice.IsVisible = true;
        _ = HideNoticeAsync(version);
    }

    private async Task HideNoticeAsync(Int32 version)
    {
        await Task.Delay(3200).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(
            () =>
            {
                if (!_disposed && version == _noticeVersion)
                {
                    _notice.IsVisible = false;
                }
            });
    }

    private Control LoadingView() =>
        MessageView("Opening application…", false);

    private String ActionProfileId() => Profile.Kind switch
    {
        DeviceProfileKind.Web => LumuiProtocol.RenderProfiles.WebResponsiveDefault,
        DeviceProfileKind.Desktop => LumuiProtocol.RenderProfiles.DesktopLandscapeDefault,
        DeviceProfileKind.Tablet => LumuiProtocol.RenderProfiles.TabletLandscapeDefault,
        DeviceProfileKind.Phone => LumuiProtocol.RenderProfiles.SmartphonePortraitDefault,
        DeviceProfileKind.Watch => LumuiProtocol.RenderProfiles.SmartwatchSquareDefault,
        DeviceProfileKind.Kiosk => LumuiProtocol.RenderProfiles.KioskLandscapePublic,
        DeviceProfileKind.Appliance => LumuiProtocol.RenderProfiles.ApplianceLandscapeShared,
        _ => LumuiProtocol.RenderProfiles.WebResponsiveDefault
    };

    private Control ErrorView(String detail) =>
        MessageView(
            detail.Length == 0
                ? "The page could not be reached."
                : detail,
            true);

    private Control MessageView(String text, Boolean error)
    {
        StackPanel content = new StackPanel
        {
            Spacing = 12D,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(new Border
        {
            Width = 42D,
            Height = 42D,
            CornerRadius = new CornerRadius(21D),
            Background = Brush(error ? "#C94A4A" : _appearance.Accent),
            Child = new TextBlock
            {
                Text = error ? "!" : "◆",
                Foreground = Brushes.White,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        content.Children.Add(new TextBlock
        {
            Text = text,
            MaxWidth = 520D,
            Foreground = Brush("#5A5A5A"),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });
        Grid view = new Grid
        {
            Background = Brushes.White,
            Children = { content }
        };
        ReadingTextFormatter.ApplyTree(view, _bionicReading);
        return view;
    }

    private static IBrush Brush(String value) =>
        BrowserBrushCache.Get(value);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _loadCancellation.Cancel();
        _loadCancellation.Dispose();
        _renderCancellation.Cancel();
        _renderCancellation.Dispose();
        _renderer?.Dispose();
        _loaded?.Dispose();
    }
}
