using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Lumui.Browser.Controls;
using Lumui.Browser.Presentation;
using Lumui.Client;
using LumuiProtocol = Lumui.Client.LumuiProtocol;

namespace Lumui.Browser.Rendering;

public sealed class LumuiRenderer : IDisposable
{
    private readonly LumuiClient _client;
    private readonly Uri _baseUri;
    private readonly RendererSettings _settings;
    private readonly AppearanceDefinition _appearance;
    private BrandDefinition _brand;
    private AppearanceStyler _styler;
    private readonly Func<Uri, Task> _navigate;
    private readonly Func<Uri, Task> _openExternal;
    private readonly Func<Uri, Task> _download;
    private readonly Func<String, String, IReadOnlyDictionary<String, Object?>, Task> _invoke;
    private readonly Action<String> _status;
    private readonly Func<JsonElement, String?>? _inputSuggestion;
    private readonly SemanticAssetLoader _assetLoader;
    private readonly HashSet<String> _surfaceAncestry;
    private readonly Boolean _embeddedPresentation;
    private readonly List<ViewerWorkspaceRenderer> _workspaceRenderers =
        new List<ViewerWorkspaceRenderer>();
    private readonly List<NativeMediaPlayer> _mediaPlayers =
        new List<NativeMediaPlayer>();
    private readonly DeferredRenderScheduler _deferredScheduler;
    private IReadOnlyList<JsonElement> _preparedRegions =
        Array.Empty<JsonElement>();
    private Boolean _preparedPageAvailable;
    private readonly CancellationTokenSource _renderCancellation =
        new CancellationTokenSource();
    private readonly Dictionary<String, Func<Object?>> _inputs =
        new Dictionary<String, Func<Object?>>(StringComparer.Ordinal);
    private Boolean _disposed;

    public LumuiRenderer(
        LumuiClient client,
        Uri baseUri,
        RendererSettings settings,
        Func<Uri, Task> navigate,
        Func<Uri, Task> openExternal,
        Func<String, String, IReadOnlyDictionary<String, Object?>, Task> invoke,
        Action<String> status,
        IReadOnlySet<String>? surfaceAncestry = null,
        Func<JsonElement, String?>? inputSuggestion = null,
        Func<Uri, Task>? download = null)
    {
        _client = client;
        _baseUri = baseUri;
        _settings = settings;
        _appearance = settings.Appearance;
        _brand = new BrandDefinition(
            _appearance.Accent,
            _appearance.Accent,
            _appearance.Accent,
            _appearance.SurfaceAlternate,
            _appearance.Text,
            _appearance.Surface,
            _appearance.SurfaceAlternate,
            BrandMotif.Lines);
        _styler = new AppearanceStyler(_appearance, _brand);
        _navigate = navigate;
        _openExternal = openExternal;
        _download = download ?? openExternal;
        _invoke = invoke;
        _status = status;
        _deferredScheduler = new DeferredRenderScheduler(
            _renderCancellation.Token,
            exception => _status(exception.Message));
        _inputSuggestion = inputSuggestion;
        _assetLoader = new SemanticAssetLoader(
            client,
            baseUri,
            status,
            _renderCancellation.Token);
        _embeddedPresentation = surfaceAncestry is not null
            && surfaceAncestry.Count > 0;
        _surfaceAncestry = surfaceAncestry is null
            ? new HashSet<String>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<String>(
                surfaceAncestry,
                StringComparer.OrdinalIgnoreCase);
        _surfaceAncestry.Add(SurfaceIdentity(baseUri));
    }

    public String DocumentTitle { get; private set; } = RendererText.Lumui;

    public ScrollViewer? DocumentViewport { get; private set; }

    public void PauseDeferredWork() => _deferredScheduler.Pause();

    public void ResumeDeferredWork() => _deferredScheduler.Resume();

    public Control Render(JsonElement surface)
    {
        PrepareSurface(surface);
        Boolean viewerWorkspace = ViewerWorkspaceRenderer.Matches(surface);
        Control content = RenderPreparedSurface(surface, viewerWorkspace);
        return WrapSurface(content, viewerWorkspace);
    }

    public async Task<Control> RenderAsync(
        JsonElement surface,
        CancellationToken cancellationToken = default)
    {
        Boolean viewerWorkspace = await Task.Run(
            () =>
            {
                PrepareSurface(surface);
                return ViewerWorkspaceRenderer.Matches(surface);
            },
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return await RunOnUiThreadAsync(
            () => RenderPreparedAsync(
                surface,
                viewerWorkspace,
                cancellationToken));
    }

    private async Task<Control> RenderPreparedAsync(
        JsonElement surface,
        Boolean viewerWorkspace,
        CancellationToken cancellationToken)
    {
        Control content;
        Boolean readingIsDeferred;
        if (viewerWorkspace)
        {
            content = await RenderViewerWorkspaceAsync(
                surface,
                cancellationToken);
            readingIsDeferred = false;
        }
        else if (_embeddedPresentation
            && _settings.Profile.Kind is DeviceProfileKind.Web
                or DeviceProfileKind.Desktop)
        {
            content = RenderCurrentPage(surface, Int32.MaxValue);
            readingIsDeferred = false;
        }
        else if (_settings.Output.Mode == OutputMode.ScreenReader)
        {
            content = RenderScreenReaderDeferred(
                surface,
                cancellationToken);
            readingIsDeferred = true;
        }
        else if (_settings.Output.Mode != OutputMode.ScreenReader
            && _settings.Interaction.Mode != InteractionMode.Guided
            && _settings.Profile.Kind is DeviceProfileKind.Web
                or DeviceProfileKind.Desktop)
        {
            content = SurfaceChromeEnabled(surface)
                ? await RenderStandardAsync(surface, cancellationToken)
                : RenderCurrentPageDeferred(surface, cancellationToken);
            readingIsDeferred = true;
        }
        else
        {
            await YieldRenderingAsync(cancellationToken);
            content = RenderPreparedSurface(surface, viewerWorkspace);
            readingIsDeferred = false;
        }
        cancellationToken.ThrowIfCancellationRequested();
        Control wrapped = WrapSurface(content, viewerWorkspace, false);
        if (_settings.BionicReading && !readingIsDeferred)
        {
            await ReadingTextFormatter.ApplyTreeAsync(
                wrapped,
                true,
                cancellationToken,
                _deferredScheduler.YieldAsync);
        }
        return wrapped;
    }

    private static Task<T> RunOnUiThreadAsync<T>(Func<Task<T>> work)
    {
        return Dispatcher.UIThread.CheckAccess()
            ? work()
            : Dispatcher.UIThread.InvokeAsync(work);
    }

    private void PrepareSurface(JsonElement surface)
    {
        DocumentViewport = null;
        DocumentTitle = Text(
            surface,
            LumuiProtocol.Fields.Title,
            RendererText.Lumui);
        _inputs.Clear();
        _brand = BrandDefinition.FromSurface(
            surface,
            _appearance,
            _settings.HighContrast,
            _settings.ColorVision);
        _styler = new AppearanceStyler(_appearance, _brand);
        JsonElement? page = CurrentPage(surface);
        _preparedPageAvailable = page is not null;
        _preparedRegions = page is not null
            && page.Value.TryGetProperty(
                LumuiProtocol.Fields.Regions,
                out JsonElement regions)
            && regions.ValueKind == JsonValueKind.Array
                ? regions.EnumerateArray().ToArray()
                : Array.Empty<JsonElement>();
    }

    private static Boolean SurfaceChromeEnabled(JsonElement surface)
    {
        return !surface.TryGetProperty("metadata", out JsonElement metadata)
            || metadata.ValueKind != JsonValueKind.Object
            || !metadata.TryGetProperty("web", out JsonElement web)
            || web.ValueKind != JsonValueKind.Object
            || !web.TryGetProperty("chrome", out JsonElement chrome)
            || chrome.ValueKind != JsonValueKind.False;
    }

    private Control RenderPreparedSurface(
        JsonElement surface,
        Boolean viewerWorkspace)
    {
        if (!viewerWorkspace && !SurfaceChromeEnabled(surface))
        {
            return RenderCurrentPage(surface, Int32.MaxValue);
        }
        return viewerWorkspace
            ? RenderViewerWorkspace(surface)
            : _settings.Output.Mode == OutputMode.ScreenReader
                ? RenderScreenReader(surface)
                : _settings.Interaction.Mode == InteractionMode.Guided
                ? RenderGuided(surface)
                : _settings.Profile.Kind switch
                {
                    DeviceProfileKind.Web => RenderDesktop(surface),
                    DeviceProfileKind.Desktop => RenderDesktop(surface),
                    DeviceProfileKind.Tablet => RenderTablet(surface),
                    DeviceProfileKind.Phone => RenderCompact(surface),
                    DeviceProfileKind.Watch => RenderWatch(surface),
                    DeviceProfileKind.Kiosk => RenderKiosk(surface),
                    DeviceProfileKind.Appliance => RenderAppliance(surface),
                    _ => RenderStandard(surface)
                };
    }

    private Control WrapSurface(
        Control content,
        Boolean viewerWorkspace,
        Boolean applyReading = true)
    {
        ContentControl themed = new ContentControl
        {
            Content = content,
            Background = Brush(_appearance.Background),
            Foreground = Brush(_appearance.Text),
            FontFamily = new FontFamily(_appearance.FontFamily),
            FontSize = Font(15D),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        if (applyReading && _settings.BionicReading)
        {
            ReadingTextFormatter.ApplyTree(themed, true);
        }
        String automationName = RendererText.Presentation(
            DocumentTitle,
            _settings.Profile.Label);
        AutomationProperties.SetName(themed, automationName);
        themed.VerticalAlignment = viewerWorkspace || DocumentViewport is not null
            ? VerticalAlignment.Stretch
            : VerticalAlignment.Top;
        return themed;
    }

    private Task<Control> RenderStandardAsync(
        JsonElement surface,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<RenderBlockPlan> blocks = new List<RenderBlockPlan>
        {
            new RenderBlockPlan(
                "application-header",
                84D,
                token => RenderBlockAsync(
                    () => Task.FromResult(RenderApplicationHeader(surface)),
                    token))
        };
        if (!_preparedPageAvailable)
        {
            blocks.Add(new RenderBlockPlan(
                "empty-page",
                120D,
                token => RenderBlockAsync(
                    () => Task.FromResult<Control>(new TextBlock
                    {
                        Text = RendererText.NoPage,
                        Margin = new Thickness(24D),
                        Foreground = Brush(_appearance.Muted)
                    }),
                    token)));
        }
        else
        {
            for (Int32 index = 0; index < _preparedRegions.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                JsonElement region = _preparedRegions[index];
                String key = Text(
                    region,
                    LumuiProtocol.Fields.Id,
                    "region-" + index.ToString(CultureInfo.InvariantCulture));
                blocks.Add(new RenderBlockPlan(
                    key,
                    EstimatedRegionHeight(region),
                    token => RenderBlockAsync(
                        () => RenderRegionAsync(region, token),
                        token)));
            }
        }
        blocks.Add(new RenderBlockPlan(
            "navigation",
            280D,
            token => RenderBlockAsync(
                () => Task.FromResult(RenderNavigationGroups(surface)),
                token)));
        VirtualizedDocumentHost host = new VirtualizedDocumentHost(
            blocks,
            _deferredScheduler,
            _renderCancellation.Token);
        DocumentViewport = host.Viewport;
        return Task.FromResult<Control>(host);
    }

    private Control RenderCurrentPageDeferred(
        JsonElement surface,
        CancellationToken cancellationToken)
    {
        JsonElement? page = CurrentPage(surface);
        if (page is null)
        {
            return new TextBlock
            {
                Text = RendererText.NoPage,
                Margin = new Thickness(24D),
                Foreground = Brush(_appearance.Muted)
            };
        }
        List<RenderBlockPlan> blocks = new List<RenderBlockPlan>();
        if (page.Value.TryGetProperty(
                LumuiProtocol.Fields.Regions,
                out JsonElement regions)
            && regions.ValueKind == JsonValueKind.Array)
        {
            Int32 index = 0;
            foreach (JsonElement region in regions.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                JsonElement value = region;
                String key = Text(
                    value,
                    LumuiProtocol.Fields.Id,
                    "region-" + index.ToString(CultureInfo.InvariantCulture));
                blocks.Add(new RenderBlockPlan(
                    key,
                    EstimatedRegionHeight(value),
                    token => RenderBlockAsync(
                        () => RenderRegionAsync(value, token),
                        token)));
                index++;
            }
        }
        VirtualizedDocumentHost host = new VirtualizedDocumentHost(
            blocks,
            _deferredScheduler,
                _renderCancellation.Token,
            1.25D)
        {
            Margin = new Thickness(0D, 0D, 0D, 32D)
        };
        DocumentViewport = host.Viewport;
        return host;
    }

    private async Task<Control> RenderBlockAsync(
        Func<Task<Control>> factory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Control content = await factory();
        cancellationToken.ThrowIfCancellationRequested();
        if (_settings.BionicReading)
        {
            await ReadingTextFormatter.ApplyTreeAsync(
                content,
                true,
                cancellationToken,
                _deferredScheduler.YieldAsync);
        }
        return content;
    }

    private Double EstimatedRegionHeight(JsonElement region)
    {
        String role = Text(
            region,
            LumuiProtocol.Fields.Role,
            LumuiProtocol.RegionRoles.Supporting);
        Double scale = Math.Clamp(
            0.35D + (0.65D * _settings.TextScale * _settings.PageScale),
            0.55D,
            3D);
        if (role == LumuiProtocol.RegionRoles.Introduction)
        {
            return 460D * scale;
        }
        Double estimate = 120D;
        Int32 tileCount = 0;
        Int32 columns = Math.Max(1, TileColumns());
        if (scale >= 1.35D)
        {
            columns = 1;
        }
        else if (scale >= 1.12D)
        {
            columns = Math.Max(1, columns - 1);
        }
        if (region.TryGetProperty(
                LumuiProtocol.Fields.Items,
                out JsonElement items)
            && items.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in items.EnumerateArray())
            {
                String kind = Text(item, LumuiProtocol.Fields.Kind);
                if (columns > 1
                    && kind is LumuiProtocol.ComponentKinds.DetailOption
                        or LumuiProtocol.ComponentKinds.Link)
                {
                    tileCount++;
                    continue;
                }
                estimate += EstimatedItemHeight(kind) * scale;
            }
        }
        estimate += Math.Ceiling(tileCount / (Double)columns) * 154D * scale;
        return Math.Clamp(estimate, 220D * scale, 16_000D);
    }

    private static Double EstimatedItemHeight(String kind) => kind switch
    {
        LumuiProtocol.ComponentKinds.Text or
        LumuiProtocol.ComponentKinds.Badge or
        LumuiProtocol.ComponentKinds.Icon or
        LumuiProtocol.ComponentKinds.ValueDisplay or
        LumuiProtocol.ComponentKinds.Status or
        LumuiProtocol.ComponentKinds.Progress or
        LumuiProtocol.ComponentKinds.Meter => 68D,
        LumuiProtocol.ComponentKinds.Button or
        LumuiProtocol.ComponentKinds.Toggle or
        LumuiProtocol.ComponentKinds.CheckBox or
        LumuiProtocol.ComponentKinds.RadioGroup or
        LumuiProtocol.ComponentKinds.ComboBox or
        LumuiProtocol.ComponentKinds.MultiSelect or
        LumuiProtocol.ComponentKinds.TextField => 82D,
        LumuiProtocol.ComponentKinds.Section or
        LumuiProtocol.ComponentKinds.Form => 520D,
        LumuiProtocol.ComponentKinds.Map or
        LumuiProtocol.ComponentKinds.Chart or
        LumuiProtocol.ComponentKinds.Image or
        LumuiProtocol.ComponentKinds.Video or
        LumuiProtocol.ComponentKinds.VideoPlayer or
        LumuiProtocol.ComponentKinds.Preview => 380D,
        LumuiProtocol.ComponentKinds.Grid or
        LumuiProtocol.ComponentKinds.List or
        LumuiProtocol.ComponentKinds.Table or
        LumuiProtocol.ComponentKinds.Calendar => 320D,
        _ => 132D
    };

    private Task<Control> RenderRegionAsync(
        JsonElement region,
        CancellationToken cancellationToken)
    {
        String kind = Text(region, LumuiProtocol.Fields.Kind);
        return kind is LumuiProtocol.ComponentKinds.Section
                or LumuiProtocol.ComponentKinds.Form
            ? RenderSectionAsync(region, cancellationToken)
            : Task.FromResult(RenderNode(region));
    }

    private Control DeferredRegion(JsonElement region)
    {
        String role = Text(
            region,
            LumuiProtocol.Fields.Role,
            LumuiProtocol.RegionRoles.Supporting);
        Double height = role == LumuiProtocol.RegionRoles.Introduction
            ? 520D
            : 320D;
        return DeferredControl(
            cancellationToken => RenderRegionAsync(region, cancellationToken),
            height);
    }

    private Control RenderSectionItem(JsonElement item)
    {
        String kind = Text(item, LumuiProtocol.Fields.Kind);
        if (_embeddedPresentation || !ShouldDeferComponent(kind))
        {
            return RenderNode(item);
        }
        Double estimatedHeight = kind switch
        {
            LumuiProtocol.ComponentKinds.Preview => 360D,
            LumuiProtocol.ComponentKinds.Map or
            LumuiProtocol.ComponentKinds.Chart or
            LumuiProtocol.ComponentKinds.Video or
            LumuiProtocol.ComponentKinds.VideoPlayer => 320D,
            LumuiProtocol.ComponentKinds.Grid or
            LumuiProtocol.ComponentKinds.List or
            LumuiProtocol.ComponentKinds.Table or
            LumuiProtocol.ComponentKinds.Calendar => 280D,
            LumuiProtocol.ComponentKinds.Form or
            LumuiProtocol.ComponentKinds.Section => 220D,
            _ => 120D
        };
        return DeferredControl(
            () => RenderNode(item),
            estimatedHeight);
    }

    private static Boolean ShouldDeferComponent(String kind) => kind is
        LumuiProtocol.ComponentKinds.Preview or
        LumuiProtocol.ComponentKinds.Map or
        LumuiProtocol.ComponentKinds.Image or
        LumuiProtocol.ComponentKinds.ImageCollection or
        LumuiProtocol.ComponentKinds.Figure or
        LumuiProtocol.ComponentKinds.Audio or
        LumuiProtocol.ComponentKinds.AudioPlayer or
        LumuiProtocol.ComponentKinds.Video or
        LumuiProtocol.ComponentKinds.VideoPlayer or
        LumuiProtocol.ComponentKinds.Grid or
        LumuiProtocol.ComponentKinds.List or
        LumuiProtocol.ComponentKinds.Table or
        LumuiProtocol.ComponentKinds.Form or
        LumuiProtocol.ComponentKinds.Section;

    private Control DeferredControl(
        Func<Control> factory,
        Double estimatedHeight) => DeferredControl(
            _ => Task.FromResult(factory()),
            estimatedHeight);

    private Control DeferredControl(
        Func<CancellationToken, Task<Control>> factory,
        Double estimatedHeight)
    {
        ContentControl host = new ContentControl
        {
            MinHeight = estimatedHeight,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        Boolean created = false;
        Boolean scheduled = false;
        Rect viewport = default;
        CancellationTokenSource? lifetime =
            CancellationTokenSource.CreateLinkedTokenSource(
            _renderCancellation.Token);
        EventHandler<EffectiveViewportChangedEventArgs>? handler = null;
        handler = (_, eventArgs) =>
        {
            viewport = eventArgs.EffectiveViewport;
            if (created
                || scheduled
                || _disposed
                || !IsNearViewport(
                    host,
                    viewport,
                    estimatedHeight))
            {
                return;
            }
            scheduled = true;
            CancellationToken requestToken = lifetime?.Token
            ?? _renderCancellation.Token;
            Int32 priority = IsInViewport(
                host,
                viewport,
                estimatedHeight)
                    ? 0
                    : 1;
            _deferredScheduler.Enqueue(
                async schedulerToken =>
                {
                    CancellationTokenSource? request = lifetime;
                    if (_disposed || request is null)
                    {
                        return;
                    }
                    using CancellationTokenSource linked =
                        CancellationTokenSource.CreateLinkedTokenSource(
                            request.Token,
                            schedulerToken);
                    if (!IsNearViewport(host, viewport, estimatedHeight))
                    {
                        scheduled = false;
                        return;
                    }
                    Control content = await factory(linked.Token);
                    linked.Token.ThrowIfCancellationRequested();
                    if (_settings.BionicReading)
                    {
                        await ReadingTextFormatter.ApplyTreeAsync(
                            content,
                            true,
                            linked.Token,
                            _deferredScheduler.YieldAsync);
                    }
                    linked.Token.ThrowIfCancellationRequested();
                    created = true;
                    host.EffectiveViewportChanged -= handler;
                    host.Content = content;
                    CancellationTokenSource? completed = lifetime;
                    lifetime = null;
                    completed?.Dispose();
                },
                priority,
                requestToken);
        };
        host.EffectiveViewportChanged += handler;
        host.DetachedFromVisualTree += (_, _) =>
        {
            host.EffectiveViewportChanged -= handler;
            CancellationTokenSource? detached = lifetime;
            lifetime = null;
            detached?.Cancel();
            detached?.Dispose();
        };
        return host;
    }

    private static Boolean IsNearViewport(
        Control control,
        Rect viewport,
        Double estimatedHeight)
    {
        if (viewport.Width <= 0D || viewport.Height <= 0D)
        {
            return false;
        }
        Double width = Math.Max(
            1D,
            Math.Max(control.Bounds.Width, control.DesiredSize.Width));
        Double height = Math.Max(
            1D,
            Math.Max(
                estimatedHeight,
                Math.Max(control.Bounds.Height, control.DesiredSize.Height)));
        const Double preload = 960D;
        return viewport.Right >= -preload
            && viewport.Bottom >= -preload
            && viewport.X <= width + preload
            && viewport.Y <= height + preload;
    }

    private static Boolean IsInViewport(
        Control control,
        Rect viewport,
        Double estimatedHeight)
    {
        if (viewport.Width <= 0D || viewport.Height <= 0D)
        {
            return false;
        }
        Double width = Math.Max(
            1D,
            Math.Max(control.Bounds.Width, control.DesiredSize.Width));
        Double height = Math.Max(
            1D,
            Math.Max(
                estimatedHeight,
                Math.Max(control.Bounds.Height, control.DesiredSize.Height)));
        return viewport.Right >= 0D
            && viewport.Bottom >= 0D
            && viewport.X <= width
            && viewport.Y <= height;
    }

    private async Task YieldRenderingAsync(
        CancellationToken cancellationToken)
    {
        await _deferredScheduler.YieldAsync(cancellationToken);
    }

    private Control RenderViewerWorkspace(JsonElement surface)
    {
        ViewerWorkspaceRenderer renderer = new ViewerWorkspaceRenderer(
            _client,
            _baseUri,
            _settings,
            _openExternal,
            _download,
            _surfaceAncestry,
            _inputSuggestion,
            _embeddedPresentation);
        _workspaceRenderers.Add(renderer);
        return renderer.Render(surface);
    }

    private async Task<Control> RenderViewerWorkspaceAsync(
        JsonElement surface,
        CancellationToken cancellationToken)
    {
        ViewerWorkspaceRenderer renderer = new ViewerWorkspaceRenderer(
            _client,
            _baseUri,
            _settings,
            _openExternal,
            _download,
            _surfaceAncestry,
            _inputSuggestion,
            _embeddedPresentation);
        _workspaceRenderers.Add(renderer);
        return await renderer.RenderAsync(surface, cancellationToken);
    }

    private Control RenderStandard(JsonElement surface)
    {
        StackPanel root = new StackPanel
        {
            Spacing = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        root.Children.Add(RenderApplicationHeader(surface));
        root.Children.Add(RenderCurrentPage(surface, Int32.MaxValue));
        root.Children.Add(RenderNavigationGroups(surface));
        return root;
    }

    private Control RenderTablet(JsonElement surface)
    {
        return RenderRegionWorkspace(surface, false);
    }

    private Control RenderDesktop(JsonElement surface)
    {
        return RenderStandard(surface);
    }

    private Control RenderDesktopPage(JsonElement surface)
    {
        JsonElement? page = CurrentPage(surface);
        if (page is null)
        {
            return new TextBlock
            {
                Text = RendererText.NoPage,
                Margin = new Thickness(48D),
                Foreground = Brush(_appearance.Muted)
            };
        }

        Double pagePadding = Math.Clamp(
            _settings.Profile.FrameWidth * 0.025D,
            24D,
            48D);
        Double pageGap = Math.Clamp(
            _settings.Profile.FrameWidth * 0.02D,
            16D,
            28D);
        Grid layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(
                "*,*,*,*,*,*,*,*,*,*,*,*"),
            ColumnSpacing = pageGap,
            RowSpacing = pageGap,
            MaxWidth = Math.Min(_settings.Profile.ContentWidth, 1344D),
            Margin = new Thickness(
                pagePadding,
                pagePadding,
                pagePadding,
                Math.Max(32D, pagePadding)),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Int32 row = 0;
        Boolean halfRowOpen = false;

        if (page.Value.TryGetProperty(
                LumuiProtocol.Fields.Regions,
                out JsonElement regions)
            && regions.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement region in regions.EnumerateArray())
            {
                String role = Text(
                    region,
                    LumuiProtocol.Fields.Role,
                    LumuiProtocol.RegionRoles.Supporting);
                Boolean introduction = role == LumuiProtocol.RegionRoles.Introduction;
                Boolean callToAction = role == LumuiProtocol.RegionRoles.CallToAction;
                if ((introduction || callToAction) && halfRowOpen)
                {
                    row++;
                    halfRowOpen = false;
                }
                while (layout.RowDefinitions.Count <= row)
                {
                    layout.RowDefinitions.Add(
                        new RowDefinition(GridLength.Auto));
                }

                Control rendered = Text(
                        region,
                        LumuiProtocol.Fields.Kind)
                    is LumuiProtocol.ComponentKinds.Section
                        or LumuiProtocol.ComponentKinds.Form
                    ? RenderDesktopWorkspaceSection(region, introduction)
                    : RenderNode(region);
                rendered.HorizontalAlignment = HorizontalAlignment.Stretch;
                layout.Children.Add(rendered);

                if (introduction)
                {
                    Grid.SetColumn(rendered, 0);
                    Grid.SetColumnSpan(rendered, 12);
                    Grid.SetRow(rendered, row++);
                    continue;
                }
                if (callToAction)
                {
                    Grid.SetColumn(rendered, 2);
                    Grid.SetColumnSpan(rendered, 8);
                    Grid.SetRow(rendered, row++);
                    continue;
                }

                Grid.SetColumn(rendered, halfRowOpen ? 6 : 0);
                Grid.SetColumnSpan(rendered, 6);
                Grid.SetRow(rendered, row);
                if (halfRowOpen)
                {
                    row++;
                }
                halfRowOpen = !halfRowOpen;
            }
        }

        return layout;
    }

    private Control RenderDesktopWorkspaceSection(
        JsonElement node,
        Boolean introduction)
    {
        StackPanel content = new StackPanel
        {
            Spacing = 13D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        String label = Text(node, LumuiProtocol.Fields.Label);
        if (label.Length > 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = label.ToUpperInvariant(),
                Foreground = Brush(_brand.Accent),
                FontWeight = FontWeight.Bold,
                FontSize = Font(12D),
                LetterSpacing = 1.1D
            });
        }
        if (node.TryGetProperty(
                LumuiProtocol.Fields.Items,
                out JsonElement items)
            && items.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in items.EnumerateArray())
            {
                content.Children.Add(
                    RenderDesktopWorkspaceItem(item, introduction));
            }
        }
        if (!introduction)
        {
            return content;
        }

        Double introductionPadding = Math.Clamp(
            _settings.Profile.FrameWidth * 0.03D,
            28D,
            52D);
        content.Margin = new Thickness(introductionPadding);
        return new Border
        {
            CornerRadius = new CornerRadius(0D),
            ClipToBounds = true,
            Child = _styler.Hero(content, DeviceProfileKind.Desktop)
        };
    }

    private Control RenderDesktopWorkspaceItem(
        JsonElement item,
        Boolean introduction)
    {
        Control rendered = RenderNode(item);
        if (rendered is not TextBlock text
            || Text(item, LumuiProtocol.Fields.Kind)
                != LumuiProtocol.ComponentKinds.Text)
        {
            return rendered;
        }
        String role = Text(
            item,
            LumuiProtocol.Fields.TextRole,
            LumuiProtocol.TextRoles.Body);
        if (role == LumuiProtocol.TextRoles.Heading)
        {
            Double size = introduction
                ? Math.Clamp(
                    _settings.Profile.FrameWidth * 0.031D,
                    32D,
                    56D)
                : Math.Clamp(
                    _settings.Profile.FrameWidth * 0.02D,
                    23.2D,
                    33.6D);
            text.FontSize = Font(size);
            text.LineHeight = Font(size * (introduction ? 1.04D : 1.2D));
            text.MaxWidth = introduction ? 900D : 680D;
        }
        else if (role == LumuiProtocol.TextRoles.Lead)
        {
            text.FontSize = Font(19D);
            text.LineHeight = Font(29D);
            text.MaxWidth = 900D;
            text.Foreground = Brush(_appearance.Muted);
        }
        return rendered;
    }

    private Control RenderViewerApplicationHeader(JsonElement surface)
    {
        Grid header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 24D,
            MaxWidth = _settings.Profile.FrameWidth,
            MinHeight = 64D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        header.Children.Add(RenderIdentityContent(surface, 38D, true));

        if (surface.TryGetProperty(
                LumuiProtocol.Fields.Identity,
                out JsonElement identity))
        {
            String home = Text(identity, LumuiProtocol.Fields.Home);
            Uri? uri = ResolveUri(home, allowExternal: false);
            if (uri is not null)
            {
                Button button = new Button
                {
                    Content = RendererText.Home,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                _styler.ApplyNavigationButton(button, false);
                button.Foreground = Brush(_brand.Accent);
                button.Click += async (_, _) => await _navigate(uri);
                header.Children.Add(button);
                Grid.SetColumn(button, 1);
            }
        }

        return new Border
        {
            Background = Brush(_appearance.Surface),
            BorderBrush = Brush(_appearance.Border),
            BorderThickness = new Thickness(0D, 0D, 0D, 1D),
            Padding = new Thickness(38D, 10D),
            Child = header
        };
    }

    private Control RenderRegionWorkspace(
        JsonElement surface,
        Boolean desktop)
    {
        JsonElement? page = CurrentPage(surface);
        if (page is null)
        {
            return new TextBlock
            {
                Text = RendererText.NoPage,
                Margin = new Thickness(24D),
                Foreground = Brush(_appearance.Muted)
            };
        }
        StackPanel root = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        root.Children.Add(
            desktop
                ? RenderApplicationHeader(surface)
                : RenderCompactHeader(surface));
        Grid workspace = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(
                desktop ? "220,*" : "180,*"),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        StackPanel sectionRail = new StackPanel
        {
            Spacing = 8D
        };
        sectionRail.Children.Add(new TextBlock
        {
            Text = RendererText.Sections,
            Foreground = Brush(_appearance.Muted),
            FontWeight = FontWeight.Bold
        });
        if (!desktop
            && surface.TryGetProperty(
                LumuiProtocol.Fields.Pages,
                out JsonElement workspacePages)
            && workspacePages.ValueKind == JsonValueKind.Array
            && workspacePages.GetArrayLength() > 1)
        {
            sectionRail.Children.Add(new Expander
            {
                Header = RendererText.Pages,
                Content = RenderRouteRail(surface)
            });
        }
        StackPanel pageContent = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        if (page.Value.TryGetProperty(
                LumuiProtocol.Fields.Regions,
                out JsonElement regions)
            && regions.ValueKind == JsonValueKind.Array)
        {
            Int32 index = 0;
            foreach (JsonElement region in regions.EnumerateArray())
            {
                index++;
                JsonElement value = region;
                Control rendered = DeferredRegion(value);
                pageContent.Children.Add(rendered);
                Button button = new Button
                {
                    Content = Text(
                        region,
                        LumuiProtocol.Fields.Label,
                        RendererText.Section(index)),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left
                };
                ApplyLinkButton(button);
                button.Click += (_, _) => rendered.BringIntoView();
                sectionRail.Children.Add(button);
            }
        }
        Border rail = new Border
        {
            Margin = new Thickness(18D, 18D, 0D, 24D),
            Child = sectionRail
        };
        ApplySoftPanel(rail);
        workspace.Children.Add(rail);
        workspace.Children.Add(pageContent);
        Grid.SetColumn(pageContent, 1);
        root.Children.Add(workspace);
        if (desktop)
        {
            root.Children.Add(RenderNavigationGroups(surface));
        }
        return root;
    }

    private Control RenderCompact(JsonElement surface)
    {
        JsonElement? page = CurrentPage(surface);
        if (page is null)
        {
            return new TextBlock
            {
                Text = RendererText.NoPage,
                Margin = new Thickness(24D),
                Foreground = Brush(_appearance.Muted)
            };
        }

        Double availableHeight = Math.Max(
            420D,
            _settings.Profile.FrameHeight
            - (2D * _settings.Profile.FrameBorder)
            - _settings.Profile.ChromeHeight);
        Grid root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Height = availableHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        Control header = RenderCompactHeader(surface);
        root.Children.Add(header);

        StackPanel pageContent = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        if (surface.TryGetProperty(
                LumuiProtocol.Fields.Pages,
                out JsonElement compactPages)
            && compactPages.ValueKind == JsonValueKind.Array
            && compactPages.GetArrayLength() > 1)
        {
            pageContent.Children.Add(new Expander
            {
                Header = RendererText.Pages,
                Margin = new Thickness(16D, 8D),
                Content = RenderRouteRail(surface)
            });
        }

        List<Control> targets = new List<Control>();
        List<String> labels = new List<String>();
        if (page.Value.TryGetProperty(
                LumuiProtocol.Fields.Regions,
                out JsonElement regions)
            && regions.ValueKind == JsonValueKind.Array)
        {
            Int32 index = 0;
            foreach (JsonElement region in regions.EnumerateArray())
            {
                index++;
                JsonElement value = region;
                String label = Text(
                    value,
                    LumuiProtocol.Fields.Label,
                    RendererText.Section(index));
                Control target;
                if (index > 1)
                {
                    target = new Expander
                    {
                        Header = label,
                        Margin = new Thickness(16D, 6D),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Content = DeferredControl(
                            () => RenderNode(value),
                            180D)
                    };
                }
                else
                {
                    target = DeferredRegion(value);
                }
                labels.Add(label);
                targets.Add(target);
                pageContent.Children.Add(target);
            }
        }

        ScrollViewer pageScroll = new ScrollViewer
        {
            Content = pageContent,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        root.Children.Add(pageScroll);
        Grid.SetRow(pageScroll, 1);

        Grid navigation = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 6D,
            Margin = new Thickness(12D, 8D, 12D, 12D)
        };
        for (Int32 index = 0; index < Math.Min(3, targets.Count); index++)
        {
            Control target = targets[index];
            Button button = new Button
            {
                Content = labels[index],
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            ApplyLinkButton(button);
            button.Click += (_, _) =>
            {
                if (target is Expander disclosure)
                {
                    disclosure.IsExpanded = true;
                }
                target.BringIntoView();
            };
            navigation.Children.Add(button);
            Grid.SetColumn(button, index);
        }
        Border navigationHost = new Border
        {
            Background = Brush(_appearance.Surface),
            BorderBrush = Brush(_appearance.Border),
            BorderThickness = new Thickness(0D, 1D, 0D, 0D),
            Child = navigation
        };
        root.Children.Add(navigationHost);
        Grid.SetRow(navigationHost, 2);
        return root;
    }

    private Control RenderWatch(JsonElement surface)
    {
        JsonElement? page = CurrentPage(surface);
        if (page is null)
        {
            return new TextBlock
            {
                Text = RendererText.NoPage,
                Margin = new Thickness(34D, 18D),
                Foreground = Brush(_appearance.Muted),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center
            };
        }

        StackPanel root = new StackPanel
        {
            Margin = new Thickness(30D, 8D, 30D, 24D),
            Spacing = 8D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        Control identity = RenderIdentityContent(surface, 22D);
        identity.HorizontalAlignment = HorizontalAlignment.Center;
        root.Children.Add(identity);

        JsonElement? heading = FindFirst(
            page.Value,
            (JsonElement node) =>
                Text(node, LumuiProtocol.Fields.Kind)
                    == LumuiProtocol.ComponentKinds.Text
                && Text(node, LumuiProtocol.Fields.TextRole)
                    == LumuiProtocol.TextRoles.Heading);
        JsonElement? lead = FindFirst(
            page.Value,
            (JsonElement node) =>
                Text(node, LumuiProtocol.Fields.Kind)
                    == LumuiProtocol.ComponentKinds.Text
                && Text(node, LumuiProtocol.Fields.TextRole)
                    == LumuiProtocol.TextRoles.Lead);

        root.Children.Add(new TextBlock
        {
            Text = heading is null
                ? Text(page.Value, LumuiProtocol.Fields.Title, DocumentTitle)
                : Text(heading.Value, LumuiProtocol.Fields.Text),
            Foreground = Brush(_appearance.Text),
            FontSize = Font(20D),
            FontWeight = FontWeight.Bold,
            LineHeight = Font(24D),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });
        if (lead is not null)
        {
            root.Children.Add(new TextBlock
            {
                Text = Text(lead.Value, LumuiProtocol.Fields.Text),
                Foreground = Brush(_appearance.Muted),
                FontSize = Font(13D),
                LineHeight = Font(18D),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });
        }

        List<JsonElement> actions = ImmediateActions(page.Value);
        foreach (JsonElement action in actions.Take(2))
        {
            root.Children.Add(RenderPrimaryAction(action));
        }

        StackPanel moreContent = new StackPanel
        {
            Spacing = 8D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        JsonElement watchPage = page.Value;
        moreContent.Children.Add(DeferredControl(
            () => RenderPage(watchPage, true),
            120D));
        if (surface.TryGetProperty(
                LumuiProtocol.Fields.Pages,
                out JsonElement watchPages)
            && watchPages.ValueKind == JsonValueKind.Array
            && watchPages.GetArrayLength() > 1)
        {
            moreContent.Children.Add(RenderRouteRail(surface));
        }
        root.Children.Add(new Expander
        {
            Header = RendererText.More,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = moreContent
        });
        return root;
    }

    private List<JsonElement> ImmediateActions(JsonElement page)
    {
        List<JsonElement> actions = new List<JsonElement>();
        HashSet<String> identities =
            new HashSet<String>(StringComparer.Ordinal);
        JsonElement? menu = FindFirst(
            page,
            (JsonElement node) =>
                Text(node, LumuiProtocol.Fields.Kind)
                    == LumuiProtocol.ComponentKinds.Menu);
        if (menu is not null
            && menu.Value.TryGetProperty(
                LumuiProtocol.Fields.Items,
                out JsonElement menuItems)
            && menuItems.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in menuItems.EnumerateArray())
            {
                AddImmediateAction(actions, identities, item);
            }
        }
        foreach (JsonElement item in FindAll(
            page,
            (JsonElement node) =>
            {
                String kind = Text(node, LumuiProtocol.Fields.Kind);
                return kind == LumuiProtocol.ComponentKinds.Link
                    || kind == LumuiProtocol.ComponentKinds.Button;
            }))
        {
            AddImmediateAction(actions, identities, item);
        }
        return actions;
    }

    private static void AddImmediateAction(
        ICollection<JsonElement> actions,
        ISet<String> identities,
        JsonElement item)
    {
        String kind = Text(item, LumuiProtocol.Fields.Kind);
        if (kind != LumuiProtocol.ComponentKinds.Link
            && kind != LumuiProtocol.ComponentKinds.Button)
        {
            return;
        }
        String identity = Text(item, LumuiProtocol.Fields.Id);
        if (identity.Length == 0)
        {
            identity = item.GetRawText();
        }
        if (identities.Add(identity))
        {
            actions.Add(item);
        }
    }

    private Control RenderPrimaryAction(JsonElement node)
    {
        String kind = Text(node, LumuiProtocol.Fields.Kind);
        if (kind == LumuiProtocol.ComponentKinds.Button)
        {
            Control action = RenderButton(node);
            action.HorizontalAlignment = HorizontalAlignment.Stretch;
            return action;
        }

        String label = Text(
            node,
            LumuiProtocol.Fields.Label,
            RendererText.Open);
        Button button = new Button
        {
            Content = label,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        ApplyPrimaryButton(button);
        AutomationProperties.SetName(button, label);
        AutomationProperties.SetAutomationId(
            button,
            Text(node, LumuiProtocol.Fields.Id));

        String href = Text(node, LumuiProtocol.Fields.Href);
        Boolean external = Boolean(node, LumuiProtocol.Fields.External);
        Uri? uri = ResolveUri(href, external);
        button.IsEnabled = uri is not null;
        if (uri is not null)
        {
            button.Click += async (_, _) =>
            {
                if (external)
                {
                    await _openExternal(uri);
                }
                else
                {
                    await _navigate(uri);
                }
            };
        }
        return button;
    }

    private Control RenderKiosk(JsonElement surface)
    {
        JsonElement? page = CurrentPage(surface);
        if (page is null)
        {
            return new TextBlock
            {
                Text = RendererText.NoPage,
                Margin = new Thickness(24D),
                Foreground = Brush(_appearance.Muted)
            };
        }

        Double availableHeight = Math.Max(
            420D,
            _settings.Profile.FrameHeight
            - (2D * _settings.Profile.FrameBorder)
            - _settings.Profile.ChromeHeight);
        Grid root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Height = availableHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Border header = new Border
        {
            Background = Brush(_appearance.Surface),
            BorderBrush = Brush(_appearance.Border),
            BorderThickness = new Thickness(0D, 0D, 0D, 1D),
            Padding = new Thickness(28D, 14D),
            Child = RenderIdentityContent(surface, 34D)
        };
        root.Children.Add(header);

        StackPanel content = new StackPanel
        {
            MaxWidth = _settings.Profile.ContentWidth,
            Margin = new Thickness(42D, 30D),
            Spacing = 16D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        String pageTitle = Text(
            page.Value,
            LumuiProtocol.Fields.Title,
            DocumentTitle);
        content.Children.Add(new TextBlock
        {
            Text = pageTitle.ToUpperInvariant(),
            Foreground = Brush(_appearance.Accent),
            FontWeight = FontWeight.Bold,
            FontSize = Font(13D),
            LetterSpacing = 1D
        });

        JsonElement? heading = FindFirst(
            page.Value,
            (JsonElement node) =>
                Text(node, LumuiProtocol.Fields.Kind)
                    == LumuiProtocol.ComponentKinds.Text
                && Text(node, LumuiProtocol.Fields.TextRole)
                    == LumuiProtocol.TextRoles.Heading);
        JsonElement? lead = FindFirst(
            page.Value,
            (JsonElement node) =>
                Text(node, LumuiProtocol.Fields.Kind)
                    == LumuiProtocol.ComponentKinds.Text
                && Text(node, LumuiProtocol.Fields.TextRole)
                    == LumuiProtocol.TextRoles.Lead);
        content.Children.Add(new TextBlock
        {
            Text = heading is null
                ? pageTitle
                : Text(heading.Value, LumuiProtocol.Fields.Text),
            Foreground = Brush(_appearance.Text),
            FontSize = Font(42D),
            FontWeight = FontWeight.Bold,
            LineHeight = Font(50D),
            TextWrapping = TextWrapping.Wrap
        });
        if (lead is not null)
        {
            content.Children.Add(new TextBlock
            {
                Text = Text(lead.Value, LumuiProtocol.Fields.Text),
                Foreground = Brush(_appearance.Muted),
                FontSize = Font(18D),
                LineHeight = Font(27D),
                TextWrapping = TextWrapping.Wrap
            });
        }

        WrapPanel actions = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (JsonElement action in ImmediateActions(page.Value).Take(4))
        {
            Control rendered = RenderPrimaryAction(action);
            rendered.Width = 260D;
            rendered.MinHeight = 74D;
            rendered.Margin = new Thickness(0D, 0D, 14D, 14D);
            actions.Children.Add(rendered);
        }
        content.Children.Add(actions);

        StackPanel moreContent = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        JsonElement kioskPage = page.Value;
        moreContent.Children.Add(DeferredControl(
            () => RenderPage(kioskPage, true),
            180D));
        if (surface.TryGetProperty(
                LumuiProtocol.Fields.Pages,
                out JsonElement kioskPages)
            && kioskPages.ValueKind == JsonValueKind.Array
            && kioskPages.GetArrayLength() > 1)
        {
            moreContent.Children.Add(RenderRouteRail(surface));
        }
        content.Children.Add(new Expander
        {
            Header = RendererText.More,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = moreContent
        });

        ScrollViewer scroll = new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        root.Children.Add(scroll);
        Grid.SetRow(scroll, 1);
        return root;
    }

    private Control RenderAppliance(JsonElement surface)
    {
        JsonElement? page = CurrentPage(surface);
        if (page is null)
        {
            return new TextBlock
            {
                Text = RendererText.NoPage,
                Margin = new Thickness(24D),
                Foreground = Brush(_appearance.Muted)
            };
        }

        Double availableHeight = Math.Max(
            360D,
            _settings.Profile.FrameHeight
            - (2D * _settings.Profile.FrameBorder)
            - _settings.Profile.ChromeHeight);
        Grid root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Height = availableHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        root.Children.Add(RenderCompactHeader(surface));

        StackPanel content = new StackPanel
        {
            Margin = new Thickness(18D),
            Spacing = 12D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Grid primary = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1.3*,.7*"),
            ColumnSpacing = 14D
        };

        JsonElement? heading = FindFirst(
            page.Value,
            (JsonElement node) =>
                Text(node, LumuiProtocol.Fields.Kind)
                    == LumuiProtocol.ComponentKinds.Text
                && Text(node, LumuiProtocol.Fields.TextRole)
                    == LumuiProtocol.TextRoles.Heading);
        JsonElement? lead = FindFirst(
            page.Value,
            (JsonElement node) =>
                Text(node, LumuiProtocol.Fields.Kind)
                    == LumuiProtocol.ComponentKinds.Text
                && Text(node, LumuiProtocol.Fields.TextRole)
                    == LumuiProtocol.TextRoles.Lead);

        StackPanel summary = new StackPanel
        {
            Spacing = 8D
        };
        summary.Children.Add(new TextBlock
        {
            Text = Text(
                page.Value,
                LumuiProtocol.Fields.Title,
                DocumentTitle).ToUpperInvariant(),
            Foreground = Brush(_appearance.Accent),
            FontWeight = FontWeight.Bold,
            FontSize = Font(11D),
            LetterSpacing = 1D
        });
        summary.Children.Add(new TextBlock
        {
            Text = heading is null
                ? Text(
                    page.Value,
                    LumuiProtocol.Fields.Title,
                    DocumentTitle)
                : Text(heading.Value, LumuiProtocol.Fields.Text),
            Foreground = Brush(_appearance.Text),
            FontSize = Font(28D),
            FontWeight = FontWeight.Bold,
            LineHeight = Font(34D),
            TextWrapping = TextWrapping.Wrap
        });
        if (lead is not null)
        {
            summary.Children.Add(new TextBlock
            {
                Text = Text(lead.Value, LumuiProtocol.Fields.Text),
                Foreground = Brush(_appearance.Muted),
                FontSize = Font(15D),
                LineHeight = Font(22D),
                TextWrapping = TextWrapping.Wrap
            });
        }
        Border summaryHost = new Border
        {
            Child = summary
        };
        ApplySoftPanel(summaryHost);
        primary.Children.Add(summaryHost);

        StackPanel actions = new StackPanel
        {
            Spacing = 8D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (JsonElement action in ImmediateActions(page.Value).Take(3))
        {
            actions.Children.Add(RenderPrimaryAction(action));
        }
        Border actionsHost = new Border
        {
            Child = actions
        };
        ApplySoftPanel(actionsHost);
        primary.Children.Add(actionsHost);
        Grid.SetColumn(actionsHost, 1);
        content.Children.Add(primary);

        WrapPanel overview = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (JsonElement detail in FindAll(
            page.Value,
            (JsonElement node) =>
                Text(node, LumuiProtocol.Fields.Kind)
                    == LumuiProtocol.ComponentKinds.DetailOption).Take(3))
        {
            Control rendered = RenderDetail(detail);
            rendered.Width = 250D;
            rendered.Margin = new Thickness(0D, 0D, 12D, 12D);
            overview.Children.Add(rendered);
        }
        content.Children.Add(overview);

        StackPanel moreContent = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        JsonElement appliancePage = page.Value;
        moreContent.Children.Add(DeferredControl(
            () => RenderPage(appliancePage, true),
            160D));
        if (surface.TryGetProperty(
                LumuiProtocol.Fields.Pages,
                out JsonElement appliancePages)
            && appliancePages.ValueKind == JsonValueKind.Array
            && appliancePages.GetArrayLength() > 1)
        {
            moreContent.Children.Add(RenderRouteRail(surface));
        }
        content.Children.Add(new Expander
        {
            Header = RendererText.More,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = moreContent
        });

        ScrollViewer scroll = new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        root.Children.Add(scroll);
        Grid.SetRow(scroll, 1);
        return root;
    }

    private Control RenderScreenReader(JsonElement surface)
    {
        StackPanel root = new StackPanel
        {
            Spacing = 0D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        TextBlock context = new TextBlock
        {
            Text = RendererText.LinearReadingOrder,
            Margin = new Thickness(24D, 18D, 24D, 0D),
            Foreground = Brush(_appearance.Muted)
        };
        AutomationProperties.SetName(
            context,
            RendererText.LinearReadingOrderDescription);
        root.Children.Add(context);
        root.Children.Add(RenderIdentity(surface));
        root.Children.Add(RenderCurrentPage(surface, Int32.MaxValue));
        root.Children.Add(RenderNavigation(surface));
        return root;
    }

    private Control RenderScreenReaderDeferred(
        JsonElement surface,
        CancellationToken cancellationToken)
    {
        List<RenderBlockPlan> blocks = new List<RenderBlockPlan>
        {
            new RenderBlockPlan(
                "reading-context",
                130D,
                token => RenderBlockAsync(
                    () => Task.FromResult(RenderReadingContext(surface)),
                    token))
        };
        for (Int32 index = 0; index < _preparedRegions.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            JsonElement region = _preparedRegions[index];
            String key = Text(
                region,
                LumuiProtocol.Fields.Id,
                "reading-region-"
                    + index.ToString(CultureInfo.InvariantCulture));
            blocks.Add(new RenderBlockPlan(
                key,
                EstimatedRegionHeight(region),
                token => RenderBlockAsync(
                    () => RenderRegionAsync(region, token),
                    token)));
        }
        blocks.Add(new RenderBlockPlan(
            "reading-navigation",
            240D,
            token => RenderBlockAsync(
                () => Task.FromResult(RenderNavigation(surface)),
                token)));
        VirtualizedDocumentHost host = new VirtualizedDocumentHost(
            blocks,
            _deferredScheduler,
                    _renderCancellation.Token,
            0.2D);
        DocumentViewport = host.Viewport;
        return host;
    }

    private Control RenderReadingContext(JsonElement surface)
    {
        TextBlock context = new TextBlock
        {
            Text = RendererText.LinearReadingOrder,
            Margin = new Thickness(24D, 18D, 24D, 0D),
            Foreground = Brush(_appearance.Muted)
        };
        AutomationProperties.SetName(
            context,
            RendererText.LinearReadingOrderDescription);
        return new StackPanel
        {
            Children =
            {
                context,
                RenderIdentity(surface)
            }
        };
    }

    private Control RenderGuided(JsonElement surface)
    {
        JsonElement? page = CurrentPage(surface);
        if (page is null
            || !page.Value.TryGetProperty(
                LumuiProtocol.Fields.Regions,
                out JsonElement regionsValue)
            || regionsValue.ValueKind != JsonValueKind.Array
            || regionsValue.GetArrayLength() == 0)
        {
            return new TextBlock
            {
                Text = RendererText.NoPage,
                Margin = new Thickness(24D),
                Foreground = Brush(_appearance.Muted)
            };
        }

        List<JsonElement> regions = regionsValue.EnumerateArray().ToList();
        Boolean watch = _settings.Profile.Kind == DeviceProfileKind.Watch;
        Grid root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        TextBlock stepLabel = new TextBlock
        {
            Foreground = Brush(_appearance.Muted),
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };
        header.Children.Add(new TextBlock
        {
            Text = RendererText.Guided.ToUpperInvariant(),
            Foreground = Brush(_appearance.Accent),
            FontSize = Font(watch ? 11D : 12D),
            FontWeight = FontWeight.Bold,
            LetterSpacing = 1.5D
        });
        header.Children.Add(stepLabel);
        Grid.SetColumn(stepLabel, 1);

        ProgressBar progress = new ProgressBar
        {
            Minimum = 0D,
            Maximum = regions.Count,
            Height = 4D,
            Margin = new Thickness(0D, 9D, 0D, 0D),
            Foreground = Brush(_appearance.Accent),
            Background = Brush(_appearance.SurfaceAlternate)
        };
        Grid headerContent = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            Children =
            {
                header,
                progress
            }
        };
        Grid.SetRow(progress, 1);
        Border guidedHeader = new Border
        {
            Background = Brush(_appearance.Surface),
            BorderBrush = Brush(_appearance.Border),
            BorderThickness = new Thickness(0D, 0D, 0D, 1D),
            Padding = watch
                ? new Thickness(12D, 10D, 12D, 9D)
                : new Thickness(18D, 13D, 18D, 11D),
            Child = headerContent
        };
        root.Children.Add(guidedHeader);

        ContentControl stepContent = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        ScrollViewer stepScroll = new ScrollViewer
        {
            Content = stepContent,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = watch
                ? new Thickness(12D, 0D)
                : new Thickness(18D, 0D)
        };
        DocumentViewport = stepScroll;
        root.Children.Add(stepScroll);
        Grid.SetRow(stepScroll, 1);

        FontAwesomeIcon previousIcon = new FontAwesomeIcon
        {
            Icon = BrowserIcons.Back,
            IconSize = watch ? 13D : 15D,
            Foreground = Brush(_appearance.Accent),
            VerticalAlignment = VerticalAlignment.Center
        };
        Button previous = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10D,
                Children =
                {
                    previousIcon,
                    new TextBlock
                    {
                        Text = RendererText.Back,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            },
            MinWidth = watch ? 104D : 144D,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        TextBlock nextLabel = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center
        };
        FontAwesomeIcon nextIcon = new FontAwesomeIcon
        {
            Icon = BrowserIcons.Forward,
            IconSize = watch ? 13D : 15D,
            Foreground = Brush(_appearance.AccentText),
            VerticalAlignment = VerticalAlignment.Center
        };
        Button next = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10D,
                Children =
                {
                    nextLabel,
                    nextIcon
                }
            },
            MinWidth = watch ? 104D : 144D,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        ApplyLinkButton(previous);
        ApplyPrimaryButton(next);
        AutomationProperties.SetName(previous, RendererText.Back);

        Grid actions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 12D
        };
        actions.Children.Add(previous);
        actions.Children.Add(next);
        Grid.SetColumn(next, 2);
        Border commandBar = new Border
        {
            Background = Brush(_appearance.Surface),
            BorderBrush = Brush(_appearance.Border),
            BorderThickness = new Thickness(0D, 1D, 0D, 0D),
            Padding = watch
                ? new Thickness(12D, 10D, 12D, 14D)
                : new Thickness(18D, 12D, 18D, 16D),
            Child = actions
        };
        root.Children.Add(commandBar);
        Grid.SetRow(commandBar, 2);

        Int32 step = 0;
        Action showStep = () =>
        {
            stepLabel.Text = RendererText.Position(step + 1, regions.Count);
            progress.Value = step + 1;
            StackPanel content = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            content.Children.Add(RenderNode(regions[step]));
            if (step == regions.Count - 1
                && surface.TryGetProperty(
                    LumuiProtocol.Fields.Pages,
                    out JsonElement guidedPages)
                && guidedPages.ValueKind == JsonValueKind.Array
                && guidedPages.GetArrayLength() > 1)
            {
                content.Children.Add(new Expander
                {
                    Header = RendererText.Pages,
                    Margin = new Thickness(12D),
                    Content = RenderRouteRail(surface)
                });
            }
            stepContent.Content = content;
            stepScroll.Offset = default(Vector);
            previous.IsEnabled = step > 0;
            String nextText = step == regions.Count - 1
                ? RendererText.Done
                : RendererText.Next;
            nextLabel.Text = nextText;
            nextIcon.Icon = step == regions.Count - 1
                ? BrowserIcons.Check
                : BrowserIcons.Forward;
            AutomationProperties.SetName(next, nextText);
        };
        previous.Click += (_, _) =>
        {
            if (step > 0)
            {
                step--;
                showStep();
            }
        };
        next.Click += (_, _) =>
        {
            if (step < regions.Count - 1)
            {
                step++;
                showStep();
                return;
            }
            _status(RendererText.Done);
        };

        showStep();
        return root;
    }

    private Control RenderCompactHeader(JsonElement surface)
    {
        Border header = new Border
        {
            Background = Brush(_appearance.Surface),
            BorderBrush = Brush(_appearance.Border),
            BorderThickness = new Thickness(0D, 0D, 0D, 1D),
            Padding = _settings.Profile.Kind == DeviceProfileKind.Watch
                ? new Thickness(18D, 10D)
                : new Thickness(18D, 13D),
            Child = RenderIdentityContent(
                surface,
                _settings.Profile.Kind == DeviceProfileKind.Watch
                    ? 24D
                    : 30D)
        };
        return header;
    }

    private Control RenderRouteRail(JsonElement surface)
    {
        StackPanel rail = new StackPanel
        {
            Spacing = 8D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        if (!surface.TryGetProperty(LumuiProtocol.Fields.Navigation, out JsonElement navigation)
            || !navigation.TryGetProperty(LumuiProtocol.Fields.Routes, out JsonElement routes)
            || routes.ValueKind != JsonValueKind.Array)
        {
            return rail;
        }
        foreach (JsonElement route in routes.EnumerateArray())
        {
            String href = Text(route, LumuiProtocol.Fields.Href);
            Uri? uri = ResolveUri(href, allowExternal: false);
            Button button = new Button
            {
                Content = Text(
                    route,
                    LumuiProtocol.Fields.Label,
                    RendererText.Page),
                IsEnabled = uri is not null,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            Boolean current = Boolean(route, LumuiProtocol.Fields.Current);
            if (current)
            {
                ApplyPrimaryButton(button);
                button.IsHitTestVisible = false;
                button.Focusable = false;
            }
            else
            {
                ApplyLinkButton(button);
            }
            if (uri is not null && !current)
            {
                button.Click += async (_, _) => await _navigate(uri);
            }
            rail.Children.Add(button);
        }
        return rail;
    }

    private Control RenderCurrentPage(JsonElement surface, Int32 maximumRegions)
    {
        JsonElement? page = CurrentPage(surface);
        if (page is null)
        {
            return new TextBlock
            {
                Text = RendererText.NoPage,
                Margin = new Thickness(24D),
                Foreground = Brush(_appearance.Muted)
            };
        }
        if (maximumRegions == Int32.MaxValue)
        {
            return RenderPage(page.Value);
        }
        StackPanel panel = new StackPanel
        {
            MaxWidth = _settings.Profile.ContentWidth,
            Margin = new Thickness(18D, 10D, 18D, 24D),
            Spacing = 16D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        if (page.Value.TryGetProperty(
                LumuiProtocol.Fields.Regions,
                out JsonElement regions)
            && regions.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement region in regions.EnumerateArray().Take(maximumRegions))
            {
                panel.Children.Add(RenderNode(region));
            }
        }
        return panel;
    }

    private static JsonElement? CurrentPage(JsonElement surface)
    {
        if (!surface.TryGetProperty(LumuiProtocol.Fields.Pages, out JsonElement pages)
            || pages.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        foreach (JsonElement page in pages.EnumerateArray())
        {
            if (Boolean(page, LumuiProtocol.Fields.Current))
            {
                return page;
            }
        }
        foreach (JsonElement page in pages.EnumerateArray())
        {
            return page;
        }
        return null;
    }

    private static JsonElement? FindFirst(
        JsonElement root,
        Func<JsonElement, Boolean> predicate)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        if (predicate(root))
        {
            return root;
        }
        JsonElement? match = FindFirstInCollection(
            root,
            LumuiProtocol.Fields.Regions,
            predicate);
        match ??= FindFirstInCollection(
            root,
            LumuiProtocol.Fields.Items,
            predicate);
        match ??= FindFirstInCollection(
            root,
            LumuiProtocol.Fields.Children,
            predicate);
        match ??= FindFirstInCollection(
            root,
            LumuiProtocol.Fields.Nodes,
            predicate);
        match ??= FindFirstInCollection(
            root,
            LumuiProtocol.Fields.Tabs,
            predicate);
        return match;
    }

    private static JsonElement? FindFirstInCollection(
        JsonElement root,
        String propertyName,
        Func<JsonElement, Boolean> predicate)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement collection)
            || collection.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        foreach (JsonElement child in collection.EnumerateArray())
        {
            JsonElement? match = FindFirst(child, predicate);
            if (match is not null)
            {
                return match;
            }
        }
        return null;
    }

    private static IReadOnlyList<JsonElement> FindAll(
        JsonElement root,
        Func<JsonElement, Boolean> predicate)
    {
        List<JsonElement> matches = new List<JsonElement>();
        CollectMatches(root, predicate, matches);
        return matches;
    }

    private static void CollectMatches(
        JsonElement root,
        Func<JsonElement, Boolean> predicate,
        ICollection<JsonElement> matches)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        if (predicate(root))
        {
            matches.Add(root);
        }
        CollectMatchesFromCollection(
            root,
            LumuiProtocol.Fields.Regions,
            predicate,
            matches);
        CollectMatchesFromCollection(
            root,
            LumuiProtocol.Fields.Items,
            predicate,
            matches);
        CollectMatchesFromCollection(
            root,
            LumuiProtocol.Fields.Children,
            predicate,
            matches);
        CollectMatchesFromCollection(
            root,
            LumuiProtocol.Fields.Nodes,
            predicate,
            matches);
        CollectMatchesFromCollection(
            root,
            LumuiProtocol.Fields.Tabs,
            predicate,
            matches);
    }

    private static void CollectMatchesFromCollection(
        JsonElement root,
        String propertyName,
        Func<JsonElement, Boolean> predicate,
        ICollection<JsonElement> matches)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement collection)
            || collection.ValueKind != JsonValueKind.Array)
        {
            return;
        }
        foreach (JsonElement child in collection.EnumerateArray())
        {
            CollectMatches(child, predicate, matches);
        }
    }

    private Control RenderIdentity(JsonElement surface)
    {
        Border host = new Border
        {
            Padding = new Thickness(24D, 22D, 24D, 14D),
            Child = RenderIdentityContent(surface, 42D)
        };
        return host;
    }

    private Control RenderApplicationHeader(JsonElement surface)
    {
        Grid header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 32D,
            MaxWidth = Math.Min(_settings.Profile.FrameWidth, 1440D),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        header.Children.Add(RenderIdentityContent(surface, 42D));
        Control navigation = RenderNavigation(surface);
        navigation.HorizontalAlignment = HorizontalAlignment.Right;
        navigation.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(navigation);
        Grid.SetColumn(navigation, 1);
        return new Border
        {
            Background = Brush(_appearance.Surface),
            BorderBrush = Brush(_appearance.Accent),
            BorderThickness = new Thickness(0D, 0D, 0D, 4D),
            Padding = new Thickness(40D, 18D),
            Child = header
        };
    }

    private Control RenderIdentityContent(
        JsonElement surface,
        Double logoSize,
        Boolean includePageTitle = false)
    {
        String name = DocumentTitle;
        String shortName = String.Empty;
        JsonElement identity = default;
        Boolean hasIdentity = surface.TryGetProperty(
            LumuiProtocol.Fields.Identity,
            out identity);
        if (hasIdentity)
        {
            name = Text(identity, LumuiProtocol.Fields.Name, name);
            shortName = Text(identity, LumuiProtocol.Fields.ShortName);
            if (_settings.Profile.Kind == DeviceProfileKind.Watch
                && shortName.Length > 0)
            {
                name = shortName;
            }
        }

        Grid heading = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 12D,
            VerticalAlignment = VerticalAlignment.Center
        };
        JsonElement logo = default;
        Boolean hasLogo = hasIdentity
            && ((identity.TryGetProperty(
                    LumuiProtocol.Fields.Logo,
                    out logo)
                && logo.ValueKind == JsonValueKind.Object)
                || (identity.TryGetProperty(
                    LumuiProtocol.Fields.Icon,
                    out logo)
                && logo.ValueKind == JsonValueKind.Object));
        Boolean logoShown = false;
        if (hasLogo)
        {
            Uri? source = ResolveUri(
                Text(logo, LumuiProtocol.Fields.Source),
                allowExternal: false);
            if (source is not null)
            {
                ContentControl mark = new ContentControl
                {
                    Width = logoSize,
                    Height = logoSize,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch
                };
                heading.Children.Add(mark);
                logoShown = true;
                _ = _assetLoader.LoadAsync(
                    mark,
                    source,
                    Text(logo, LumuiProtocol.Fields.Type));
            }
        }
        StackPanel names = new StackPanel
        {
            Spacing = 1D,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (!hasLogo && shortName.Length > 0)
        {
            names.Children.Add(new TextBlock
            {
                Text = shortName.ToUpperInvariant(),
                Foreground = Brush(_brand.Accent),
                FontSize = Font(11D),
                FontWeight = FontWeight.Bold,
                LetterSpacing = 1.2D
            });
        }
        names.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = Font(includePageTitle ? 18D : 24D),
            FontWeight = FontWeight.Light,
            Foreground = Brush(_appearance.Text),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });
        if (includePageTitle)
        {
            JsonElement? page = CurrentPage(surface);
            String pageTitle = page is null
                ? String.Empty
                : Text(page.Value, LumuiProtocol.Fields.Title);
            if (pageTitle.Length > 0
                && !String.Equals(
                    pageTitle,
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                names.Children.Add(new TextBlock
                {
                    Text = pageTitle,
                    FontSize = Font(12D),
                    Foreground = Brush(_appearance.Muted),
                    TextWrapping = TextWrapping.Wrap
                });
            }
        }
        heading.Children.Add(names);
        Grid.SetColumn(names, logoShown ? 1 : 0);
        return heading;
    }

    private Control RenderNavigation(JsonElement surface)
    {
        WrapPanel bar = new WrapPanel
        {
            MaxWidth = _settings.Profile.ContentWidth,
            Margin = new Thickness(0D),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        if (!surface.TryGetProperty(LumuiProtocol.Fields.Navigation, out JsonElement navigation)
            || !navigation.TryGetProperty(LumuiProtocol.Fields.Routes, out JsonElement routes)
            || routes.ValueKind != JsonValueKind.Array)
        {
            return bar;
        }
        foreach (JsonElement route in routes.EnumerateArray())
        {
            String href = Text(route, LumuiProtocol.Fields.Href);
            if (href.Length == 0)
            {
                continue;
            }
            Button button = new Button
            {
                Content = Text(
                    route,
                    LumuiProtocol.Fields.Label,
                    RendererText.Page),
                Margin = new Thickness(0D)
            };
            Boolean current = Boolean(route, LumuiProtocol.Fields.Current);
            _styler.ApplyNavigationButton(button, current);
            if (current)
            {
                button.IsHitTestVisible = false;
                button.Focusable = false;
            }
            Uri? uri = ResolveUri(href, allowExternal: false);
            button.IsEnabled = uri is not null;
            if (uri is not null && !current)
            {
                button.Click += async (_, _) => await _navigate(uri);
            }
            bar.Children.Add(button);
        }
        return bar;
    }

    private Control RenderPage(
        JsonElement page,
        Boolean deferRegions = false)
    {
        StackPanel panel = new StackPanel
        {
            Margin = new Thickness(0D),
            Spacing = 0D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        if (page.TryGetProperty(LumuiProtocol.Fields.Regions, out JsonElement regions)
            && regions.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement region in regions.EnumerateArray())
            {
                panel.Children.Add(
                    deferRegions
                        ? DeferredRegion(region)
                        : RenderNode(region));
            }
        }
        return panel;
    }

    private Control RenderNavigationGroups(JsonElement surface)
    {
        StackPanel footer = new StackPanel
        {
            MaxWidth = Math.Min(_settings.Profile.ContentWidth, 1440D),
            Margin = new Thickness(48D, 0D),
            Spacing = 28D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Boolean wide = _settings.Profile.Kind is DeviceProfileKind.Desktop
            or DeviceProfileKind.Web
            or DeviceProfileKind.Kiosk;
        Grid? columns = null;
        Int32 groupIndex = 0;
        if (wide)
        {
            columns = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions(
                    "1.2*,0.8*,0.8*,0.8*,0.8*"),
                ColumnSpacing = 44D,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            columns.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            columns.Children.Add(RenderFooterIdentity(surface));
            footer.Children.Add(columns);
        }
        if (surface.TryGetProperty(LumuiProtocol.Fields.Navigation, out JsonElement navigation)
            && navigation.TryGetProperty(LumuiProtocol.Fields.Groups, out JsonElement groups)
            && groups.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement group in groups.EnumerateArray())
            {
                StackPanel groupPanel = new StackPanel { Spacing = 8 };
                groupPanel.Children.Add(new TextBlock
                {
                    Text = Text(
                        group,
                        LumuiProtocol.Fields.Label,
                        RendererText.Links),
                    FontWeight = FontWeight.Bold,
                    FontSize = Font(18D),
                    Foreground = Brush(_appearance.CodeText)
                });
                String description = Text(group, LumuiProtocol.Fields.Description);
                if (description.Length > 0)
                {
                    groupPanel.Children.Add(new TextBlock
                    {
                        Text = description,
                        Foreground = Brush(_appearance.CodeText),
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                if (group.TryGetProperty(LumuiProtocol.Fields.Links, out JsonElement links)
                    && links.ValueKind == JsonValueKind.Array)
                {
                    StackPanel linkPanel = new StackPanel
                    {
                        Spacing = 2D,
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    foreach (JsonElement link in links.EnumerateArray())
                    {
                        String href = Text(link, LumuiProtocol.Fields.Href);
                        if (href.Length == 0)
                        {
                            continue;
                        }
                        Button button = new Button
                        {
                            Content = new TextBlock
                            {
                                Text = Text(
                                    link,
                                    LumuiProtocol.Fields.Label,
                                    RendererText.Open),
                                Foreground = Brush(_appearance.CodeText),
                                TextWrapping = TextWrapping.Wrap
                            },
                            Background = Brushes.Transparent,
                            Foreground = Brush(_appearance.CodeText),
                            BorderThickness = new Thickness(0D),
                            Padding = new Thickness(0D, 4D),
                            MinHeight = 28D,
                            FontWeight = FontWeight.Normal,
                            HorizontalAlignment = HorizontalAlignment.Left,
                            HorizontalContentAlignment = HorizontalAlignment.Left
                        };
                        Boolean external = Boolean(link, LumuiProtocol.Fields.External);
                        Uri? uri = ResolveUri(href, external);
                        button.IsEnabled = uri is not null;
                        if (uri is not null)
                        {
                            button.Click += async (_, _) =>
                            {
                                if (external)
                                {
                                    await _openExternal(uri);
                                }
                                else
                                {
                                    await _navigate(uri);
                                }
                            };
                        }
                        linkPanel.Children.Add(button);
                    }
                    groupPanel.Children.Add(linkPanel);
                }
                if (columns is null)
                {
                    footer.Children.Add(groupPanel);
                }
                else
                {
                    Int32 row = groupIndex / 4;
                    if (row >= columns.RowDefinitions.Count)
                    {
                        columns.RowDefinitions.Add(
                            new RowDefinition(GridLength.Auto));
                    }
                    columns.Children.Add(groupPanel);
                    Grid.SetColumn(groupPanel, (groupIndex % 4) + 1);
                    Grid.SetRow(groupPanel, row);
                    groupIndex++;
                }
            }
        }

        String copyright = String.Empty;
        if (surface.TryGetProperty(LumuiProtocol.Fields.Identity, out JsonElement identity))
        {
            String holder = Text(
                identity,
                LumuiProtocol.Fields.CopyrightHolder,
                Text(identity, LumuiProtocol.Fields.Name, DocumentTitle));
            if (identity.TryGetProperty(LumuiProtocol.Fields.CopyrightStartYear, out JsonElement year)
                && year.TryGetInt32(out Int32 startYear))
            {
                Int32 currentYear = DateTime.Now.Year;
                copyright = "© " + (startYear < currentYear ? $"{startYear}-{currentYear}" : startYear.ToString(CultureInfo.InvariantCulture))
                    + " " + holder;
            }
        }
        if (copyright.Length > 0)
        {
            footer.Children.Add(new Border
            {
                BorderBrush = Brush("#4DFFFFFF"),
                BorderThickness = new Thickness(0D, 1D, 0D, 0D),
                Padding = new Thickness(0D, 20D, 0D, 0D),
                Child = new TextBlock
                {
                    Text = copyright,
                    Foreground = Brush(_appearance.CodeText)
                }
            });
        }

        return new Border
        {
            Background = Brush(_appearance.CodeBackground),
            Padding = new Thickness(0D, 60D, 0D, 32D),
            Child = footer
        };
    }

    private Control RenderFooterIdentity(JsonElement surface)
    {
        Grid identityLayout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 12D,
            VerticalAlignment = VerticalAlignment.Top
        };
        String name = DocumentTitle;
        if (!surface.TryGetProperty(
                LumuiProtocol.Fields.Identity,
                out JsonElement identity))
        {
            identityLayout.Children.Add(new TextBlock
            {
                Text = name,
                Foreground = Brush(_appearance.CodeText),
                FontSize = Font(18D),
                FontWeight = FontWeight.Bold
            });
            return identityLayout;
        }

        name = Text(identity, LumuiProtocol.Fields.Name, name);
        JsonElement logo = default;
        Boolean hasLogo = (identity.TryGetProperty(
                LumuiProtocol.Fields.Logo,
                out logo)
            && logo.ValueKind == JsonValueKind.Object)
            || (identity.TryGetProperty(
                LumuiProtocol.Fields.Icon,
                out logo)
            && logo.ValueKind == JsonValueKind.Object);
        Int32 textColumn = 0;
        if (hasLogo)
        {
            Uri? source = ResolveUri(
                Text(logo, LumuiProtocol.Fields.Source),
                allowExternal: false);
            if (source is not null)
            {
                ContentControl mark = new ContentControl
                {
                    Width = 52D,
                    Height = 52D,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch
                };
                identityLayout.Children.Add(mark);
                textColumn = 1;
                _ = _assetLoader.LoadAsync(
                    mark,
                    source,
                    Text(logo, LumuiProtocol.Fields.Type));
            }
        }
        TextBlock nameText = new TextBlock
        {
            Text = name,
            Foreground = Brush(_appearance.CodeText),
            FontSize = Font(18D),
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };
        identityLayout.Children.Add(nameText);
        Grid.SetColumn(nameText, textColumn);
        return identityLayout;
    }

    private Control RenderNode(JsonElement node)
    {
        if (node.TryGetProperty(LumuiProtocol.Fields.Visible, out JsonElement visible)
            && visible.ValueKind == JsonValueKind.False)
        {
            return new Border { IsVisible = false };
        }
        String kind = Text(node, LumuiProtocol.Fields.Kind);
        return kind switch
        {
            LumuiProtocol.ComponentKinds.Section => RenderSection(node),
            LumuiProtocol.ComponentKinds.Form => RenderForm(node),
            LumuiProtocol.ComponentKinds.Page => RenderCollection(node),
            LumuiProtocol.ComponentKinds.List => RenderList(node),
            LumuiProtocol.ComponentKinds.OptionBar => RenderOptionBar(node),
            LumuiProtocol.ComponentKinds.Grid => RenderGrid(node),
            LumuiProtocol.ComponentKinds.Tree => RenderTree(node),
            LumuiProtocol.ComponentKinds.Tabs => RenderTabs(node),
            LumuiProtocol.ComponentKinds.Toolbar => RenderToolbar(node),
            LumuiProtocol.ComponentKinds.Menu => RenderMenu(node),
            LumuiProtocol.ComponentKinds.Breadcrumb => RenderBreadcrumb(node),
            LumuiProtocol.ComponentKinds.Calendar => RenderCalendar(node),
            LumuiProtocol.ComponentKinds.Table => RenderTable(node),
            LumuiProtocol.ComponentKinds.Text => RenderText(node),
            LumuiProtocol.ComponentKinds.ValueDisplay => RenderValue(node),
            LumuiProtocol.ComponentKinds.RichText => RenderRichText(node),
            LumuiProtocol.ComponentKinds.CodeBlock => RenderCode(node),
            LumuiProtocol.ComponentKinds.Quote => RenderQuote(node),
            LumuiProtocol.ComponentKinds.Figure => RenderFigure(node),
            LumuiProtocol.ComponentKinds.ImageCollection => RenderImageCollection(node),
            LumuiProtocol.ComponentKinds.Icon => RenderIcon(node),
            LumuiProtocol.ComponentKinds.Badge => RenderBadge(node),
            LumuiProtocol.ComponentKinds.Chart => RenderChart(node),
            LumuiProtocol.ComponentKinds.DetailOption => RenderDetail(node),
            LumuiProtocol.ComponentKinds.Status or
            LumuiProtocol.ComponentKinds.Alert or
            LumuiProtocol.ComponentKinds.Toast or
            LumuiProtocol.ComponentKinds.Error or
            LumuiProtocol.ComponentKinds.EmptyState or
            LumuiProtocol.ComponentKinds.Dialog or
            LumuiProtocol.ComponentKinds.Notification => RenderMessage(node),
            LumuiProtocol.ComponentKinds.Activity => RenderActivity(node),
            LumuiProtocol.ComponentKinds.Link => RenderLink(node),
            LumuiProtocol.ComponentKinds.Button => RenderButton(node),
            LumuiProtocol.ComponentKinds.TextField or
            LumuiProtocol.ComponentKinds.SearchField or
            LumuiProtocol.ComponentKinds.NumberField or
            LumuiProtocol.ComponentKinds.DateField or
            LumuiProtocol.ComponentKinds.TimeField or
            LumuiProtocol.ComponentKinds.DateTimeField or
            LumuiProtocol.ComponentKinds.ColorField or
            LumuiProtocol.ComponentKinds.OtpField or
            LumuiProtocol.ComponentKinds.PasswordField => RenderTextInput(node),
            LumuiProtocol.ComponentKinds.TextArea => RenderTextArea(node),
            LumuiProtocol.ComponentKinds.Toggle or
            LumuiProtocol.ComponentKinds.CheckBox or
            LumuiProtocol.ComponentKinds.CheckOption => RenderCheck(node),
            LumuiProtocol.ComponentKinds.Choice or
            LumuiProtocol.ComponentKinds.ComboBox or
            LumuiProtocol.ComponentKinds.RadioGroup => RenderChoice(node),
            LumuiProtocol.ComponentKinds.MultiSelect => RenderMultiSelect(node),
            LumuiProtocol.ComponentKinds.Slider => RenderSlider(node),
            LumuiProtocol.ComponentKinds.Rating => RenderRating(node),
            LumuiProtocol.ComponentKinds.Stepper => RenderStepper(node),
            LumuiProtocol.ComponentKinds.DateRangeField => RenderDateRange(node),
            LumuiProtocol.ComponentKinds.Progress => RenderProgress(node),
            LumuiProtocol.ComponentKinds.Meter => RenderMeter(node),
            LumuiProtocol.ComponentKinds.Image => RenderImage(node),
            LumuiProtocol.ComponentKinds.ImageOption => RenderImageOption(node),
            LumuiProtocol.ComponentKinds.Audio or
            LumuiProtocol.ComponentKinds.AudioPlayer or
            LumuiProtocol.ComponentKinds.Video or
            LumuiProtocol.ComponentKinds.VideoPlayer => RenderMedia(node),
            LumuiProtocol.ComponentKinds.MediaPicker => RenderFilePicker(node, true),
            LumuiProtocol.ComponentKinds.Map => RenderMap(node),
            LumuiProtocol.ComponentKinds.Navigation => RenderNavigationComponent(node),
            LumuiProtocol.ComponentKinds.Dialer => RenderDialer(node),
            LumuiProtocol.ComponentKinds.LocationPicker => RenderLocationPicker(node),
            LumuiProtocol.ComponentKinds.ContactPicker => RenderContactPicker(node),
            LumuiProtocol.ComponentKinds.FilePicker => RenderFilePicker(node, false),
            LumuiProtocol.ComponentKinds.Preview => RenderPreview(node),
            LumuiProtocol.ComponentKinds.Clock => RenderClock(node),
            LumuiProtocol.ComponentKinds.Graphic => RenderGraphic(node),
            _ => RenderFallback(node)
        };
    }

    private async Task<Control> RenderSectionAsync(
        JsonElement node,
        CancellationToken cancellationToken)
    {
        String role = Text(
            node,
            LumuiProtocol.Fields.Role,
            LumuiProtocol.RegionRoles.Supporting);
        String priority = Text(
            node,
            LumuiProtocol.Fields.Priority,
            LumuiProtocol.Priorities.Normal);
        if (!_embeddedPresentation
            && role == LumuiProtocol.RegionRoles.Introduction
            && (_settings.Profile.Kind == DeviceProfileKind.Web
                || _settings.Profile.Kind == DeviceProfileKind.Desktop))
        {
            return RenderDesktopIntroduction(node);
        }
        Boolean compact = _settings.Profile.Kind is DeviceProfileKind.Phone
            or DeviceProfileKind.Watch;
        StackPanel stack = new StackPanel
        {
            MaxWidth = _embeddedPresentation
                ? Double.PositiveInfinity
                : Math.Min(_settings.Profile.ContentWidth, 1180D),
            Spacing = compact ? 10D : 16D,
            Margin = !_embeddedPresentation
                && role == LumuiProtocol.RegionRoles.Introduction
                ? IntroductionMargin()
                : new Thickness(0D),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        String label = Text(node, LumuiProtocol.Fields.Label);
        if (label.Length > 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = label.ToUpperInvariant(),
                Foreground = Brush(
                    role == LumuiProtocol.RegionRoles.Introduction
                        ? _brand.Accent
                        : _appearance.Accent),
                FontWeight = FontWeight.Bold,
                FontSize = Font(compact ? 11D : 13D),
                LetterSpacing = 1
            });
        }
        String description = Text(node, LumuiProtocol.Fields.Description);
        if (description.Length > 0)
        {
            TextBlock summary = Body(description);
            summary.FontSize = Font(compact ? 14D : 16D);
            summary.LineHeight = Font(compact ? 21D : 25D);
            summary.MaxWidth = 820D;
            stack.Children.Add(summary);
        }
        if (node.TryGetProperty(LumuiProtocol.Fields.Items, out JsonElement items)
            && items.ValueKind == JsonValueKind.Array)
        {
            Int32 columns = TileColumns();
            Int32 tilePosition = 0;
            AdaptiveGridPanel? tiles = null;
            Stopwatch budget = Stopwatch.StartNew();
            foreach (JsonElement item in items.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                String kind = Text(item, LumuiProtocol.Fields.Kind);
                Boolean tileKind =
                    kind == LumuiProtocol.ComponentKinds.DetailOption
                    || kind == LumuiProtocol.ComponentKinds.Link
                    || kind == LumuiProtocol.ComponentKinds.ValueDisplay;
                Boolean tile = columns > 1 && tileKind;
                if (!tile)
                {
                    stack.Children.Add(RenderSectionItem(item));
                }
                else
                {
                    if (tiles is null)
                    {
                        tiles = CreateTileGrid(columns);
                        stack.Children.Add(tiles);
                    }
                    Control child = RenderNode(item);
                    tilePosition++;
                    _styler.ApplyTileAccent(child, tilePosition);
                    tiles.Children.Add(child);
                }
                if (budget.ElapsedMilliseconds >= 2)
                {
                    await YieldRenderingAsync(cancellationToken);
                    budget.Restart();
                }
            }
        }
        if (_embeddedPresentation)
        {
            return new Border
            {
                Child = stack,
                Padding = new Thickness(14D),
                ClipToBounds = true,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
        }
        if (role == LumuiProtocol.RegionRoles.Introduction)
        {
            return _styler.Hero(stack, _settings.Profile.Kind);
        }
        if (role is "summary" or "example" or "examples")
        {
            Border grouped = new Border
            {
                Child = stack,
                MaxWidth = _settings.Profile.ContentWidth,
                Margin = _embeddedPresentation
                    ? new Thickness(0D)
                    : SectionMargin(),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            _styler.ApplyShowcaseCard(grouped);
            return grouped;
        }
        if (_settings.Profile.Kind is DeviceProfileKind.Web
            or DeviceProfileKind.Desktop)
        {
            Border section = new Border
            {
                Child = stack,
                Padding = _embeddedPresentation
                    ? new Thickness(20D)
                    : new Thickness(48D, 40D, 48D, 44D),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            _styler.ApplySectionSurface(section, role);
            return section;
        }
        Border border = new Border
        {
            Child = stack,
            MaxWidth = _settings.Profile.ContentWidth,
            Margin = SectionMargin(),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        border.Classes.Add(BrowserStyleClasses.Card);
        _styler.ApplyCard(border, role, priority);
        return border;
    }

    private Control RenderSection(JsonElement node)
    {
        String role = Text(
            node,
            LumuiProtocol.Fields.Role,
            LumuiProtocol.RegionRoles.Supporting);
        String priority = Text(
            node,
            LumuiProtocol.Fields.Priority,
            LumuiProtocol.Priorities.Normal);
        if (!_embeddedPresentation
            && role == LumuiProtocol.RegionRoles.Introduction
            && (_settings.Profile.Kind == DeviceProfileKind.Web
                || _settings.Profile.Kind == DeviceProfileKind.Desktop))
        {
            return RenderDesktopIntroduction(node);
        }
        Boolean compact = _settings.Profile.Kind is DeviceProfileKind.Phone
            or DeviceProfileKind.Watch;
        StackPanel stack = new StackPanel
        {
            MaxWidth = _embeddedPresentation
                ? Double.PositiveInfinity
                : Math.Min(_settings.Profile.ContentWidth, 1180D),
            Spacing = compact ? 10D : 16D,
            Margin = !_embeddedPresentation
                && role == LumuiProtocol.RegionRoles.Introduction
                ? IntroductionMargin()
                : new Thickness(0D),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        String label = Text(node, LumuiProtocol.Fields.Label);
        if (label.Length > 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = label.ToUpperInvariant(),
                Foreground = Brush(
                    role == LumuiProtocol.RegionRoles.Introduction
                        ? _brand.Accent
                        : _appearance.Accent),
                FontWeight = FontWeight.Bold,
                FontSize = Font(compact ? 11D : 13D),
                LetterSpacing = 1
            });
        }
        String description = Text(node, LumuiProtocol.Fields.Description);
        if (description.Length > 0)
        {
            TextBlock summary = Body(description);
            summary.FontSize = Font(compact ? 14D : 16D);
            summary.LineHeight = Font(compact ? 21D : 25D);
            summary.MaxWidth = 820D;
            stack.Children.Add(summary);
        }
        if (node.TryGetProperty(LumuiProtocol.Fields.Items, out JsonElement items)
            && items.ValueKind == JsonValueKind.Array)
        {
            Int32 columns = TileColumns();
            Int32 tilePosition = 0;
            AdaptiveGridPanel? tiles = null;
            foreach (JsonElement item in items.EnumerateArray())
            {
                String kind = Text(item, LumuiProtocol.Fields.Kind);
                Boolean tileKind =
                    kind == LumuiProtocol.ComponentKinds.DetailOption
                    || kind == LumuiProtocol.ComponentKinds.Link
                    || kind == LumuiProtocol.ComponentKinds.ValueDisplay;
                Boolean tile = columns > 1 && tileKind;
                if (!tile)
                {
                    stack.Children.Add(RenderSectionItem(item));
                    continue;
                }
                if (tiles is null)
                {
                    tiles = CreateTileGrid(columns);
                    stack.Children.Add(tiles);
                }
                Control child = RenderNode(item);
                tilePosition++;
                _styler.ApplyTileAccent(child, tilePosition);
                tiles.Children.Add(child);
            }
        }
        if (_embeddedPresentation)
        {
            return new Border
            {
                Child = stack,
                Padding = new Thickness(14D),
                ClipToBounds = true,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
        }
        if (role == LumuiProtocol.RegionRoles.Introduction)
        {
            return _styler.Hero(stack, _settings.Profile.Kind);
        }
        if (role is "summary" or "example" or "examples")
        {
            Border grouped = new Border
            {
                Child = stack,
                MaxWidth = _settings.Profile.ContentWidth,
                Margin = _embeddedPresentation
                    ? new Thickness(0D)
                    : SectionMargin(),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            _styler.ApplyShowcaseCard(grouped);
            return grouped;
        }
        if (_settings.Profile.Kind is DeviceProfileKind.Web
            or DeviceProfileKind.Desktop)
        {
            Border section = new Border
            {
                Child = stack,
                Padding = _embeddedPresentation
                    ? new Thickness(20D)
                    : new Thickness(48D, 40D, 48D, 44D),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            _styler.ApplySectionSurface(section, role);
            return section;
        }
        Border border = new Border
        {
            Child = stack,
            MaxWidth = _settings.Profile.ContentWidth,
            Margin = SectionMargin(),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        border.Classes.Add(BrowserStyleClasses.Card);
        _styler.ApplyCard(border, role, priority);
        return border;
    }

    private Control RenderDesktopIntroduction(JsonElement node)
    {
        StackPanel narrative = new StackPanel
        {
            MaxWidth = 900D,
            Spacing = 16D,
            VerticalAlignment = VerticalAlignment.Center
        };
        String label = Text(node, LumuiProtocol.Fields.Label);
        if (label.Length > 0)
        {
            narrative.Children.Add(new TextBlock
            {
                Text = label.ToUpperInvariant(),
                Foreground = Brush(_brand.Accent),
                FontWeight = FontWeight.Bold,
                FontSize = Font(13D),
                LetterSpacing = 1D
            });
        }

        List<JsonElement> actionItems = new List<JsonElement>();
        if (node.TryGetProperty(
                LumuiProtocol.Fields.Items,
                out JsonElement items)
            && items.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in items.EnumerateArray())
            {
                String kind = Text(item, LumuiProtocol.Fields.Kind);
                if (kind == LumuiProtocol.ComponentKinds.List
                    && item.TryGetProperty(
                        LumuiProtocol.Fields.Items,
                        out JsonElement listItems)
                    && listItems.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement listItem in listItems.EnumerateArray())
                    {
                        String listKind = Text(
                            listItem,
                            LumuiProtocol.Fields.Kind);
                        if (listKind is LumuiProtocol.ComponentKinds.Link
                            or LumuiProtocol.ComponentKinds.DetailOption)
                        {
                            actionItems.Add(listItem);
                        }
                        else
                        {
                            narrative.Children.Add(
                                RenderDesktopIntroductionItem(listItem));
                        }
                    }
                    continue;
                }
                narrative.Children.Add(RenderDesktopIntroductionItem(item));
            }
        }

        AdaptiveSplitPanel layout = new AdaptiveSplitPanel
        {
            ColumnSpacing = 40D,
            RowSpacing = 32D,
            PrimaryShare = 0.68D,
            Breakpoint = 980D * _settings.TextScale,
            MinimumSecondaryWidth = 360D * _settings.TextScale,
            MaxWidth = Math.Min(_settings.Profile.ContentWidth, 1280D),
            MinHeight = 460D,
            Margin = new Thickness(48D, 40D, 48D, 48D),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        layout.Children.Add(narrative);

        if (actionItems.Count > 0)
        {
            AdaptiveGridPanel actions = CreateTileGrid(2, 180D);
            actions.VerticalAlignment = VerticalAlignment.Center;
            for (Int32 index = 0; index < actionItems.Count; index++)
            {
                Control action = RenderNode(actionItems[index]);
                action.MinHeight = 180D;
                action.HorizontalAlignment = HorizontalAlignment.Stretch;
                action.VerticalAlignment = VerticalAlignment.Stretch;
                _styler.ApplyTileAccent(action, index + 1);
                actions.Children.Add(action);
            }
            layout.Children.Add(actions);
        }
        return _styler.Hero(layout, _settings.Profile.Kind);
    }

    private Control RenderDesktopIntroductionItem(JsonElement item)
    {
        Control rendered = RenderNode(item);
        if (rendered is not TextBlock text
            || Text(item, LumuiProtocol.Fields.Kind)
                != LumuiProtocol.ComponentKinds.Text)
        {
            return rendered;
        }
        String role = Text(
            item,
            LumuiProtocol.Fields.TextRole,
            LumuiProtocol.TextRoles.Body);
        if (role == LumuiProtocol.TextRoles.Heading)
        {
            text.FontSize = Font(64D);
            text.LineHeight = Font(70D);
            text.FontWeight = FontWeight.Light;
            text.MaxWidth = 900D;
        }
        else if (role == LumuiProtocol.TextRoles.Lead)
        {
            text.FontSize = Font(22D);
            text.LineHeight = Font(34D);
            text.MaxWidth = 860D;
        }
        return rendered;
    }

    private Thickness IntroductionMargin()
    {
        return _settings.Profile.Kind switch
        {
            DeviceProfileKind.Watch =>
                new Thickness(18D, 12D, 18D, 22D),
            DeviceProfileKind.Phone =>
                new Thickness(24D, 28D, 24D, 34D),
            DeviceProfileKind.Tablet =>
                new Thickness(42D, 46D, 42D, 52D),
            _ => new Thickness(54D, 56D, 54D, 62D)
        };
    }

    private Thickness SectionMargin()
    {
        return _settings.Profile.Kind switch
        {
            DeviceProfileKind.Watch =>
                new Thickness(12D, 7D),
            DeviceProfileKind.Phone =>
                new Thickness(18D, 8D),
            _ => new Thickness(24D, 10D)
        };
    }

    private AdaptiveGridPanel CreateTileGrid(
        Int32 columns,
        Double minimumItemWidth = 310D)
    {
        Double scaledItemWidth = minimumItemWidth * _settings.TextScale;
        Double twoColumnWidth = (scaledItemWidth * 2D) + 16D;
        Double threeColumnWidth = (scaledItemWidth * 3D) + 32D;
        return new AdaptiveGridPanel
        {
            MinimumItemWidth = Math.Max(
                180D,
                scaledItemWidth),
            MaximumColumns = columns,
            ColumnSpacing = 16D,
            RowSpacing = 16D,
            MinimumWidthForTwoColumns = columns >= 3
                ? Math.Max(twoColumnWidth, 681D * _settings.TextScale)
                : twoColumnWidth,
            MinimumWidthForThreeColumns = Math.Max(
                threeColumnWidth,
                961D * _settings.TextScale),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private Int32 TileColumns()
    {
        return _settings.Profile.Kind switch
        {
            DeviceProfileKind.Web or
            DeviceProfileKind.Desktop or
            DeviceProfileKind.Kiosk => 3,
            DeviceProfileKind.Tablet or
            DeviceProfileKind.Appliance => 2,
            _ => 1
        };
    }

    private Control RenderCollection(JsonElement node)
    {
        String kind = Text(node, LumuiProtocol.Fields.Kind);
        Boolean page = kind == LumuiProtocol.ComponentKinds.Page;
        StackPanel panel = new StackPanel
        {
            Spacing = page ? 22D : 12D,
            MaxWidth = _settings.Profile.ContentWidth,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        String label = Text(
            node,
            page ? LumuiProtocol.Fields.Title : LumuiProtocol.Fields.Label,
            Text(
                node,
                page ? LumuiProtocol.Fields.Label : LumuiProtocol.Fields.Title));
        if (label.Length > 0)
        {
            TextBlock heading = new TextBlock
            {
                Text = label,
                FontSize = Font(page ? 34D : 22D),
                LineHeight = Font(page ? 43D : 30D),
                FontWeight = page ? FontWeight.Light : FontWeight.SemiBold,
                Foreground = Brush(_appearance.Text),
                TextWrapping = TextWrapping.Wrap
            };
            AutomationProperties.SetHeadingLevel(heading, page ? 1 : 2);
            panel.Children.Add(heading);
        }
        String description = Text(node, LumuiProtocol.Fields.Description);
        if (description.Length > 0)
        {
            TextBlock summary = Body(description);
            summary.FontSize = Font(page ? 18D : 16D);
            summary.LineHeight = Font(page ? 28D : 24D);
            summary.MaxWidth = 820D;
            panel.Children.Add(summary);
        }
        foreach (String field in new String[]
        {
            LumuiProtocol.Fields.Items,
            LumuiProtocol.Fields.Regions,
            LumuiProtocol.Fields.Children,
            LumuiProtocol.Fields.Nodes,
            LumuiProtocol.Fields.Tabs,
            LumuiProtocol.Fields.Actions,
            LumuiProtocol.Fields.Options
        })
        {
            if (!node.TryGetProperty(field, out JsonElement items) || items.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            JsonElement[] collection = items.EnumerateArray().ToArray();
            Int32 columns = TileColumns();
            Boolean tileCollection = columns > 1
                && collection.Length > 0
                && collection.All((JsonElement item) =>
                {
                    String kind = Text(item, LumuiProtocol.Fields.Kind);
                    return kind is LumuiProtocol.ComponentKinds.DetailOption
                        or LumuiProtocol.ComponentKinds.Link;
                });
            if (tileCollection)
            {
                AdaptiveGridPanel tiles = CreateTileGrid(columns);
                for (Int32 index = 0; index < collection.Length; index++)
                {
                    Control child = RenderNode(collection[index]);
                    tiles.Children.Add(child);
                }
                panel.Children.Add(tiles);
                break;
            }
            foreach (JsonElement item in collection)
            {
                if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty(LumuiProtocol.Fields.Kind, out _))
                {
                    panel.Children.Add(
                        page ? RenderNode(item) : RenderSectionItem(item));
                }
                else if (field == LumuiProtocol.Fields.Actions
                    && item.ValueKind == JsonValueKind.String)
                {
                    panel.Children.Add(RenderActionReference(
                        node,
                        item.GetString() ?? String.Empty));
                }
                else
                {
                    panel.Children.Add(Body(
                        item.ValueKind == JsonValueKind.String
                            ? item.GetString() ?? String.Empty
                            : item.ValueKind == JsonValueKind.Object
                                ? Text(
                                    item,
                                    LumuiProtocol.Fields.Label,
                                    Text(
                                        item,
                                        LumuiProtocol.Fields.Title,
                                        RendererText.Item))
                                : Display(item)));
                }
            }
            break;
        }
        AutomationProperties.SetName(
            panel,
            label.Length > 0 ? label : page ? "Page" : "Collection");
        return panel;
    }

    private Control RenderForm(JsonElement node)
    {
        StackPanel content = new StackPanel
        {
            Spacing = 18D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        String label = Text(node, LumuiProtocol.Fields.Label);
        if (label.Length > 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = Font(24D),
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush(_appearance.Text),
                TextWrapping = TextWrapping.Wrap
            });
        }
        String description = Text(node, LumuiProtocol.Fields.Description);
        if (description.Length > 0)
        {
            content.Children.Add(Body(description));
        }
        if (node.TryGetProperty(LumuiProtocol.Fields.Items, out JsonElement items)
            && items.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in items.EnumerateArray())
            {
                Control rendered = RenderSectionItem(item);
                if (rendered is Button button)
                {
                    button.HorizontalAlignment = HorizontalAlignment.Left;
                    button.Margin = new Thickness(0D, 4D, 0D, 0D);
                }
                content.Children.Add(rendered);
            }
        }
        Border form = new Border
        {
            Child = content,
            MaxWidth = 760D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        form.Classes.Add(BrowserStyleClasses.Card);
        _styler.ApplyShowcaseCard(form);
        form.Padding = new Thickness(
            _settings.Profile.Kind == DeviceProfileKind.Watch ? 14D : 26D);
        AutomationProperties.SetName(form, label.Length > 0 ? label : "Form");
        return form;
    }

private Control RenderList(JsonElement node)
    {
        if (!node.TryGetProperty(LumuiProtocol.Fields.Items, out JsonElement items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return RenderCollection(node);
        }
        JsonElement[] values = items.EnumerateArray().ToArray();
        Boolean examples = values.Length > 0
            && values.All(item =>
                Text(item, LumuiProtocol.Fields.Kind)
                    == LumuiProtocol.ComponentKinds.Section
                && Text(item, LumuiProtocol.Fields.Role) == "example");
        if (examples)
        {
            Int32 exampleColumns =
                _settings.Profile.Kind is DeviceProfileKind.Web
                    or DeviceProfileKind.Desktop
                    or DeviceProfileKind.Kiosk
                    ? 2
                    : _settings.Profile.Kind == DeviceProfileKind.Tablet ? 2 : 1;
            return RenderDeferredGrid(
                values,
                exampleColumns,
                560D,
                RenderExampleCard,
                ComponentPreviewMetrics.TileHeight,
                false);
        }

        String label = Text(node, LumuiProtocol.Fields.Label);
        String description = Text(node, LumuiProtocol.Fields.Description);
        Boolean tiles = values.Length > 0
            && values.All(value =>
                Text(value, LumuiProtocol.Fields.Kind) is
                    LumuiProtocol.ComponentKinds.DetailOption
                    or LumuiProtocol.ComponentKinds.Link
                    or LumuiProtocol.ComponentKinds.ValueDisplay);
        StackPanel list = new StackPanel
        {
            Spacing = 12D,
            MaxWidth = _settings.Profile.ContentWidth,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        if (label.Length > 0)
        {
            list.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = Font(21D),
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush(_appearance.Text),
                TextWrapping = TextWrapping.Wrap
            });
        }
        if (description.Length > 0)
        {
            list.Children.Add(Body(description));
        }

        if (tiles)
        {
            AdaptiveGridPanel grid = CreateTileGrid(
                Math.Min(TileColumns(), Math.Max(1, values.Length)),
                280D);
            for (Int32 index = 0; index < values.Length; index++)
            {
                Control item = RenderSectionItem(values[index]);
                item.HorizontalAlignment = HorizontalAlignment.Stretch;
                item.VerticalAlignment = VerticalAlignment.Stretch;
                _styler.ApplyTileAccent(item, index + 1);
                grid.Children.Add(item);
            }
            list.Children.Add(grid);
        }
        else
        {
            foreach (JsonElement value in values)
            {
                Control item = RenderSectionItem(value);
                item.HorizontalAlignment = HorizontalAlignment.Stretch;
                list.Children.Add(item);
            }
        }
        AutomationProperties.SetName(
            list,
            label.Length > 0 ? label : "List");
        return list;
    }

    private Control RenderDeferredGrid(
        IReadOnlyList<JsonElement> values,
        Int32 maximumColumns,
        Double minimumItemWidth,
        Func<JsonElement, Control> render,
        Double estimatedItemHeight,
        Boolean accents)
    {
        Int32 safeMaximum = Math.Max(1, maximumColumns);
        if (values.Count <= safeMaximum)
        {
            return CreateDeferredGridRange(
                values,
                0,
                values.Count,
                safeMaximum,
                minimumItemWidth,
                render,
                estimatedItemHeight,
                accents,
                true);
        }
        StackPanel rows = new StackPanel
        {
            Spacing = 16D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        for (Int32 start = 0; start < values.Count; start += safeMaximum)
        {
            Int32 rowStart = start;
            Int32 count = Math.Min(safeMaximum, values.Count - start);
            if (start == 0)
            {
                rows.Children.Add(CreateDeferredGridRange(
                    values,
                    rowStart,
                    count,
                    safeMaximum,
                    minimumItemWidth,
                    render,
                    estimatedItemHeight,
                    accents,
                    true));
                continue;
            }
            rows.Children.Add(DeferredControl(
                () => CreateDeferredGridRange(
                    values,
                    rowStart,
                    count,
                    safeMaximum,
                    minimumItemWidth,
                    render,
                    estimatedItemHeight,
                    accents,
                    false),
                estimatedItemHeight));
        }
        return rows;
    }

    private AdaptiveGridPanel CreateDeferredGridRange(
        IReadOnlyList<JsonElement> values,
        Int32 start,
        Int32 count,
        Int32 maximumColumns,
        Double minimumItemWidth,
        Func<JsonElement, Control> render,
        Double estimatedItemHeight,
        Boolean accents,
        Boolean eager)
    {
        AdaptiveGridPanel grid = CreateTileGrid(
            maximumColumns,
            minimumItemWidth);
        grid.PreserveColumnWidth = maximumColumns > 1;
        for (Int32 offset = 0; offset < count; offset++)
        {
            Int32 itemIndex = start + offset;
            Control CreateItem()
            {
                Control item = render(values[itemIndex]);
                if (accents)
                {
                    _styler.ApplyTileAccent(item, itemIndex + 1);
                }
                return item;
            }
            grid.Children.Add(eager
                ? CreateItem()
                : DeferredControl(CreateItem, estimatedItemHeight));
        }
        return grid;
    }

    private Control RenderExampleCard(JsonElement node)
    {
        Grid content = new Grid
        {
            RowDefinitions = new RowDefinitions("84,*"),
            RowSpacing = 12D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        StackPanel summary = new StackPanel
        {
            Spacing = 6D,
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        StackPanel details = new StackPanel
        {
            Spacing = 8D,
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        content.Children.Add(summary);
        content.Children.Add(details);
        Grid.SetRow(details, 1);
        Double previewHeight = 0D;
        JsonElement reference = default;
        if (node.TryGetProperty(LumuiProtocol.Fields.Items, out JsonElement items)
            && items.ValueKind == JsonValueKind.Array)
        {
            JsonElement[] values = items.EnumerateArray().ToArray();
            Boolean hasPreview = values.Any(item => Text(
                item,
                LumuiProtocol.Fields.Kind) == LumuiProtocol.ComponentKinds.Preview);
            reference = values.FirstOrDefault(item => Text(
                item,
                LumuiProtocol.Fields.Kind) == LumuiProtocol.ComponentKinds.Link);
            foreach (JsonElement item in values)
            {
                String kind = Text(item, LumuiProtocol.Fields.Kind);
                if (kind == LumuiProtocol.ComponentKinds.Text)
                {
                    String role = Text(item, LumuiProtocol.Fields.TextRole);
                    TextBlock copy = role == LumuiProtocol.TextRoles.Heading
                        ? new TextBlock
                        {
                            Text = Text(item, LumuiProtocol.Fields.Text),
                            FontSize = Font(22D),
                            FontWeight = FontWeight.SemiBold,
                            Foreground = Brush(_appearance.Text),
                            TextWrapping = TextWrapping.Wrap
                        }
                        : Body(Text(item, LumuiProtocol.Fields.Text));
                    copy.MaxHeight = role == LumuiProtocol.TextRoles.Heading
                        ? 32D
                        : 42D;
                    copy.ClipToBounds = true;
                    if (role == LumuiProtocol.TextRoles.Heading)
                    {
                        copy.TextWrapping = TextWrapping.NoWrap;
                        copy.TextTrimming = TextTrimming.CharacterEllipsis;
                    }
                    summary.Children.Add(copy);
                }
                else if (kind == LumuiProtocol.ComponentKinds.Link)
                {
                    if (!hasPreview)
                    {
                        details.Children.Add(RenderCompactLink(item));
                    }
                }
                else
                {
                    if (kind == LumuiProtocol.ComponentKinds.Preview)
                    {
                        previewHeight = ComponentPreviewMetrics.Height();
                        Control previewSurface = RenderPreview(
                            item,
                            true,
                            previewHeight);
                        Grid preview = new Grid
                        {
                            Height = previewHeight,
                            ClipToBounds = true,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            Children = { previewSurface }
                        };
                        details.Children.Add(preview);
                        continue;
                    }
                    Control renderedItem = RenderSectionItem(item);
                    details.Children.Add(renderedItem);
                }
            }
        }
        Double cardHeight = previewHeight > 0D
            ? ComponentPreviewMetrics.TileHeight
            : 220D;
        if (reference.ValueKind == JsonValueKind.Object)
        {
            Boolean external = Boolean(reference, LumuiProtocol.Fields.External);
            Boolean download = Boolean(reference, LumuiProtocol.Fields.Download);
            Uri? uri = ResolveUri(
                Text(reference, LumuiProtocol.Fields.Href),
                external);
            if (uri is not null)
            {
                Button openCard = new Button
                {
                    Content = content,
                    Height = cardHeight,
                    MinHeight = 0D,
                    Padding = new Thickness(18D),
                    Background = Brush(_appearance.Surface),
                    BorderThickness = new Thickness(0D),
                    CornerRadius = new CornerRadius(0D),
                    ClipToBounds = true,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch
                };
                AutomationProperties.SetName(
                    openCard,
                    Text(reference, LumuiProtocol.Fields.Label, "Open component details"));
                openCard.Click += async (_, _) =>
                {
                    if (download)
                    {
                        await _download(uri);
                    }
                    else if (external)
                    {
                        await _openExternal(uri);
                    }
                    else
                    {
                        await _navigate(uri);
                    }
                };
                return openCard;
            }
        }
        Border card = new Border
        {
            Child = content,
            Height = cardHeight,
            MinHeight = 0D,
            Padding = new Thickness(18D),
            Background = Brush(_appearance.Surface),
            BorderThickness = new Thickness(0D),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        return card;
    }

    private Control RenderGrid(JsonElement node)
    {
        if (!node.TryGetProperty(LumuiProtocol.Fields.Items, out JsonElement items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return RenderCollection(node);
        }
        JsonElement[] values = items.EnumerateArray().ToArray();
        Int32 maximumColumns = values.Length == 4
            ? Math.Min(2, TileColumns())
            : Math.Min(TileColumns(), Math.Max(1, values.Length));
        AdaptiveGridPanel grid = CreateTileGrid(
            Math.Max(1, maximumColumns),
            280D);
        for (Int32 index = 0; index < values.Length; index++)
        {
            Control child = RenderSectionItem(values[index]);
            _styler.ApplyTileAccent(child, index + 1);
            grid.Children.Add(child);
        }
        String label = Text(node, LumuiProtocol.Fields.Label);
        String description = Text(node, LumuiProtocol.Fields.Description);
        if (label.Length == 0 && description.Length == 0)
        {
            return grid;
        }
        StackPanel content = new StackPanel { Spacing = 10D };
        if (label.Length > 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = Font(21D),
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush(_appearance.Text),
                TextWrapping = TextWrapping.Wrap
            });
        }
        if (description.Length > 0)
        {
            content.Children.Add(Body(description));
        }
        content.Children.Add(grid);
        return content;
    }

    private Control RenderOptionBar(JsonElement node)
    {
        String id = Text(node, LumuiProtocol.Fields.Id);
        String current = Text(node, LumuiProtocol.Fields.Value);
        String action = Text(node, LumuiProtocol.Fields.Action);
        StackPanel panel = new StackPanel
        {
            Spacing = 10D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        String label = Text(node, LumuiProtocol.Fields.Label);
        if (label.Length > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = Brush(_appearance.Text),
                FontWeight = FontWeight.SemiBold
            });
        }
        String description = Text(node, LumuiProtocol.Fields.Description);
        if (description.Length > 0)
        {
            panel.Children.Add(Body(description));
        }
        StackPanel options = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4D
        };
        List<(ToggleButton Button, String Value)> buttons =
            new List<(ToggleButton Button, String Value)>();
        if (node.TryGetProperty(LumuiProtocol.Fields.Options, out JsonElement values)
            && values.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement option in values.EnumerateArray())
            {
                String value = Text(option, LumuiProtocol.Fields.Value);
                ToggleButton button = new ToggleButton
                {
                    Content = Text(option, LumuiProtocol.Fields.Label, value),
                    IsChecked = value == current || Boolean(option, "selected"),
                    MinHeight = 40D
                };
                if (button.IsChecked == true)
                {
                    current = value;
                }
                ApplySegmentButton(button, button.IsChecked == true);
                button.Click += async (_, _) =>
                {
                    current = value;
                    foreach ((ToggleButton candidate, String candidateValue) in buttons)
                    {
                        Boolean selected = candidateValue == current;
                        candidate.IsChecked = selected;
                        ApplySegmentButton(candidate, selected);
                    }
                    if (action.Length > 0)
                    {
                        await _invoke(
                            id,
                            action,
                            new Dictionary<String, Object?>(StringComparer.Ordinal) { [id] = value });
                    }
                };
                buttons.Add((button, value));
                options.Children.Add(button);
            }
        }
        _inputs[id] = () => current;
        Border frame = new Border
        {
            Child = new ScrollViewer
            {
                Content = options,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
            },
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = 760D
        };
        _styler.ApplySegmentFrame(frame);
        panel.Children.Add(frame);
        AutomationProperties.SetName(
            panel,
            label.Length > 0 ? label : "Options");
        return panel;
    }

    private void ApplySegmentButton(
        ToggleButton button,
        Boolean selected = false)
    {
        button.Background = Brush(
            selected ? _appearance.Accent : _appearance.Surface);
        button.Foreground = Brush(
            selected ? _appearance.AccentText : _appearance.Text);
        button.BorderBrush = Brush(
            selected ? _appearance.Accent : _appearance.Border);
        button.BorderThickness = new Thickness(1D);
        button.CornerRadius = new CornerRadius(_appearance.ControlRadius);
        button.Padding = new Thickness(14D, 8D);
        button.FontFamily = new FontFamily(_appearance.FontFamily);
    }

    private Control RenderTree(JsonElement node)
    {
        StackPanel tree = new StackPanel
        {
            Spacing = 6D,
            MaxWidth = 760D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        HashSet<String> expanded = node.TryGetProperty("expanded_node_ids", out JsonElement expandedIds)
            && expandedIds.ValueKind == JsonValueKind.Array
                ? expandedIds.EnumerateArray().Select(Display).ToHashSet(StringComparer.Ordinal)
                : new HashSet<String>(StringComparer.Ordinal);
        if (node.TryGetProperty(LumuiProtocol.Fields.Nodes, out JsonElement nodes)
            && nodes.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in nodes.EnumerateArray())
            {
                tree.Children.Add(RenderTreeNode(item, expanded));
            }
        }
        AutomationProperties.SetName(
            tree,
            Text(node, LumuiProtocol.Fields.Label, "Tree"));
        return tree;
    }

    private Control RenderTreeNode(
        JsonElement node,
        IReadOnlySet<String> expanded)
    {
        String id = Text(node, LumuiProtocol.Fields.Id);
        String label = Text(node, LumuiProtocol.Fields.Label, "Item");
        String description = Text(
            node,
            LumuiProtocol.Fields.Description,
            Text(node, LumuiProtocol.Fields.Text));
        Boolean selected = Boolean(node, "selected");
        JsonElement[] children = node.TryGetProperty(
                LumuiProtocol.Fields.Items,
                out JsonElement nested)
            && nested.ValueKind == JsonValueKind.Array
                ? nested.EnumerateArray().ToArray()
                : Array.Empty<JsonElement>();
        if (children.Length == 0)
        {
            Border leaf = TreeRow(label, description, selected);
            AutomationProperties.SetAutomationId(leaf, id);
            return leaf;
        }
        StackPanel childList = new StackPanel
        {
            Spacing = 6D,
            Margin = new Thickness(18D, 7D, 0D, 2D)
        };
        foreach (JsonElement child in children)
        {
            childList.Children.Add(RenderTreeNode(child, expanded));
        }
        Border branchContent = new Border
        {
            Child = childList,
            BorderBrush = Brush(_appearance.Border),
            BorderThickness = new Thickness(1D, 0D, 0D, 0D),
            Margin = new Thickness(16D, 0D, 0D, 0D)
        };
        Expander branch = new Expander
        {
            Header = TreeRow(label, description, selected),
            Content = branchContent,
            IsExpanded = expanded.Contains(id),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(branch, label);
        AutomationProperties.SetAutomationId(branch, id);
        return branch;
    }

    private Border TreeRow(
        String label,
        String description,
        Boolean selected)
    {
        StackPanel content = new StackPanel { Spacing = 2D };
        content.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush(_appearance.Text),
            TextWrapping = TextWrapping.Wrap
        });
        if (description.Length > 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = description,
                Foreground = Brush(_appearance.Muted),
                TextWrapping = TextWrapping.Wrap
            });
        }
        Border row = new Border
        {
            Child = content,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _styler.ApplyCollectionRow(row, selected);
        return row;
    }

    private Control RenderTabs(JsonElement node)
    {
        String selected = Text(node, "selected");
        StackPanel panel = new StackPanel
        {
            Spacing = 0D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        StackPanel tabBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2D
        };
        Border contentFrame = new Border
        {
            Padding = new Thickness(22D),
            MinHeight = 140D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _styler.ApplyDataFrame(contentFrame);
        contentFrame.CornerRadius = new CornerRadius(
            0D,
            0D,
            UsesLumiStyle ? 0D : Math.Max(14D, _appearance.ControlRadius),
            UsesLumiStyle ? 0D : Math.Max(14D, _appearance.ControlRadius));
        contentFrame.BorderThickness = new Thickness(1D, 0D, 1D, 1D);
        List<(ToggleButton Button, JsonElement Tab)> tabs =
            new List<(ToggleButton Button, JsonElement Tab)>();
        if (node.TryGetProperty(LumuiProtocol.Fields.Tabs, out JsonElement values)
            && values.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement tab in values.EnumerateArray())
            {
                String tabId = Text(tab, LumuiProtocol.Fields.Id);
                ToggleButton button = new ToggleButton
                {
                    Content = Text(tab, LumuiProtocol.Fields.Label, "Tab"),
                    IsChecked = tabId == selected || Boolean(tab, "selected")
                };
                ApplyTabButton(button, button.IsChecked == true);
                button.Click += (_, _) =>
                {
                    foreach ((ToggleButton candidate, JsonElement candidateTab) in tabs)
                    {
                        Boolean active = candidate == button;
                        candidate.IsChecked = active;
                        ApplyTabButton(candidate, active);
                        if (active)
                        {
                            contentFrame.Child = RenderTabContent(candidateTab);
                        }
                    }
                };
                tabs.Add((button, tab));
                tabBar.Children.Add(button);
            }
        }
        Int32 activeIndex = tabs.FindIndex(item => item.Button.IsChecked == true);
        if (activeIndex < 0 && tabs.Count > 0)
        {
            activeIndex = 0;
            tabs[0].Button.IsChecked = true;
            ApplyTabButton(tabs[0].Button, true);
        }
        contentFrame.Child = activeIndex >= 0
            ? RenderTabContent(tabs[activeIndex].Tab)
            : Body("Choose a tab.");
        Border tabFrame = new Border
        {
            Child = new ScrollViewer
            {
                Content = tabBar,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
            },
            Background = Brush(_appearance.Surface),
            BorderBrush = Brush(_appearance.Border),
            BorderThickness = new Thickness(1D),
            CornerRadius = new CornerRadius(
                UsesLumiStyle ? 0D : Math.Max(14D, _appearance.ControlRadius),
                UsesLumiStyle ? 0D : Math.Max(14D, _appearance.ControlRadius),
                0D,
                0D),
            Padding = new Thickness(8D, 5D, 8D, 0D)
        };
        panel.Children.Add(tabFrame);
        panel.Children.Add(contentFrame);
        AutomationProperties.SetName(
            panel,
            Text(node, LumuiProtocol.Fields.Label, "Tabs"));
        return panel;
    }

    private void ApplyTabButton(
        ToggleButton button,
        Boolean selected)
    {
        button.Background = Brushes.Transparent;
        button.Foreground = Brush(
            selected ? _appearance.Accent : _appearance.Muted);
        button.BorderBrush = Brush(_appearance.Accent);
        button.BorderThickness = selected
            ? new Thickness(0D, 0D, 0D, 3D)
            : new Thickness(0D);
        button.CornerRadius = new CornerRadius(0D);
        button.Padding = new Thickness(16D, 10D, 16D, 9D);
        button.MinHeight = 44D;
        button.FontWeight = selected
            ? FontWeight.SemiBold
            : FontWeight.Normal;
    }

    private Control RenderTabContent(JsonElement tab)
    {
        if (tab.TryGetProperty(LumuiProtocol.Fields.Items, out JsonElement items)
            && items.ValueKind == JsonValueKind.Array)
        {
            StackPanel panel = new StackPanel { Spacing = 8D };
            foreach (JsonElement item in items.EnumerateArray())
            {
                panel.Children.Add(RenderNode(item));
            }
            return panel;
        }
        return Body(Text(tab, LumuiProtocol.Fields.Description, Text(tab, LumuiProtocol.Fields.Label)));
    }

    private Control RenderToolbar(JsonElement node)
    {
        StackPanel toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8D
        };
        if (node.TryGetProperty(LumuiProtocol.Fields.Actions, out JsonElement actions)
            && actions.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement action in actions.EnumerateArray())
            {
                String actionId = Display(action);
                Button button = (Button)RenderActionReference(node, actionId);
                button.MinHeight = 40D;
                toolbar.Children.Add(button);
            }
        }
        if (node.TryGetProperty(LumuiProtocol.Fields.Items, out JsonElement items)
            && items.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in items.EnumerateArray())
            {
                Control rendered = RenderNode(item);
                rendered.VerticalAlignment = VerticalAlignment.Center;
                toolbar.Children.Add(rendered);
            }
        }
        String label = Text(node, LumuiProtocol.Fields.Label, "Toolbar");
        StackPanel content = new StackPanel { Spacing = 9D };
        if (label.Length > 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = label,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush(_appearance.Text)
            });
        }
        Border frame = new Border
        {
            Child = new ScrollViewer
            {
                Content = toolbar,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
            },
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _styler.ApplySegmentFrame(frame);
        frame.Padding = new Thickness(8D);
        content.Children.Add(frame);
        AutomationProperties.SetName(content, label);
        return content;
    }

    private Control RenderMenu(JsonElement node)
    {
        StackPanel menu = new StackPanel
        {
            Spacing = 2D,
            MaxWidth = 440D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        if (node.TryGetProperty(LumuiProtocol.Fields.Items, out JsonElement items)
            && items.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in items.EnumerateArray())
            {
                Control rendered = RenderMenuRow(item);
                rendered.HorizontalAlignment = HorizontalAlignment.Stretch;
                menu.Children.Add(rendered);
            }
        }
        String label = Text(node, LumuiProtocol.Fields.Label, "Menu");
        Border frame = new Border
        {
            Child = menu,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxWidth = 440D
        };
        _styler.ApplyShowcaseCard(frame);
        frame.Padding = new Thickness(8D);
        AutomationProperties.SetName(frame, label);
        return frame;
    }

    private Control RenderMenuRow(JsonElement node)
    {
        if (Text(node, LumuiProtocol.Fields.Kind)
            == LumuiProtocol.ComponentKinds.Link)
        {
            Button link = (Button)RenderCompactLink(node);
            link.HorizontalAlignment = HorizontalAlignment.Stretch;
            link.HorizontalContentAlignment = HorizontalAlignment.Left;
            link.Background = Brushes.Transparent;
            link.BorderThickness = new Thickness(0D);
            link.Padding = new Thickness(14D, 11D);
            return link;
        }
        String label = Text(
            node,
            LumuiProtocol.Fields.Label,
            RendererText.Action);
        String description = Text(node, LumuiProtocol.Fields.Description);
        StackPanel text = new StackPanel { Spacing = 2D };
        text.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush(_appearance.Text),
            TextWrapping = TextWrapping.Wrap
        });
        if (description.Length > 0)
        {
            text.Children.Add(new TextBlock
            {
                Text = description,
                Foreground = Brush(_appearance.Muted),
                TextWrapping = TextWrapping.Wrap
            });
        }
        Button row = new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Transparent,
            Foreground = Brush(_appearance.Text),
            BorderThickness = new Thickness(0D),
            CornerRadius = new CornerRadius(_appearance.ControlRadius),
            Padding = new Thickness(14D, 11D)
        };
        String componentId = Text(node, LumuiProtocol.Fields.Id);
        String actionId = Text(node, LumuiProtocol.Fields.Action);
        row.IsEnabled = actionId.Length > 0;
        if (actionId.Length > 0)
        {
            row.Click += async (_, _) => await _invoke(
                componentId,
                actionId,
                _inputs.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value(),
                    StringComparer.Ordinal));
        }
        AutomationProperties.SetName(row, label);
        AutomationProperties.SetHelpText(row, description);
        return row;
    }

    private Control RenderBreadcrumb(JsonElement node)
    {
        StackPanel trail = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4D,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (node.TryGetProperty(LumuiProtocol.Fields.Items, out JsonElement items)
            && items.ValueKind == JsonValueKind.Array)
        {
            JsonElement[] values = items.EnumerateArray().ToArray();
            for (Int32 index = 0; index < values.Length; index++)
            {
                if (index > 0)
                {
                    trail.Children.Add(new TextBlock
                    {
                        Text = "›",
                        Margin = new Thickness(5D, 0D),
                        Foreground = Brush(_appearance.Muted),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }
                JsonElement item = values[index];
                if (index == values.Length - 1)
                {
                    trail.Children.Add(new TextBlock
                    {
                        Text = Text(item, LumuiProtocol.Fields.Label, "Current"),
                        Foreground = Brush(_appearance.Text),
                        FontWeight = FontWeight.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    continue;
                }
                Button link = (Button)RenderCompactLink(item);
                link.Background = Brushes.Transparent;
                link.BorderThickness = new Thickness(0D);
                link.Padding = new Thickness(5D, 7D);
                trail.Children.Add(link);
            }
        }
        ScrollViewer scroll = new ScrollViewer
        {
            Content = trail,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        AutomationProperties.SetName(scroll, "Breadcrumb");
        return scroll;
    }

    private Control RenderCompactLink(JsonElement node)
    {
        String label = Text(node, LumuiProtocol.Fields.Label, RendererText.Open);
        Button button = new Button
        {
            Content = label,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10D, 7D)
        };
        ApplyLinkButton(button);
        AutomationProperties.SetName(button, label);
        String href = Text(node, LumuiProtocol.Fields.Href);
        Boolean external = Boolean(node, LumuiProtocol.Fields.External);
        Boolean download = Boolean(node, LumuiProtocol.Fields.Download);
        Uri? uri = ResolveUri(href, external);
        button.IsEnabled = uri is not null;
        if (uri is not null)
        {
            button.Click += async (_, _) =>
            {
                if (download)
                {
                    await _download(uri);
                }
                else if (external)
                {
                    await _openExternal(uri);
                }
                else
                {
                    await _navigate(uri);
                }
            };
        }
        return button;
    }

private Control RenderCalendar(JsonElement node)
    {
        String id = Text(node, LumuiProtocol.Fields.Id);
        String selectedDate = Text(node, LumuiProtocol.Fields.Value);
        if (_settings.Profile.Kind == DeviceProfileKind.Watch)
        {
            return RenderCompactCalendar(id, selectedDate);
        }

        DateTimeOffset selected = ParseDate(selectedDate)
            ?? new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);
        StackPanel panel = new StackPanel { Spacing = 16D };
        StackPanel heading = new StackPanel { Spacing = 4D };
        heading.Children.Add(new TextBlock
        {
            Text = Text(node, LumuiProtocol.Fields.Label, "Calendar"),
            FontSize = Font(24D),
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush(_appearance.Text),
            TextWrapping = TextWrapping.Wrap
        });
        TextBlock selection = new TextBlock
        {
            Text = selected.ToString(
                "d MMMM yyyy 'selected'",
                CultureInfo.CurrentCulture),
            Foreground = Brush(_appearance.Muted),
            TextWrapping = TextWrapping.NoWrap
        };
        heading.Children.Add(selection);
        panel.Children.Add(heading);

        Grid calendar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*,*"),
            RowDefinitions = new RowDefinitions(
                "Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 6D,
            RowSpacing = 6D
        };
        String[] weekdays =
        {
            "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"
        };
        for (Int32 index = 0; index < weekdays.Length; index++)
        {
            TextBlock weekday = new TextBlock
            {
                Text = weekdays[index],
                Foreground = Brush(_appearance.Muted),
                FontSize = Font(12D),
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(2D, 2D, 2D, 6D),
                TextWrapping = TextWrapping.NoWrap
            };
            calendar.Children.Add(weekday);
            Grid.SetColumn(weekday, index);
        }

        List<Button> dateButtons = new List<Button>();
        if (node.TryGetProperty(LumuiProtocol.Fields.Children, out JsonElement days)
            && days.ValueKind == JsonValueKind.Array)
        {
            Int32 dayIndex = 0;
            foreach (JsonElement day in days.EnumerateArray())
            {
                String dayLabel = Text(day, LumuiProtocol.Fields.Label);
                Boolean enabled = !HasFalse(day, LumuiProtocol.Fields.Enabled);
                Boolean isSelected = Boolean(day, "selected");
                TextBlock dayText = new TextBlock
                {
                    Text = dayLabel,
                    FontSize = Font(15D),
                    FontWeight = isSelected
                        ? FontWeight.Bold
                        : FontWeight.Normal,
                    TextWrapping = TextWrapping.NoWrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Button date = new Button
                {
                    Content = dayText,
                    IsEnabled = enabled,
                    MinWidth = 0D,
                    MinHeight = 46D,
                    Padding = new Thickness(0D),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                if (isSelected)
                {
                    ApplyPrimaryButton(date);
                }
                else
                {
                    ApplyLinkButton(date);
                }
                if (!enabled)
                {
                    date.Opacity = 0.42D;
                }
                if (enabled)
                {
                    String componentId = Text(day, LumuiProtocol.Fields.Id);
                    String action = Text(day, LumuiProtocol.Fields.Action);
                    date.Click += async (_, _) =>
                    {
                        selectedDate = selected.Year.ToString(
                                CultureInfo.InvariantCulture)
                            + "-"
                            + selected.Month.ToString(
                                "00",
                                CultureInfo.InvariantCulture)
                            + "-"
                            + dayLabel.PadLeft(2, '0');
                        selection.Text = dayLabel
                            + " "
                            + selected.ToString(
                                "MMMM yyyy",
                                CultureInfo.CurrentCulture)
                            + " selected";
                        foreach (Button candidate in dateButtons)
                        {
                            ApplyLinkButton(candidate);
                        }
                        ApplyPrimaryButton(date);
                        if (action.Length > 0)
                        {
                            await _invoke(
                                componentId,
                                action,
                                new Dictionary<String, Object?>(
                                    StringComparer.Ordinal)
                                {
                                    [id] = selectedDate
                                });
                        }
                    };
                }
                dateButtons.Add(date);
                calendar.Children.Add(date);
                Grid.SetColumn(date, dayIndex % 7);
                Grid.SetRow(date, (dayIndex / 7) + 1);
                dayIndex++;
            }
        }

        _inputs[id] = () => selectedDate;
        panel.Children.Add(calendar);
        Border card = new Border
        {
            Child = panel,
            Padding = new Thickness(22D),
            MaxWidth = 760D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _styler.ApplyComponentPanel(card, _brand.Accent);
        AutomationProperties.SetName(
            card,
            Text(node, LumuiProtocol.Fields.Label, "Calendar"));
        return card;
    }

    private Control RenderCompactCalendar(String id, String selectedValue)
    {
        DateTime selected = new DateTime(2026, 8, 14);
        if (DateTime.TryParse(selectedValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
        {
            selected = parsed;
        }
        TextBlock weekday = new TextBlock
        {
            Text = selected.ToString("dddd", CultureInfo.CurrentCulture),
            Foreground = Brush(_appearance.Accent),
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        TextBlock day = new TextBlock
        {
            Text = selected.ToString("d MMM", CultureInfo.CurrentCulture),
            Foreground = Brush(_appearance.Text),
            FontSize = Font(30D),
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        void Refresh()
        {
            weekday.Text = selected.ToString("dddd", CultureInfo.CurrentCulture);
            day.Text = selected.ToString("d MMM", CultureInfo.CurrentCulture);
        }
        Button previous = new Button { Content = "Previous", MinWidth = 88D };
        Button next = new Button { Content = "Next", MinWidth = 88D };
        ApplyLinkButton(previous);
        ApplyPrimaryButton(next);
        previous.Click += (_, _) =>
        {
            selected = selected.AddDays(-1D);
            Refresh();
        };
        next.Click += (_, _) =>
        {
            selected = selected.AddDays(1D);
            Refresh();
        };
        Grid actions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 8D,
            Children = { previous, next }
        };
        Grid.SetColumn(next, 1);
        StackPanel panel = new StackPanel
        {
            Spacing = 8D,
            Children = { weekday, day, actions }
        };
        _inputs[id] = () => selected.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Border card = new Border
        {
            Child = panel,
            Padding = new Thickness(14D),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        ApplyCard(card);
        return card;
    }

    private Control RenderActionReference(JsonElement node, String actionId)
    {
        String componentId = Text(node, LumuiProtocol.Fields.Id);
        String kind = Text(node, LumuiProtocol.Fields.Kind);
        String label = (kind, actionId) switch
        {
            (LumuiProtocol.ComponentKinds.Dialog, "undo") => "Keep editing",
            (LumuiProtocol.ComponentKinds.Dialog, "component_demo") => "Discard changes",
            (LumuiProtocol.ComponentKinds.Notification, "component_demo") => "View reservation",
            (_, "component_demo") => "Try example",
            _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
                actionId.Replace('_', ' ').Replace('-', ' '))
        };
        Button button = new Button
        {
            Content = label,
            IsEnabled = actionId.Length > 0
        };
        button.Classes.Add(BrowserStyleClasses.Primary);
        ApplyPrimaryButton(button);
        AutomationProperties.SetName(button, label);
        AutomationProperties.SetAutomationId(button, componentId + ".action." + actionId);
        button.Click += async (_, _) =>
        {
            Dictionary<String, Object?> input = _inputs.ToDictionary(
                (KeyValuePair<String, Func<Object?>> pair) => pair.Key,
                (KeyValuePair<String, Func<Object?>> pair) => pair.Value(),
                StringComparer.Ordinal);
            await _invoke(componentId, actionId, input);
        };
        return button;
    }

    private Control RenderText(JsonElement node)
    {
        String role = Text(node, LumuiProtocol.Fields.TextRole, LumuiProtocol.TextRoles.Body);
        String value = Text(node, LumuiProtocol.Fields.Text);
        TextBlock text = Boolean(node, "selectable")
            ? new SelectableTextBlock()
            : new TextBlock();
        text.TextWrapping = TextWrapping.Wrap;
        text.Foreground = Brush(_appearance.Text);
        text.FontSize = TextSize(role);
        text.FontWeight = role == LumuiProtocol.TextRoles.Heading
            ? FontWeight.Light
            : FontWeight.Normal;
        text.MaxWidth = role == LumuiProtocol.TextRoles.Lead
            ? 820D
            : Double.PositiveInfinity;
        text.LineHeight = TextLineHeight(role);
        ReadingTextFormatter.Apply(text, value, _settings.BionicReading);
        if (role == LumuiProtocol.TextRoles.Heading)
        {
            AutomationProperties.SetHeadingLevel(text, 1);
        }
        AutomationProperties.SetAutomationId(text, Text(node, LumuiProtocol.Fields.Id));
        return text;
    }

    private Double TextSize(String role)
    {
        Double value = role switch
        {
            LumuiProtocol.TextRoles.Heading =>
                _settings.Profile.Kind switch
                {
                    DeviceProfileKind.Web => 48D,
                    DeviceProfileKind.Desktop => 42D,
                    DeviceProfileKind.Kiosk => 42D,
                    DeviceProfileKind.Tablet => 36D,
                    DeviceProfileKind.Phone => 30D,
                    DeviceProfileKind.Watch => 20D,
                    _ => 36D
                },
            LumuiProtocol.TextRoles.Lead =>
                _settings.Profile.Kind switch
                {
                    DeviceProfileKind.Web => 21D,
                    DeviceProfileKind.Desktop => 20D,
                    DeviceProfileKind.Tablet => 19D,
                    DeviceProfileKind.Phone => 17D,
                    DeviceProfileKind.Watch => 13D,
                    _ => 18D
                },
            _ => _settings.Profile.Kind switch
            {
                DeviceProfileKind.Web => 16D,
                DeviceProfileKind.Phone => 15D,
                DeviceProfileKind.Watch => 13D,
                _ => 16D
            }
        };
        return Font(value);
    }

    private Double TextLineHeight(String role)
    {
        Double value = role == LumuiProtocol.TextRoles.Heading
            ? _settings.Profile.Kind switch
            {
                DeviceProfileKind.Web => 57D,
                DeviceProfileKind.Desktop => 50D,
                DeviceProfileKind.Kiosk => 50D,
                DeviceProfileKind.Tablet => 43D,
                DeviceProfileKind.Phone => 36D,
                DeviceProfileKind.Watch => 24D,
                _ => 43D
            }
            : _settings.Profile.Kind switch
            {
                DeviceProfileKind.Web => 27D,
                DeviceProfileKind.Phone => 23D,
                DeviceProfileKind.Watch => 18D,
                _ => 25D
            };
        return Font(value);
    }

    private Control RenderRichText(JsonElement node)
    {
        String content = Text(node, LumuiProtocol.Fields.Content);
        StackPanel panel = new StackPanel
        {
            Spacing = 12D,
            MaxWidth = 820D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        String[] lines = content
            .Replace("\r", String.Empty, StringComparison.Ordinal)
            .Split('\n');
        Int32 index = 0;
        while (index < lines.Length)
        {
            String line = lines[index].TrimEnd();
            if (String.IsNullOrWhiteSpace(line))
            {
                index++;
                continue;
            }
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                index++;
                List<String> code = new List<String>();
                while (index < lines.Length
                    && !lines[index].TrimStart().StartsWith(
                        "```",
                        StringComparison.Ordinal))
                {
                    code.Add(lines[index]);
                    index++;
                }
                if (index < lines.Length)
                {
                    index++;
                }
                panel.Children.Add(CodeBlock(
                    String.Join(Environment.NewLine, code)));
                continue;
            }
            Int32 headingLevel = MarkdownHeadingLevel(line);
            if (headingLevel > 0)
            {
                TextBlock heading = RichTextBlock(
                    line[(headingLevel + 1)..],
                    headingLevel == 1 ? 31D : headingLevel == 2 ? 25D : 20D,
                    FontWeight.Bold);
                heading.Margin = new Thickness(0D, index == 0 ? 0D : 8D, 0D, 1D);
                AutomationProperties.SetHeadingLevel(heading, headingLevel);
                panel.Children.Add(heading);
                index++;
                continue;
            }
            Int32 listPrefix = MarkdownListPrefix(line);
            if (listPrefix > 0)
            {
                StackPanel list = new StackPanel { Spacing = 8D };
                while (index < lines.Length)
                {
                    String item = lines[index].Trim();
                    Int32 prefix = MarkdownListPrefix(item);
                    if (prefix == 0)
                    {
                        break;
                    }
                    String marker = item.StartsWith("- ", StringComparison.Ordinal)
                        || item.StartsWith("* ", StringComparison.Ordinal)
                            ? "•"
                            : item[..(prefix - 1)];
                    Grid row = new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                        ColumnSpacing = 10D
                    };
                    row.Children.Add(new TextBlock
                    {
                        Text = marker,
                        Foreground = Brush(_appearance.Accent),
                        FontWeight = FontWeight.Bold,
                        VerticalAlignment = VerticalAlignment.Top
                    });
                    TextBlock itemText = RichTextBlock(
                        item[prefix..],
                        15D,
                        FontWeight.Normal);
                    row.Children.Add(itemText);
                    Grid.SetColumn(itemText, 1);
                    list.Children.Add(row);
                    index++;
                }
                panel.Children.Add(list);
                continue;
            }
            if (line.StartsWith("> ", StringComparison.Ordinal))
            {
                Border quote = new Border
                {
                    Child = RichTextBlock(
                        line[2..],
                        16D,
                        FontWeight.Normal),
                    BorderBrush = Brush(_appearance.Accent),
                    BorderThickness = new Thickness(4D, 0D, 0D, 0D),
                    Padding = new Thickness(16D, 10D),
                    Background = Brush(_appearance.SurfaceAlternate)
                };
                panel.Children.Add(quote);
                index++;
                continue;
            }
            if (line.Contains('|')
                && index + 1 < lines.Length
                && IsMarkdownTableDivider(lines[index + 1]))
            {
                List<String[]> rows = new List<String[]>
                {
                    MarkdownCells(line)
                };
                index += 2;
                while (index < lines.Length && lines[index].Contains('|'))
                {
                    rows.Add(MarkdownCells(lines[index]));
                    index++;
                }
                panel.Children.Add(RenderRichTable(rows));
                continue;
            }
            List<String> paragraph = new List<String> { line.Trim() };
            index++;
            while (index < lines.Length
                && !String.IsNullOrWhiteSpace(lines[index])
                && !IsMarkdownBoundary(lines[index]))
            {
                paragraph.Add(lines[index].Trim());
                index++;
            }
            panel.Children.Add(RichTextBlock(
                String.Join(" ", paragraph),
                16D,
                FontWeight.Normal));
        }
        return panel;
    }

    private TextBlock RichTextBlock(
        String value,
        Double size,
        FontWeight weight)
    {
        TextBlock text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = Font(size),
            LineHeight = Font(size * 1.55D),
            FontWeight = weight,
            Foreground = Brush(_appearance.Text)
        };
        if (_settings.BionicReading)
        {
            ReadingTextFormatter.Apply(text, CleanInlineMarkdown(value), true);
        }
        else
        {
            AppendInlineMarkdown(text, value);
        }
        return text;
    }

    private void AppendInlineMarkdown(TextBlock text, String value)
    {
        Int32 position = 0;
        while (position < value.Length)
        {
            Int32 bold = value.IndexOf("**", position, StringComparison.Ordinal);
            Int32 code = value.IndexOf('`', position);
            Int32 italic = value.IndexOf('*', position);
            Int32 marker = new[] { bold, code, italic }
                .Where(candidate => candidate >= position)
                .DefaultIfEmpty(-1)
                .Min();
            if (marker < 0)
            {
                text.Inlines?.Add(new Run(value[position..]));
                break;
            }
            if (marker > position)
            {
                text.Inlines?.Add(new Run(value[position..marker]));
            }
            if (marker == bold)
            {
                Int32 end = value.IndexOf("**", marker + 2, StringComparison.Ordinal);
                if (end > marker)
                {
                    text.Inlines?.Add(new Run(value[(marker + 2)..end])
                    {
                        FontWeight = FontWeight.Bold
                    });
                    position = end + 2;
                    continue;
                }
            }
            else if (marker == code)
            {
                Int32 end = value.IndexOf('`', marker + 1);
                if (end > marker)
                {
                    text.Inlines?.Add(new Run(value[(marker + 1)..end])
                    {
                        FontFamily = new FontFamily("Consolas"),
                        Foreground = Brush(_appearance.Accent)
                    });
                    position = end + 1;
                    continue;
                }
            }
            else if (marker == italic)
            {
                Int32 end = value.IndexOf('*', marker + 1);
                if (end > marker)
                {
                    text.Inlines?.Add(new Run(value[(marker + 1)..end])
                    {
                        FontStyle = FontStyle.Italic
                    });
                    position = end + 1;
                    continue;
                }
            }
            text.Inlines?.Add(new Run(value[marker].ToString()));
            position = marker + 1;
        }
    }

    private Control RenderRichTable(IReadOnlyList<String[]> rows)
    {
        Int32 columns = rows.Count == 0
            ? 0
            : rows.Max(row => row.Length);
        if (columns == 0)
        {
            return new Border();
        }
        Grid table = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(
                String.Join(",", Enumerable.Repeat("*", columns))),
            MinWidth = columns * 150D
        };
        for (Int32 rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (Int32 columnIndex = 0; columnIndex < columns; columnIndex++)
            {
                String value = columnIndex < rows[rowIndex].Length
                    ? rows[rowIndex][columnIndex]
                    : String.Empty;
                Border cell = new Border
                {
                    Child = RichTextBlock(
                        value,
                        14D,
                        rowIndex == 0
                            ? FontWeight.SemiBold
                            : FontWeight.Normal),
                    Padding = new Thickness(11D, 9D),
                    Background = rowIndex == 0
                        ? Brush(_appearance.SurfaceAlternate)
                        : Brush(_appearance.Surface),
                    BorderBrush = Brush(_appearance.Border),
                    BorderThickness = new Thickness(
                        columnIndex == 0 ? 1D : 0D,
                        rowIndex == 0 ? 1D : 0D,
                        1D,
                        1D)
                };
                table.Children.Add(cell);
                Grid.SetColumn(cell, columnIndex);
                Grid.SetRow(cell, rowIndex);
            }
        }
        Border frame = new Border
        {
            Child = new ScrollViewer
            {
                Content = table,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
            },
            ClipToBounds = true
        };
        _styler.ApplyDataFrame(frame);
        return frame;
    }

    private static Int32 MarkdownHeadingLevel(String line)
    {
        Int32 level = 0;
        while (level < line.Length && line[level] == '#')
        {
            level++;
        }
        return level is > 0 and <= 6
            && level < line.Length
            && line[level] == ' '
                ? level
                : 0;
    }

    private static Int32 MarkdownListPrefix(String line)
    {
        String value = line.TrimStart();
        if (value.StartsWith("- ", StringComparison.Ordinal)
            || value.StartsWith("* ", StringComparison.Ordinal))
        {
            return 2;
        }
        Int32 digits = 0;
        while (digits < value.Length && Char.IsDigit(value[digits]))
        {
            digits++;
        }
        return digits > 0
            && digits + 1 < value.Length
            && value[digits] == '.'
            && value[digits + 1] == ' '
                ? digits + 2
                : 0;
    }

    private static Boolean IsMarkdownBoundary(String line)
    {
        String value = line.TrimStart();
        return value.StartsWith("```", StringComparison.Ordinal)
            || value.StartsWith("> ", StringComparison.Ordinal)
            || MarkdownHeadingLevel(value) > 0
            || MarkdownListPrefix(value) > 0;
    }

    private static Boolean IsMarkdownTableDivider(String line)
    {
        String value = line
            .Replace("|", String.Empty, StringComparison.Ordinal)
            .Replace(":", String.Empty, StringComparison.Ordinal)
            .Replace("-", String.Empty, StringComparison.Ordinal)
            .Trim();
        return value.Length == 0 && line.Contains('-');
    }

    private static String[] MarkdownCells(String line) =>
        line.Trim()
            .Trim('|')
            .Split('|')
            .Select(cell => CleanInlineMarkdown(cell.Trim()))
            .ToArray();

    private Control RenderCode(JsonElement node)
    {
        String value = Text(
            node,
            LumuiProtocol.Fields.Text,
            Text(node, LumuiProtocol.Fields.Content));
        String language = Text(node, "language");
        String componentId = Text(node, LumuiProtocol.Fields.Id);
        String copyAction = Text(node, "copy_action");
        Grid header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12D
        };
        header.Children.Add(new TextBlock
        {
            Text = language.Length > 0
                ? language.ToUpperInvariant()
                : "CODE",
            Foreground = Brush(_appearance.Muted),
            FontSize = Font(12D),
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = 1D,
            VerticalAlignment = VerticalAlignment.Center
        });
        Button copy = new Button
        {
            Content = "Copy",
            HorizontalAlignment = HorizontalAlignment.Right,
            MinHeight = 36D,
            Padding = new Thickness(13D, 6D)
        };
        ApplyLinkButton(copy);
        copy.Click += async (_, _) =>
        {
            IClipboard? clipboard = TopLevel.GetTopLevel(copy)?.Clipboard;
            if (clipboard is not null)
            {
                await ClipboardExtensions.SetTextAsync(clipboard, value);
                _status("Code copied.");
            }
            if (copyAction.Length > 0)
            {
                await _invoke(
                    componentId,
                    copyAction,
                    new Dictionary<String, Object?>(StringComparer.Ordinal));
            }
        };
        header.Children.Add(copy);
        Grid.SetColumn(copy, 1);
        StackPanel content = new StackPanel { Spacing = 10D };
        content.Children.Add(header);
        content.Children.Add(CodeBlock(value));
        Border frame = new Border { Child = content };
        _styler.ApplyShowcaseCard(frame);
        AutomationProperties.SetName(
            frame,
            language.Length > 0 ? language + " code" : "Code");
        return frame;
    }

    private Control CodeBlock(String value)
    {
        SelectableTextBlock code = new SelectableTextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas"),
            Foreground = Brush(_appearance.CodeText),
            LineHeight = Font(22D)
        };
        Border border = new Border
        {
            Background = Brush(_appearance.CodeBackground),
            CornerRadius = new CornerRadius(UsesLumiStyle ? 0D : 12D),
            Padding = new Thickness(14),
            Child = new ScrollViewer
            {
                Content = code,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
            }
        };
        return border;
    }

private Control RenderDetail(JsonElement node)
    {
        String id = Text(node, LumuiProtocol.Fields.Id);
        String label = Text(
            node,
            LumuiProtocol.Fields.Label,
            RendererText.Option);
        String description = Text(node, LumuiProtocol.Fields.Description);
        String detail = Text(
            node,
            LumuiProtocol.Fields.Text,
            DisplayProperty(node, LumuiProtocol.Fields.Value));
        Boolean selected = Boolean(node, "selected");
        StackPanel narrative = ChoiceNarrative(label, description);
        StackPanel trailing = new StackPanel
        {
            Spacing = 5D,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (detail.Length > 0)
        {
            trailing.Children.Add(new TextBlock
            {
                Text = detail,
                Foreground = Brush(_appearance.Text),
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right,
                TextWrapping = TextWrapping.Wrap
            });
        }
        FontAwesomeIcon selectedIcon = new FontAwesomeIcon
        {
            Icon = BrowserIcons.Check,
            IconSize = 16D,
            Foreground = Brush(_appearance.Accent),
            IsVisible = selected,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        trailing.Children.Add(selectedIcon);
        Grid layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 16D,
            Children = { narrative, trailing }
        };
        Grid.SetColumn(trailing, 1);
        String actionId = Text(node, LumuiProtocol.Fields.Action);
        _inputs[id] = () => Text(
            node,
            LumuiProtocol.Fields.Value,
            detail);

        if (actionId.Length == 0)
        {
            selectedIcon.IsVisible = false;
            Border card = new Border
            {
                Child = layout,
                MinHeight = 116D,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            _styler.ApplyShowcaseCard(card);
            AutomationProperties.SetName(card, label);
            AutomationProperties.SetAutomationId(card, id);
            AutomationProperties.SetHelpText(card, description);
            return card;
        }

        Button button = new Button
        {
            Content = layout,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = !HasFalse(node, LumuiProtocol.Fields.Enabled)
        };
        _styler.ApplyChoiceButton(button, selected);
        button.Click += async (_, _) =>
        {
            selected = true;
            selectedIcon.IsVisible = true;
            _styler.ApplyChoiceButton(button, true);
            await InvokeComponentActionAsync(node);
        };
        AutomationProperties.SetName(button, label);
        AutomationProperties.SetAutomationId(button, id);
        AutomationProperties.SetHelpText(button, description);
        return button;
    }

    private Control RenderStatus(JsonElement node)
    {
        String title = Text(
            node,
            LumuiProtocol.Fields.Label,
            Text(node, LumuiProtocol.Fields.Title, RendererText.Status));
        String description = Text(
            node,
            LumuiProtocol.Fields.StateDescription,
            Text(node, LumuiProtocol.Fields.Description));
        String tone = Text(
            node,
            "tone",
            Text(node, "state", "info"));
        FontAwesomeIcon icon = new FontAwesomeIcon
        {
            Icon = tone is "success" or "available"
                ? BrowserIcons.Check
                : BrowserIcons.CircleInfo,
            IconSize = 19D,
            Foreground = Brush(
                tone == "warning"
                    ? _brand.Highlight
                    : tone is "error" or "critical"
                        ? _brand.AccentSecondary
                        : _brand.Accent),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0D, 2D, 0D, 0D)
        };
        StackPanel narrative = new StackPanel { Spacing = 3D };
        narrative.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brush(_appearance.Text),
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        if (description.Length > 0)
        {
            narrative.Children.Add(Body(description));
        }
        Grid layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 12D,
            Children = { icon, narrative }
        };
        Grid.SetColumn(narrative, 1);
        Border panel = new Border { Child = layout };
        _styler.ApplyStatusPanel(panel, tone);
        AutomationProperties.SetName(
            panel,
            description.Length > 0 ? title + ". " + description : title);
        AutomationProperties.SetAutomationId(
            panel,
            Text(node, LumuiProtocol.Fields.Id));
        return panel;
    }

    private Control RenderAlert(JsonElement node)
    {
        String title = Text(
            node,
            LumuiProtocol.Fields.Title,
            Text(node, LumuiProtocol.Fields.Label, "Notice"));
        String message = Text(
            node,
            LumuiProtocol.Fields.Message,
            Text(node, LumuiProtocol.Fields.Description));
        String severity = Text(node, "severity", "info");
        FontAwesomeIcon icon = new FontAwesomeIcon
        {
            Icon = severity == "success"
                ? BrowserIcons.Check
                : severity is "warning" or "error" or "critical"
                    ? BrowserIcons.CircleWarning
                    : BrowserIcons.CircleInfo,
            IconSize = 20D,
            Foreground = Brush(
                severity == "warning"
                    ? _brand.Highlight
                    : severity is "error" or "critical"
                        ? _brand.AccentSecondary
                        : _brand.Accent),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0D, 2D, 0D, 0D)
        };
        StackPanel narrative = ChoiceNarrative(title, message);
        Grid notice = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 12D,
            Children = { icon, narrative }
        };
        Grid.SetColumn(narrative, 1);
        StackPanel content = new StackPanel
        {
            Spacing = 12D,
            Children = { notice }
        };
        if (node.TryGetProperty(
                LumuiProtocol.Fields.Actions,
                out JsonElement actions)
            && actions.ValueKind == JsonValueKind.Array)
        {
            WrapPanel actionBar = new WrapPanel();
            foreach (JsonElement action in actions.EnumerateArray())
            {
                String actionId = Display(action);
                if (actionId.Length == 0)
                {
                    continue;
                }
                Button button = (Button)RenderActionReference(
                    node,
                    actionId);
                button.Margin = new Thickness(0D, 0D, 8D, 0D);
                actionBar.Children.Add(button);
            }
            if (actionBar.Children.Count > 0)
            {
                content.Children.Add(actionBar);
            }
        }
        Border frame = new Border { Child = content };
        _styler.ApplyStatusPanel(frame, severity);
        AutomationProperties.SetName(
            frame,
            message.Length > 0 ? title + ". " + message : title);
        AutomationProperties.SetAutomationId(
            frame,
            Text(node, LumuiProtocol.Fields.Id));
        return frame;
    }

    private Control RenderToast(JsonElement node)
    {
        String message = Text(
            node,
            LumuiProtocol.Fields.Message,
            Text(node, LumuiProtocol.Fields.Description, "Done"));
        String actionId = Text(node, LumuiProtocol.Fields.Action);
        Grid content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(
                actionId.Length > 0 ? "*,Auto" : "*"),
            ColumnSpacing = 12D
        };
        content.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = Brush(_appearance.Text),
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        });
        if (actionId.Length > 0)
        {
            Button action = (Button)RenderActionReference(node, actionId);
            action.HorizontalAlignment = HorizontalAlignment.Right;
            content.Children.Add(action);
            Grid.SetColumn(action, 1);
        }
        Border frame = new Border { Child = content };
        _styler.ApplyStatusPanel(frame, "info");
        frame.MaxWidth = 560D;
        frame.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(frame, message);
        AutomationProperties.SetAutomationId(
            frame,
            Text(node, LumuiProtocol.Fields.Id));
        return frame;
    }

private Control RenderMessage(JsonElement node)
    {
        String kind = Text(node, LumuiProtocol.Fields.Kind);
        if (kind == LumuiProtocol.ComponentKinds.Status)
        {
            return RenderStatus(node);
        }
        if (kind == LumuiProtocol.ComponentKinds.Alert)
        {
            return RenderAlert(node);
        }
        if (kind == LumuiProtocol.ComponentKinds.Toast)
        {
            return RenderToast(node);
        }

        Boolean error = kind == LumuiProtocol.ComponentKinds.Error;
        Boolean empty = kind == LumuiProtocol.ComponentKinds.EmptyState;
        Boolean dialog = kind == LumuiProtocol.ComponentKinds.Dialog;
        Boolean notification = kind == LumuiProtocol.ComponentKinds.Notification;
        String title = Text(
            node,
            LumuiProtocol.Fields.Title,
            Text(
                node,
                LumuiProtocol.Fields.Label,
                error ? "Something went wrong"
                    : empty ? "Nothing here yet"
                    : dialog ? "Please confirm"
                    : notification ? "Notification"
                    : RendererText.Status));
        String message = Text(
            node,
            LumuiProtocol.Fields.Message,
            Text(
                node,
                LumuiProtocol.Fields.Body,
                Text(
                    node,
                    LumuiProtocol.Fields.StateDescription,
                    Text(
                        node,
                        LumuiProtocol.Fields.Description,
                        Text(node, LumuiProtocol.Fields.Text)))));

        StackPanel stack = new StackPanel
        {
            Spacing = empty ? 12D : 8D,
            HorizontalAlignment = empty
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Stretch
        };

        if (empty
            && node.TryGetProperty("illustration", out JsonElement illustration)
            && illustration.ValueKind == JsonValueKind.Object)
        {
            Control visual = RenderNode(illustration);
            visual.HorizontalAlignment = HorizontalAlignment.Center;
            visual.Margin = new Thickness(0D, 0D, 0D, 4D);
            stack.Children.Add(visual);
        }
        else if (error || dialog || notification)
        {
            String icon = error || dialog
                ? BrowserIcons.CircleWarning
                : BrowserIcons.CircleInfo;
            FontAwesomeIcon glyph = new FontAwesomeIcon
            {
                Icon = icon,
                IconSize = 20D,
                Foreground = Brush(error || dialog
                    ? _brand.AccentSecondary
                    : _appearance.Accent),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            stack.Children.Add(new Border
            {
                Width = 42D,
                Height = 42D,
                CornerRadius = new CornerRadius(21D),
                Background = Brush(_appearance.SurfaceAlternate),
                Child = glyph
            });
        }

        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = Font(dialog ? 24D : 21D),
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush(_appearance.Text),
            TextAlignment = empty ? TextAlignment.Center : TextAlignment.Left,
            TextWrapping = TextWrapping.Wrap
        });

        if (message.Length > 0)
        {
            TextBlock body = Body(message);
            body.TextAlignment = empty ? TextAlignment.Center : TextAlignment.Left;
            body.MaxWidth = empty ? 460D : Double.PositiveInfinity;
            stack.Children.Add(body);
        }

        if (notification)
        {
            String category = Text(node, "category");
            if (category.Length > 0)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = category.Replace('-', ' '),
                    Foreground = Brush(_appearance.Muted),
                    FontSize = Font(12D),
                    FontWeight = FontWeight.SemiBold
                });
            }
        }

        WrapPanel actionBar = new WrapPanel
        {
            Margin = new Thickness(0D, 10D, 0D, 0D),
            HorizontalAlignment = empty
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Left
        };
        if (node.TryGetProperty(LumuiProtocol.Fields.Actions, out JsonElement actions)
            && actions.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement action in actions.EnumerateArray())
            {
                String actionId = Display(action);
                if (actionId.Length == 0)
                {
                    continue;
                }
                Button button = (Button)RenderActionReference(node, actionId);
                button.Margin = new Thickness(0D, 0D, 8D, 0D);
                actionBar.Children.Add(button);
            }
        }
        else
        {
            String actionId = Text(node, LumuiProtocol.Fields.Action);
            if (actionId.Length > 0)
            {
                actionBar.Children.Add(RenderActionReference(node, actionId));
            }
        }
        if (actionBar.Children.Count > 0)
        {
            stack.Children.Add(actionBar);
        }

        Border card = new Border
        {
            Child = stack,
            Padding = new Thickness(
                dialog ? 26D : 22D),
            MaxWidth = dialog ? 560D : Double.PositiveInfinity,
            HorizontalAlignment = dialog || empty
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Stretch
        };
        card.Classes.Add(BrowserStyleClasses.Soft);
        _styler.ApplyComponentPanel(
            card,
            error || dialog
                ? _brand.AccentSecondary
                : empty
                    ? _brand.AccentTertiary
                    : _appearance.Accent);
        AutomationProperties.SetName(card, title);
        return card;
    }

    private Control RenderTable(JsonElement node)
    {
        StackPanel panel = new StackPanel { Spacing = 12D };
        String caption = Text(node, LumuiProtocol.Fields.Caption, Text(node, LumuiProtocol.Fields.Label));
        if (caption.Length > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = caption,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush(_appearance.Text),
                FontSize = Font(18D),
                TextWrapping = TextWrapping.Wrap
            });
        }
        JsonElement[] columns = node.TryGetProperty(LumuiProtocol.Fields.Columns, out JsonElement columnValue)
            && columnValue.ValueKind == JsonValueKind.Array
                ? columnValue.EnumerateArray().ToArray()
                : Array.Empty<JsonElement>();
        if (columns.Length == 0)
        {
            return RenderFallback(node);
        }
        Grid table = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(
                String.Join(",", columns.Select(_ => "*"))),
            MinWidth = Math.Min(820D, columns.Length * 180D)
        };
        table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (Int32 index = 0; index < columns.Length; index++)
        {
            Grid heading = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 8D
            };
            TextBlock headingText = new TextBlock
            {
                Text = Text(
                    columns[index],
                    LumuiProtocol.Fields.Label,
                    Text(
                        columns[index],
                        LumuiProtocol.Fields.Title,
                        Text(columns[index], LumuiProtocol.Fields.Id, RendererText.Column))),
                Foreground = Brush(_appearance.Text),
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            heading.Children.Add(headingText);
            if (Boolean(columns[index], "sortable"))
            {
                TextBlock sort = new TextBlock
                {
                    Text = "↕",
                    Foreground = Brush(_appearance.Muted),
                    FontSize = Font(13D),
                    VerticalAlignment = VerticalAlignment.Center
                };
                heading.Children.Add(sort);
                Grid.SetColumn(sort, 1);
            }
            Border cell = new Border
            {
                Child = heading,
                Padding = new Thickness(12D, 10D),
                Background = Brush(_appearance.SurfaceAlternate),
                BorderBrush = Brush(_appearance.Border),
                BorderThickness = new Thickness(
                    index == 0 ? 1D : 0D,
                    1D,
                    1D,
                    1D)
            };
            table.Children.Add(cell);
            Grid.SetColumn(cell, index);
        }
        if (node.TryGetProperty(LumuiProtocol.Fields.Rows, out JsonElement rows) && rows.ValueKind == JsonValueKind.Array)
        {
            Int32 rowIndex = 1;
            foreach (JsonElement row in rows.EnumerateArray())
            {
                table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                foreach ((JsonElement column, Int32 index) in columns.Select(
                    (JsonElement value, Int32 index) => (value, index)))
                {
                    String key = Text(column, LumuiProtocol.Fields.Id, index.ToString(CultureInfo.InvariantCulture));
                    JsonElement cell;
                    String value;
                    if (row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out cell))
                    {
                        value = Display(cell);
                    }
                    else if (row.ValueKind == JsonValueKind.Array && index < row.GetArrayLength())
                    {
                        value = Display(row[index]);
                    }
                    else
                    {
                        value = String.Empty;
                    }
                    Border dataCell = new Border
                    {
                        Child = new TextBlock
                        {
                            Text = value,
                            Foreground = Brush(_appearance.Text),
                            TextWrapping = TextWrapping.Wrap,
                            TextAlignment = index == 0
                                ? TextAlignment.Left
                                : TextAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        Padding = new Thickness(12D, 10D),
                        Background = rowIndex % 2 == 0
                            ? Brush(_appearance.SurfaceAlternate)
                            : Brush(_appearance.Surface),
                        BorderBrush = Brush(_appearance.Border),
                        BorderThickness = new Thickness(
                            index == 0 ? 1D : 0D,
                            0D,
                            1D,
                            1D)
                    };
                    table.Children.Add(dataCell);
                    Grid.SetColumn(dataCell, index);
                    Grid.SetRow(dataCell, rowIndex);
                }
                rowIndex++;
            }
        }
        ScrollViewer tableScroll = new ScrollViewer
        {
            Content = table,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Border frame = new Border
        {
            Child = tableScroll,
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _styler.ApplyDataFrame(frame);
        panel.Children.Add(frame);
        AutomationProperties.SetName(
            frame,
            caption.Length > 0 ? caption : "Table");
        return panel;
    }

    private Control RenderValue(JsonElement node)
    {
        StackPanel panel = new StackPanel { Spacing = 5D };
        String label = Text(node, LumuiProtocol.Fields.Label);
        if (label.Length > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = Brush(_appearance.Muted),
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
        }
        String explicitText = Text(node, LumuiProtocol.Fields.Text);
        String value = explicitText.Length > 0
            ? explicitText
            : DisplayProperty(node, LumuiProtocol.Fields.Value)
                + Text(node, LumuiProtocol.Fields.Unit);
        panel.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = Font(30D),
            FontWeight = FontWeight.Bold,
            Foreground = Brush(_appearance.Text),
            TextWrapping = TextWrapping.Wrap
        });
        String description = Text(node, LumuiProtocol.Fields.Description);
        if (description.Length > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = description,
                Foreground = Brush(_appearance.Muted),
                TextWrapping = TextWrapping.Wrap
            });
        }
        Border frame = new Border
        {
            Child = panel,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _styler.ApplyCollectionRow(frame);
        AutomationProperties.SetName(
            frame,
            label.Length > 0 ? label + " " + value : value);
        return frame;
    }

    private Control RenderQuote(JsonElement node)
    {
        StackPanel text = new StackPanel { Spacing = 10D };
        TextBlock quotation = new TextBlock
        {
            FontSize = Font(20D),
            LineHeight = Font(31D),
            FontStyle = FontStyle.Italic,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(_appearance.Text)
        };
        ReadingTextFormatter.Apply(
            quotation,
            Text(node, LumuiProtocol.Fields.Text),
            _settings.BionicReading);
        text.Children.Add(quotation);
        String attribution = Text(node, LumuiProtocol.Fields.Attribution);
        if (attribution.Length > 0)
        {
            text.Children.Add(new TextBlock
            {
                Text = attribution,
                Foreground = Brush(_appearance.Muted),
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
        }
        Grid content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 16D
        };
        content.Children.Add(new TextBlock
        {
            Text = "“",
            Foreground = Brush(_appearance.Accent),
            FontSize = Font(42D),
            FontWeight = FontWeight.Bold,
            LineHeight = Font(38D),
            VerticalAlignment = VerticalAlignment.Top
        });
        content.Children.Add(text);
        Grid.SetColumn(text, 1);
        Border border = new Border
        {
            Child = content,
            Background = Brush(_appearance.SurfaceAlternate),
            BorderBrush = Brush(_appearance.Accent),
            BorderThickness = new Thickness(5D, 0D, 0D, 0D),
            CornerRadius = new CornerRadius(
                UsesLumiStyle ? 0D : Math.Max(14D, _appearance.ControlRadius)),
            Padding = new Thickness(22D),
            MaxWidth = 820D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        border.Classes.Add(BrowserStyleClasses.Soft);
        AutomationProperties.SetName(
            border,
            attribution.Length > 0 ? "Quote from " + attribution : "Quote");
        AutomationProperties.SetHelpText(
            border,
            Text(node, "cite"));
        return border;
    }

    private Control RenderFigure(JsonElement node)
    {
        StackPanel panel = new StackPanel
        {
            Spacing = 10D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        if (node.TryGetProperty(LumuiProtocol.Fields.Content, out JsonElement content))
        {
            panel.Children.Add(
                content.ValueKind == JsonValueKind.Object && content.TryGetProperty(LumuiProtocol.Fields.Kind, out _)
                    ? RenderNode(content)
                    : Body(Display(content)));
        }
        String caption = Text(node, LumuiProtocol.Fields.Caption);
        if (caption.Length > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = caption,
                Foreground = Brush(_appearance.Text),
                FontWeight = FontWeight.SemiBold,
                FontSize = Font(16D),
                TextWrapping = TextWrapping.Wrap
            });
        }
        String credit = Text(node, "credit");
        if (credit.Length > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = credit,
                Foreground = Brush(_appearance.Muted),
                FontSize = Font(13D),
                TextWrapping = TextWrapping.Wrap
            });
        }
        String source = Text(node, "source_link");
        if (source.Length > 0)
        {
            Uri? sourceUri = ResolveUri(source, allowExternal: true);
            Button sourceButton = new Button
            {
                Content = "View original",
                IsEnabled = sourceUri is not null,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            ApplyLinkButton(sourceButton);
            if (sourceUri is not null)
            {
                sourceButton.Click += async (_, _) => await _openExternal(sourceUri);
            }
            panel.Children.Add(sourceButton);
        }
        Border frame = new Border { Child = panel };
        _styler.ApplyShowcaseCard(frame);
        AutomationProperties.SetName(
            frame,
            caption.Length > 0 ? caption : RendererText.Image);
        return frame;
    }

    private Control RenderImageCollection(JsonElement node)
    {
        StackPanel panel = new StackPanel
        {
            Spacing = 12D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        String caption = Text(node, LumuiProtocol.Fields.Caption, Text(node, LumuiProtocol.Fields.Label));
        if (caption.Length > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = caption,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush(_appearance.Text),
                TextWrapping = TextWrapping.Wrap
            });
        }
        List<JsonElement> sources = new List<JsonElement>();
        if (node.TryGetProperty(LumuiProtocol.Fields.Images, out JsonElement images) && images.ValueKind == JsonValueKind.Array)
        {
            sources.AddRange(images.EnumerateArray());
        }
        if (sources.Count == 0)
        {
            return panel;
        }
        Int32 current = Math.Clamp((Int32)Number(node, "current_index"), 0, sources.Count - 1);
        ContentControl viewport = new ContentControl
        {
            Height = _embeddedPresentation
                ? 230D
                : _settings.Profile.Kind switch
            {
                DeviceProfileKind.Watch => 130D,
                DeviceProfileKind.Phone => 220D,
                _ => 290D
            },
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        TextBlock imageLabel = Body(String.Empty);
        TextBlock position = new TextBlock
        {
            Foreground = Brush(_appearance.Muted),
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.SemiBold
        };
        void ShowImage()
        {
            JsonElement image = sources[current];
            Uri? uri = SourceUriValue(image);
            String label = image.ValueKind == JsonValueKind.Object
                ? Text(image, LumuiProtocol.Fields.Alt, Text(image, LumuiProtocol.Fields.Label, RendererText.Image))
                : RendererText.Image;
            imageLabel.Text = label;
            position.Text = (current + 1).ToString(CultureInfo.CurrentCulture)
                + " of "
                + sources.Count.ToString(CultureInfo.CurrentCulture);
            if (uri is null)
            {
                viewport.Content = Body(label);
                return;
            }
            ContentControl imageHost = new ContentControl
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = Body(label)
            };
            viewport.Content = imageHost;
            _ = _assetLoader.LoadAsync(imageHost, uri, MediaType(image));
        }
        Button previous = new Button { Content = "Previous", IsEnabled = sources.Count > 1 };
        Button next = new Button { Content = "Next", IsEnabled = sources.Count > 1 };
        ApplyLinkButton(previous);
        ApplyPrimaryButton(next);
        previous.Click += (_, _) =>
        {
            current = (current - 1 + sources.Count) % sources.Count;
            ShowImage();
        };
        next.Click += (_, _) =>
        {
            current = (current + 1) % sources.Count;
            ShowImage();
        };
        Grid navigation = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Children = { previous, position, next }
        };
        Grid.SetColumn(position, 1);
        position.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(next, 2);
        Border frame = new Border { Child = viewport };
        _styler.ApplyMediaFrame(frame);
        panel.Children.Add(frame);
        panel.Children.Add(imageLabel);
        panel.Children.Add(navigation);
        ShowImage();
        AutomationProperties.SetName(
            frame,
            caption.Length > 0 ? caption : RendererText.Image);
        return panel;
    }

    private Control RenderCompactContent(JsonElement node)
    {
        String text = Text(
            node,
            LumuiProtocol.Fields.Label,
            DisplayProperty(node, LumuiProtocol.Fields.Value, Text(node, LumuiProtocol.Fields.Meaning, Text(node, LumuiProtocol.Fields.Symbol, Text(node, LumuiProtocol.Fields.Kind)))));
        Border border = new Border
        {
            Child = new TextBlock
            {
                Text = text,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush(_appearance.Text)
            }
        };
        border.Classes.Add(BrowserStyleClasses.Soft);
        ApplySoftPanel(border);
        return border;
    }

private Control RenderIcon(JsonElement node)
    {
        String symbol = Text(node, LumuiProtocol.Fields.Symbol);
        String meaning = Text(
            node,
            LumuiProtocol.Fields.Meaning,
            Text(node, LumuiProtocol.Fields.Label, symbol));
        FontAwesomeIcon glyph = new FontAwesomeIcon
        {
            Icon = SemanticIcon(symbol),
            IconSize = 28D,
            Foreground = Brush(_appearance.Accent),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Border icon = new Border
        {
            Width = 64D,
            Height = 64D,
            CornerRadius = new CornerRadius(UsesLumiStyle ? 0D : 20D),
            Background = Brush(_appearance.SurfaceAlternate),
            BorderBrush = Brush(_appearance.Border),
            BorderThickness = new Thickness(1D),
            Child = glyph
        };
        AutomationProperties.SetName(icon, meaning);
        AutomationProperties.SetAutomationId(
            icon,
            Text(node, LumuiProtocol.Fields.Id));
        return icon;
    }

private Control RenderBadge(JsonElement node)
    {
        String label = Text(node, LumuiProtocol.Fields.Label);
        String value = DisplayProperty(node, LumuiProtocol.Fields.Value);
        String text = label.Length > 0 && value.Length > 0
            ? label + " " + value
            : label.Length > 0
                ? label
                : value;
        String tone = Text(node, "tone");
        String accent = tone switch
        {
            "success" => _brand.Accent,
            "warning" => _brand.Highlight,
            "error" => _brand.AccentSecondary,
            _ => _brand.AccentTertiary
        };
        Border indicator = new Border
        {
            Width = 8D,
            Height = 8D,
            CornerRadius = new CornerRadius(999D),
            Background = Brush(accent),
            VerticalAlignment = VerticalAlignment.Center
        };
        TextBlock content = new TextBlock
        {
            Text = text,
            Foreground = Brush(_appearance.Text),
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        StackPanel row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8D,
            Children = { indicator, content }
        };
        Border badge = new Border
        {
            Background = Brush(_appearance.SurfaceAlternate),
            BorderBrush = Brush(accent),
            BorderThickness = new Thickness(1D),
            CornerRadius = new CornerRadius(UsesLumiStyle ? 0D : 999D),
            Padding = new Thickness(12D, 7D),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = row
        };
        AutomationProperties.SetName(badge, text);
        AutomationProperties.SetAutomationId(
            badge,
            Text(node, LumuiProtocol.Fields.Id));
        return badge;
    }

private Control RenderChart(JsonElement node)
    {
        List<String> labels = new List<String>();
        List<Double> values = new List<Double>();
        String seriesLabel = String.Empty;
        if (node.TryGetProperty("data", out JsonElement data)
            && data.ValueKind == JsonValueKind.Object)
        {
            if (data.TryGetProperty("labels", out JsonElement dataLabels)
                && dataLabels.ValueKind == JsonValueKind.Array)
            {
                labels.AddRange(dataLabels.EnumerateArray().Select(Display));
            }
            if (data.TryGetProperty("values", out JsonElement dataValues)
                && dataValues.ValueKind == JsonValueKind.Array)
            {
                values.AddRange(dataValues.EnumerateArray().Select(value =>
                    value.TryGetDouble(out Double number) ? number : 0D));
            }
        }
        if (node.TryGetProperty("series", out JsonElement series)
            && series.ValueKind == JsonValueKind.Array)
        {
            JsonElement first = series.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object)
            {
                seriesLabel = Text(first, LumuiProtocol.Fields.Label);
                if (values.Count == 0
                    && first.TryGetProperty(
                        LumuiProtocol.Fields.Values,
                        out JsonElement seriesValues)
                    && seriesValues.ValueKind == JsonValueKind.Array)
                {
                    values.AddRange(seriesValues.EnumerateArray().Select(value =>
                        value.TryGetDouble(out Double number) ? number : 0D));
                }
            }
        }
        if (values.Count == 0)
        {
            return RenderFallback(node);
        }

        Double maximum = Math.Max(1D, values.Max());
        Grid plot = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(
                String.Join(
                    ",",
                    Enumerable.Repeat("*", values.Count))),
            ColumnSpacing = _settings.Profile.Kind is
                DeviceProfileKind.Phone or DeviceProfileKind.Watch
                    ? 6D
                    : 14D,
            Height = _settings.Profile.Kind is
                DeviceProfileKind.Phone or DeviceProfileKind.Watch
                    ? 230D
                    : 300D
        };
        for (Int32 index = 0; index < values.Count; index++)
        {
            Double value = Math.Max(0D, values[index]);
            String category = index < labels.Count
                ? labels[index]
                : (index + 1).ToString(CultureInfo.InvariantCulture);
            String amountText = value.ToString(
                "0.##",
                CultureInfo.CurrentCulture);
            Border bar = new Border
            {
                Height = Math.Max(8D, (value / maximum) * 190D),
                MinWidth = 18D,
                MaxWidth = 72D,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = Brush((index % 4) switch
                {
                    0 => _brand.Accent,
                    1 => _brand.AccentSecondary,
                    2 => _brand.AccentTertiary,
                    _ => _brand.Highlight
                }),
                CornerRadius = new CornerRadius(
                    UsesLumiStyle ? 0D : 9D,
                    UsesLumiStyle ? 0D : 9D,
                    UsesLumiStyle ? 0D : 3D,
                    UsesLumiStyle ? 0D : 3D)
            };
            StackPanel column = new StackPanel
            {
                Spacing = 7D,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Children =
                {
                    new TextBlock
                    {
                        Text = amountText,
                        Foreground = Brush(_appearance.Text),
                        FontWeight = FontWeight.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextWrapping = TextWrapping.NoWrap
                    },
                    bar,
                    new TextBlock
                    {
                        Text = category,
                        Foreground = Brush(_appearance.Muted),
                        FontSize = Font(12D),
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        MaxHeight = 44D
                    }
                }
            };
            plot.Children.Add(column);
            Grid.SetColumn(column, index);
            AutomationProperties.SetName(
                column,
                category + ", " + amountText);
        }

        String summary = Text(
            node,
            LumuiProtocol.Fields.Summary,
            RendererText.Chart);
        StackPanel content = new StackPanel { Spacing = 14D };
        if (seriesLabel.Length > 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = seriesLabel,
                Foreground = Brush(_appearance.Text),
                FontWeight = FontWeight.SemiBold,
                FontSize = Font(18D)
            });
        }
        content.Children.Add(new Border
        {
            Child = plot,
            Padding = new Thickness(18D, 18D, 18D, 10D),
            Background = Brush(_appearance.SurfaceAlternate),
            BorderBrush = Brush(_appearance.Border),
            BorderThickness = new Thickness(1D),
            CornerRadius = new CornerRadius(UsesLumiStyle ? 0D : 16D)
        });
        content.Children.Add(Body(summary));
        Border frame = new Border { Child = content };
        _styler.ApplyShowcaseCard(frame);
        AutomationProperties.SetName(frame, summary);
        AutomationProperties.SetAutomationId(
            frame,
            Text(node, LumuiProtocol.Fields.Id));
        return frame;
    }

private Control RenderMultiSelect(JsonElement node)
    {
        String id = Text(node, LumuiProtocol.Fields.Id);
        HashSet<String> selected =
            node.TryGetProperty(
                LumuiProtocol.Fields.Values,
                out JsonElement values)
            && values.ValueKind == JsonValueKind.Array
                ? values
                    .EnumerateArray()
                    .Select(Display)
                    .ToHashSet(StringComparer.Ordinal)
                : new HashSet<String>(StringComparer.Ordinal);
        Int32 minimum = Math.Max(
            0,
            (Int32)Number(node, "min_selected"));
        Int32 maximum = Math.Max(
            minimum,
            (Int32)Number(node, "max_selected", Int32.MaxValue));
        List<(CheckBox Control, Border Card, String Value)> entries =
            new List<(CheckBox Control, Border Card, String Value)>();
        StackPanel optionsPanel = new StackPanel { Spacing = 8D };
        TextBlock summary = new TextBlock
        {
            Foreground = Brush(_appearance.Muted),
            FontSize = Font(13D)
        };
        void UpdateSummary()
        {
            summary.Text = selected.Count.ToString(
                    CultureInfo.CurrentCulture)
                + (selected.Count == 1 ? " selected" : " selected");
        }
        if (node.TryGetProperty(
                LumuiProtocol.Fields.Options,
                out JsonElement options)
            && options.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement option in options.EnumerateArray())
            {
                String value = Text(
                    option,
                    LumuiProtocol.Fields.Value,
                    Text(option, LumuiProtocol.Fields.Id));
                String label = Text(
                    option,
                    LumuiProtocol.Fields.Label,
                    value);
                String description = Text(
                    option,
                    LumuiProtocol.Fields.Description);
                CheckBox check = new CheckBox
                {
                    Content = ChoiceNarrative(label, description),
                    IsChecked = selected.Contains(value),
                    IsEnabled = !HasFalse(
                        node,
                        LumuiProtocol.Fields.Enabled)
                        && !HasFalse(
                            option,
                            LumuiProtocol.Fields.Enabled),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                Border card = new Border { Child = check };
                _styler.ApplyChoiceCard(card, selected.Contains(value));
                entries.Add((check, card, value));
                optionsPanel.Children.Add(card);
            }
        }
        _inputs[id] = () => selected.ToArray();
        foreach ((CheckBox check, Border card, String value) in entries)
        {
            check.Click += async (_, _) =>
            {
                Boolean requested = check.IsChecked == true;
                if (requested && selected.Count >= maximum)
                {
                    check.IsChecked = false;
                    return;
                }
                if (!requested
                    && selected.Contains(value)
                    && selected.Count <= minimum)
                {
                    check.IsChecked = true;
                    return;
                }
                if (requested)
                {
                    selected.Add(value);
                }
                else
                {
                    selected.Remove(value);
                }
                _styler.ApplyChoiceCard(card, requested);
                UpdateSummary();
                await InvokeComponentActionAsync(node);
            };
        }
        UpdateSummary();
        StackPanel group = new StackPanel
        {
            Spacing = 10D,
            Children = { optionsPanel, summary }
        };
        return LabeledChoiceGroup(node, group);
    }

private Control RenderSlider(JsonElement node)
    {
        String id = Text(node, LumuiProtocol.Fields.Id);
        Double minimum = Number(node, LumuiProtocol.Fields.Min);
        Double maximum = Number(node, LumuiProtocol.Fields.Max, 100D);
        if (maximum <= minimum)
        {
            maximum = minimum + 1D;
        }
        Double value = Math.Clamp(
            Number(node, LumuiProtocol.Fields.Value),
            minimum,
            maximum);
        String unit = Text(node, LumuiProtocol.Fields.Unit);
        TextBlock output = new TextBlock
        {
            Foreground = Brush(_appearance.Text),
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Border outputFrame = new Border
        {
            MinWidth = 72D,
            MinHeight = 42D,
            Padding = new Thickness(12D, 7D),
            Background = Brush(_appearance.SurfaceAlternate),
            BorderBrush = Brush(_appearance.Border),
            BorderThickness = new Thickness(1D),
            CornerRadius = new CornerRadius(UsesLumiStyle ? 0D : 12D),
            Child = output
        };
        Slider slider = new Slider
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = value,
            TickFrequency = Math.Max(
                0.001D,
                Number(node, LumuiProtocol.Fields.Step, 1D)),
            IsEnabled = !HasFalse(node, LumuiProtocol.Fields.Enabled),
            MinHeight = 44D
        };
        void UpdateOutput()
        {
            output.Text = slider.Value.ToString(
                    "0.##",
                    CultureInfo.CurrentCulture)
                + unit;
        }
        slider.PropertyChanged += (_, change) =>
        {
            if (change.Property == RangeBase.ValueProperty)
            {
                UpdateOutput();
            }
        };
        slider.PointerReleased += async (_, _) =>
            await InvokeComponentActionAsync(node);
        slider.KeyUp += async (_, args) =>
        {
            if (args.Key is Avalonia.Input.Key.Left
                or Avalonia.Input.Key.Right
                or Avalonia.Input.Key.Up
                or Avalonia.Input.Key.Down
                or Avalonia.Input.Key.PageUp
                or Avalonia.Input.Key.PageDown
                or Avalonia.Input.Key.Home
                or Avalonia.Input.Key.End)
            {
                await InvokeComponentActionAsync(node);
            }
        };
        UpdateOutput();
        StackPanel headingText = ChoiceNarrative(
            Text(node, LumuiProtocol.Fields.Label, "Value"),
            Text(node, LumuiProtocol.Fields.Description));
        Grid heading = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 14D,
            Children = { headingText, outputFrame }
        };
        Grid.SetColumn(outputFrame, 1);
        StackPanel content = new StackPanel
        {
            Spacing = 14D,
            Children = { heading, slider }
        };
        if (node.TryGetProperty("marks", out JsonElement marks)
            && marks.ValueKind == JsonValueKind.Array)
        {
            JsonElement[] entries = marks.EnumerateArray().ToArray();
            if (entries.Length > 0)
            {
                Grid markRow = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions(
                        String.Join(",", entries.Select(_ => "*")))
                };
                for (Int32 index = 0; index < entries.Length; index++)
                {
                    TextBlock mark = new TextBlock
                    {
                        Text = Text(
                            entries[index],
                            LumuiProtocol.Fields.Label,
                            Display(entries[index])),
                        Foreground = Brush(_appearance.Muted),
                        FontSize = Font(12D),
                        TextAlignment = index == 0
                            ? TextAlignment.Left
                            : index == entries.Length - 1
                                ? TextAlignment.Right
                                : TextAlignment.Center
                    };
                    markRow.Children.Add(mark);
                    Grid.SetColumn(mark, index);
                }
                content.Children.Add(markRow);
            }
        }
        _inputs[id] = () => slider.Value;
        Border frame = new Border { Child = content };
        _styler.ApplyShowcaseCard(frame);
        AutomationProperties.SetName(
            slider,
            Text(node, LumuiProtocol.Fields.Label, "Value"));
        AutomationProperties.SetAutomationId(slider, id);
        return frame;
    }

private Control RenderStepper(JsonElement node)
    {
        String id = Text(node, LumuiProtocol.Fields.Id);
        Double minimum = Number(node, LumuiProtocol.Fields.Min, Double.MinValue);
        Double maximum = Number(node, LumuiProtocol.Fields.Max, Double.MaxValue);
        Double step = Math.Max(
            0.001D,
            Number(node, LumuiProtocol.Fields.Step, 1D));
        Double value = Math.Clamp(
            Number(node, LumuiProtocol.Fields.Value),
            minimum,
            maximum);
        String unit = Text(node, LumuiProtocol.Fields.Unit);
        TextBox input = new TextBox
        {
            Text = value.ToString("0.##", CultureInfo.CurrentCulture),
            IsEnabled = !HasFalse(node, LumuiProtocol.Fields.Enabled),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            MinWidth = 82D,
            FontWeight = FontWeight.Bold
        };
        Button decrease = IconButton(
            BrowserIcons.ZoomOut,
            "Decrease",
            !HasFalse(node, LumuiProtocol.Fields.Enabled));
        Button increase = IconButton(
            BrowserIcons.ZoomIn,
            "Increase",
            !HasFalse(node, LumuiProtocol.Fields.Enabled));
        TextBlock unitLabel = new TextBlock
        {
            Text = unit,
            Foreground = Brush(_appearance.Muted),
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = unit.Length > 0
        };
        void Normalize()
        {
            value = Double.TryParse(
                input.Text,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out Double parsed)
                    ? Math.Clamp(parsed, minimum, maximum)
                    : value;
            input.Text = value.ToString(
                "0.##",
                CultureInfo.CurrentCulture);
            decrease.IsEnabled = value > minimum;
            increase.IsEnabled = value < maximum;
        }
        async Task ChangeAsync(Double amount)
        {
            Normalize();
            value = Math.Clamp(value + amount, minimum, maximum);
            input.Text = value.ToString(
                "0.##",
                CultureInfo.CurrentCulture);
            Normalize();
            await InvokeComponentActionAsync(node);
        }
        decrease.Click += async (_, _) => await ChangeAsync(-step);
        increase.Click += async (_, _) => await ChangeAsync(step);
        input.LostFocus += async (_, _) =>
        {
            Normalize();
            await InvokeComponentActionAsync(node);
        };
        input.KeyUp += async (_, args) =>
        {
            if (args.Key == Avalonia.Input.Key.Enter)
            {
                Normalize();
                await InvokeComponentActionAsync(node);
            }
        };
        Grid controls = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            ColumnSpacing = 9D,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children = { decrease, input, unitLabel, increase }
        };
        Grid.SetColumn(input, 1);
        Grid.SetColumn(unitLabel, 2);
        Grid.SetColumn(increase, 3);
        _inputs[id] = () =>
        {
            Normalize();
            return value;
        };
        StackPanel content = new StackPanel
        {
            Spacing = 12D,
            Children =
            {
                ChoiceNarrative(
                    Text(node, LumuiProtocol.Fields.Label, "Value"),
                    Text(node, LumuiProtocol.Fields.Description)),
                controls
            }
        };
        Border frame = new Border { Child = content };
        _styler.ApplyShowcaseCard(frame);
        Normalize();
        AutomationProperties.SetAutomationId(input, id);
        return frame;
    }

private Control RenderDateRange(JsonElement node)
    {
        String id = Text(node, LumuiProtocol.Fields.Id);
        DatePicker start = new DatePicker
        {
            SelectedDate = ParseDate(
                Text(node, LumuiProtocol.Fields.Start)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = !HasFalse(node, LumuiProtocol.Fields.Enabled)
        };
        DatePicker end = new DatePicker
        {
            SelectedDate = ParseDate(
                Text(node, LumuiProtocol.Fields.End)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = !HasFalse(node, LumuiProtocol.Fields.Enabled)
        };
        ApplyInput(start);
        ApplyInput(end);
        StackPanel fields = new StackPanel
        {
            Spacing = 12D,
            Children =
            {
                LabeledCompactControl(RendererText.StartDate, start),
                LabeledCompactControl(RendererText.EndDate, end)
            }
        };
        AutomationProperties.SetName(start, RendererText.StartDate);
        AutomationProperties.SetName(end, RendererText.EndDate);
        _inputs[id + ".start"] = () =>
            start.SelectedDate?.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture) ?? String.Empty;
        _inputs[id + ".end"] = () =>
            end.SelectedDate?.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture) ?? String.Empty;
        start.PropertyChanged += async (_, change) =>
        {
            if (change.Property == DatePicker.SelectedDateProperty)
            {
                await InvokeComponentActionAsync(node);
            }
        };
        end.PropertyChanged += async (_, change) =>
        {
            if (change.Property == DatePicker.SelectedDateProperty)
            {
                await InvokeComponentActionAsync(node);
            }
        };
        return Field(node, fields);
    }

    private static DateTimeOffset? ParseDate(String value)
    {
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out DateTimeOffset date)
                ? date
                : null;
    }

    private Control RenderCapability(JsonElement node)
    {
        StackPanel panel = new StackPanel { Spacing = 8 };
        String title = Text(
            node,
            LumuiProtocol.Fields.Label,
            Text(
                node,
                LumuiProtocol.Fields.Title,
                Text(
                    node,
                    LumuiProtocol.Fields.Kind,
                    RendererText.DeviceFunction)));
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush(_appearance.Text)
        });
        String description = Text(node, LumuiProtocol.Fields.Description, Text(node, LumuiProtocol.Fields.RouteSummary));
        if (description.Length > 0)
        {
            panel.Children.Add(Body(description));
        }
        String action = Text(node, LumuiProtocol.Fields.Action);
        if (action.Length > 0)
        {
            String componentId = Text(node, LumuiProtocol.Fields.Id);
            Button button = new Button
            {
                Content = title,
                IsEnabled = !HasFalse(node, LumuiProtocol.Fields.Enabled)
            };
            button.Classes.Add(BrowserStyleClasses.Primary);
            ApplyPrimaryButton(button);
            button.Click += async (_, _) =>
            {
                Dictionary<String, Object?> input = _inputs.ToDictionary(
                    (KeyValuePair<String, Func<Object?>> pair) => pair.Key,
                    (KeyValuePair<String, Func<Object?>> pair) => pair.Value(),
                    StringComparer.Ordinal);
                await _invoke(componentId, action, input);
            };
            panel.Children.Add(button);
        }
        else if (node.TryGetProperty(LumuiProtocol.Fields.Fallback, out JsonElement fallback) && fallback.ValueKind == JsonValueKind.Object)
        {
            panel.Children.Add(RenderNode(fallback));
        }
        else
        {
            panel.Children.Add(Body(RendererText.FunctionUnavailable));
        }
        Border border = new Border { Child = panel };
        border.Classes.Add(BrowserStyleClasses.Card);
        ApplyCard(border);
        return border;
    }

private Control RenderNavigationComponent(JsonElement node)
    {
        String summary = Text(
            node,
            LumuiProtocol.Fields.RouteSummary,
            "Walking route");
        StackPanel panel = CapabilityPanel(summary, String.Empty);
        Double latitude = 52.3669D;
        Double longitude = 4.9077D;
        String destinationLabel = "Destination";
        if (node.TryGetProperty("destination", out JsonElement destination)
            && destination.ValueKind == JsonValueKind.Object)
        {
            destinationLabel = Text(
                destination,
                LumuiProtocol.Fields.Label,
                destinationLabel);
            latitude = Number(destination, "latitude", latitude);
            longitude = Number(destination, "longitude", longitude);
        }

        Grid map = new Grid
        {
            Height = _settings.Profile.Kind is DeviceProfileKind.Phone
                or DeviceProfileKind.Watch
                    ? 150D
                    : 210D,
            Background = Brush(_appearance.SurfaceAlternate),
            ClipToBounds = true
        };
        map.Children.Add(CreateMapTileLayer(latitude, longitude, 15));
        map.Children.Add(new Border
        {
            Width = 28D,
            Height = 28D,
            CornerRadius = new CornerRadius(14D, 14D, 14D, 4D),
            Background = Brush(_brand.Accent),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = new RotateTransform(-45D),
            Child = new Border
            {
                Width = 9D,
                Height = 9D,
                CornerRadius = new CornerRadius(5D),
                Background = Brush(_appearance.Surface)
            }
        });
        panel.Children.Add(new Border
        {
            Child = map,
            CornerRadius = new CornerRadius(UsesLumiStyle ? 0D : 16D),
            BorderBrush = Brush(_appearance.Border),
            BorderThickness = new Thickness(1D)
        });
        panel.Children.Add(new TextBlock
        {
            Text = destinationLabel,
            FontSize = Font(20D),
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush(_appearance.Text),
            TextWrapping = TextWrapping.Wrap
        });

        if (node.TryGetProperty("current_step", out JsonElement currentStep)
            && currentStep.ValueKind == JsonValueKind.Object)
        {
            panel.Children.Add(RenderNavigationStep(currentStep, true));
        }
        if (node.TryGetProperty("maneuvers", out JsonElement maneuvers)
            && maneuvers.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement maneuver in maneuvers.EnumerateArray())
            {
                panel.Children.Add(RenderNavigationStep(maneuver, false));
            }
        }

        WrapPanel metrics = new WrapPanel
        {
            Margin = new Thickness(0D, 6D, 0D, 0D)
        };
        String remaining = DisplayProperty(node, "distance_remaining");
        if (remaining.Length > 0)
        {
            metrics.Children.Add(new TextBlock
            {
                Text = remaining + " m left",
                Margin = new Thickness(0D, 0D, 20D, 0D),
                Foreground = Brush(_appearance.Muted)
            });
        }
        String etaValue = Text(node, "eta");
        if (DateTimeOffset.TryParse(etaValue, out DateTimeOffset eta))
        {
            metrics.Children.Add(new TextBlock
            {
                Text = "Arrive " + eta.ToString("HH:mm", CultureInfo.CurrentCulture),
                Foreground = Brush(_appearance.Muted)
            });
        }
        if (metrics.Children.Count > 0)
        {
            panel.Children.Add(metrics);
        }
        return CapabilityCard(panel, "Route guidance");
    }

    private Control RenderNavigationStep(JsonElement step, Boolean current)
    {
        Grid row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 12D,
            Margin = new Thickness(0D, 4D)
        };
        Border marker = new Border
        {
            Width = 30D,
            Height = 30D,
            CornerRadius = new CornerRadius(15D),
            Background = Brush(current ? _appearance.Accent : _appearance.SurfaceAlternate),
            Child = new TextBlock
            {
                Text = current ? "1" : "·",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brush(current ? _appearance.AccentText : _appearance.Text),
                FontWeight = FontWeight.Bold
            }
        };
        row.Children.Add(marker);
        StackPanel content = new StackPanel { Spacing = 2D };
        content.Children.Add(new TextBlock
        {
            Text = Text(step, "instruction", "Continue"),
            Foreground = Brush(_appearance.Text),
            FontWeight = current ? FontWeight.SemiBold : FontWeight.Normal,
            TextWrapping = TextWrapping.Wrap
        });
        String distance = DisplayProperty(step, "distance");
        if (distance.Length > 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = distance + " m",
                Foreground = Brush(_appearance.Muted),
                FontSize = Font(13D)
            });
        }
        row.Children.Add(content);
        Grid.SetColumn(content, 1);
        return row;
    }

private Control RenderDialer(JsonElement node)
    {
        String id = Text(node, LumuiProtocol.Fields.Id);
        String number = Text(node, "number");
        TextBlock value = new TextBlock
        {
            Text = number.Length > 0 ? number : "Enter a number",
            FontSize = Font(28D),
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Brush(number.Length > 0
                ? _appearance.Text
                : _appearance.Muted),
            TextWrapping = TextWrapping.Wrap
        };
        Grid keypad = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
            ColumnSpacing = 10D,
            RowSpacing = 10D,
            MaxWidth = 340D,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        String[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "*", "0", "#" };
        Button call = new Button
        {
            Content = "Call",
            IsEnabled = number.Length > 0
        };
        ApplyPrimaryButton(call);
        void UpdateNumber()
        {
            value.Text = number.Length > 0 ? number : "Enter a number";
            value.Foreground = Brush(number.Length > 0
                ? _appearance.Text
                : _appearance.Muted);
            call.IsEnabled = number.Length > 0;
        }
        for (Int32 index = 0; index < keys.Length; index++)
        {
            String key = keys[index];
            Button digit = new Button
            {
                Content = key,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                MinHeight = 52D,
                FontSize = Font(19D)
            };
            ApplyLinkButton(digit);
            digit.Click += (_, _) =>
            {
                number += key;
                UpdateNumber();
            };
            keypad.Children.Add(digit);
            Grid.SetColumn(digit, index % 3);
            Grid.SetRow(digit, index / 3);
        }

        StackPanel panel = CapabilityPanel(
            Text(node, LumuiProtocol.Fields.Label, "Phone"),
            Text(node, LumuiProtocol.Fields.Description));
        panel.Children.Add(value);
        panel.Children.Add(keypad);
        Grid actions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 10D,
            MaxWidth = 340D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Button remove = new Button
        {
            Content = "Delete",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        ApplyLinkButton(remove);
        remove.Click += (_, _) =>
        {
            if (number.Length > 0)
            {
                number = number.Substring(0, number.Length - 1);
                UpdateNumber();
            }
        };
        actions.Children.Add(remove);
        String action = FirstAction(node);
        if (action.Length > 0)
        {
            call.HorizontalAlignment = HorizontalAlignment.Stretch;
            call.Click += async (_, _) => await _invoke(
                id,
                action,
                new Dictionary<String, Object?>(StringComparer.Ordinal)
                {
                    [id] = number
                });
            actions.Children.Add(call);
            Grid.SetColumn(call, 1);
        }
        panel.Children.Add(actions);
        _inputs[id] = () => number;
        return CapabilityCard(panel, "Phone dialer");
    }

    private Control RenderClock(JsonElement node)
    {
        String label = Text(node, LumuiProtocol.Fields.Label, "Local time");
        String value = Text(node, LumuiProtocol.Fields.Value);
        String timezone = Text(node, "timezone");
        DateTime displayedAt = DateTime.Now;
        if (value.Length == 0
            && node.TryGetProperty(LumuiProtocol.Fields.Fallback, out JsonElement fallback)
            && fallback.ValueKind == JsonValueKind.Object)
        {
            value = Text(
                fallback,
                LumuiProtocol.Fields.Text,
                DisplayProperty(fallback, LumuiProtocol.Fields.Value));
        }
        if (value.Length == 0)
        {
            if (timezone.Length > 0)
            {
                try
                {
                    TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(timezone);
                    displayedAt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
                }
                catch (TimeZoneNotFoundException)
                {
                    displayedAt = DateTime.Now;
                }
                catch (InvalidTimeZoneException)
                {
                    displayedAt = DateTime.Now;
                }
            }
            value = displayedAt.ToString("HH:mm", CultureInfo.CurrentCulture);
        }

        StackPanel panel = new StackPanel
        {
            Spacing = 8D,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        panel.Children.Add(new TextBlock
        {
            Text = label.ToUpperInvariant(),
            Foreground = Brush(_appearance.Accent),
            FontSize = Font(11D),
            FontWeight = FontWeight.Bold,
            LetterSpacing = 1.2D,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = Font(_settings.Profile.Kind == DeviceProfileKind.Watch
                ? 38D
                : 54D),
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush(_appearance.Text),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = timezone.Length > 0
                ? timezone
                : displayedAt.ToString("dddd, d MMMM", CultureInfo.CurrentCulture),
            Foreground = Brush(_appearance.Muted),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        Border card = new Border
        {
            Child = panel,
            Padding = new Thickness(32D, 26D),
            MinWidth = _settings.Profile.Kind == DeviceProfileKind.Watch
                ? 180D
                : 250D,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        card.Classes.Add(BrowserStyleClasses.Soft);
        _styler.ApplyComponentPanel(card, _brand.AccentTertiary);
        AutomationProperties.SetName(
            card,
            label + " " + value + (timezone.Length > 0 ? " " + timezone : String.Empty));
        return card;
    }

    private Control RenderFilePicker(JsonElement node, Boolean media)
    {
        String id = Text(node, LumuiProtocol.Fields.Id);
        String title = Text(
            node,
            LumuiProtocol.Fields.Label,
            media ? "Choose media" : "Choose a file");
        String description = Text(node, LumuiProtocol.Fields.Description);
        String action = Text(node, LumuiProtocol.Fields.Action);
        TextBlock selection = new TextBlock
        {
            Text = media ? "No photo selected" : "No document selected",
            Foreground = Brush(_appearance.Muted),
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        FontAwesomeIcon glyph = new FontAwesomeIcon
        {
            Icon = media ? BrowserIcons.Upload : BrowserIcons.Download,
            IconSize = 30D,
            Foreground = Brush(_appearance.Accent),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Button choose = new Button
        {
            Content = media ? "Choose photo" : "Choose document",
            HorizontalAlignment = HorizontalAlignment.Center,
            IsEnabled = !HasFalse(node, LumuiProtocol.Fields.Enabled)
        };
        choose.Classes.Add(BrowserStyleClasses.Primary);
        ApplyPrimaryButton(choose);
        AutomationProperties.SetName(choose, title);

        Border dropArea = new Border
        {
            Padding = new Thickness(24D),
            CornerRadius = new CornerRadius(UsesLumiStyle ? 0D : 16D),
            BorderBrush = Brush(_appearance.Border),
            BorderThickness = new Thickness(1D),
            Background = Brush(_appearance.SurfaceAlternate),
            Child = new StackPanel
            {
                Spacing = 10D,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    glyph,
                    selection,
                    choose
                }
            }
        };

        choose.Click += async (_, _) =>
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(choose);
            if (topLevel is null)
            {
                return;
            }
            IReadOnlyList<IStorageFile> files =
                await topLevel.StorageProvider.OpenFilePickerAsync(
                    new FilePickerOpenOptions
                    {
                        Title = title,
                        AllowMultiple = Text(
                            node,
                            "selection_mode",
                            "single") == "multiple"
                    });
            if (files.Count == 0)
            {
                return;
            }
            String[] names = files.Select(file => file.Name).ToArray();
            selection.Text = String.Join(Environment.NewLine, names);
            selection.Foreground = Brush(_appearance.Text);
            choose.Content = "Choose another";
            if (action.Length > 0)
            {
                await _invoke(
                    id,
                    action,
                    new Dictionary<String, Object?>(StringComparer.Ordinal)
                    {
                        [id] = names
                    });
            }
        };

        StackPanel panel = CapabilityPanel(title, description);
        panel.Children.Add(dropArea);
        return CapabilityCard(panel, title);
    }

private Control RenderContactPicker(JsonElement node)
    {
        String id = Text(node, LumuiProtocol.Fields.Id);
        String title = Text(
            node,
            LumuiProtocol.Fields.Label,
            "Choose a contact");
        String action = Text(node, LumuiProtocol.Fields.Action);
        String[] names =
        {
            "Alex Morgan",
            "Sam de Vries",
            "Robin Chen"
        };
        String selected = names[0];
        StackPanel choices = new StackPanel { Spacing = 8D };
        List<Button> buttons = new List<Button>();
        foreach (String name in names)
        {
            Button choice = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                    ColumnSpacing = 12D,
                    Children =
                    {
                        new Border
                        {
                            Width = 38D,
                            Height = 38D,
                            CornerRadius = new CornerRadius(19D),
                            Background = Brush(_appearance.SurfaceAlternate),
                            Child = new FontAwesomeIcon
                            {
                                Icon = BrowserIcons.User,
                                IconSize = 18D,
                                Foreground = Brush(_appearance.Accent),
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center
                            }
                        },
                        new TextBlock
                        {
                            Text = name,
                            Foreground = Brush(_appearance.Text),
                            FontWeight = FontWeight.SemiBold,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                }
            };
            Grid.SetColumn(((Grid)choice.Content).Children[1], 1);
            buttons.Add(choice);
            choices.Children.Add(choice);
            choice.Click += (_, _) =>
            {
                selected = name;
                foreach (Button candidate in buttons)
                {
                    ApplyLinkButton(candidate);
                }
                _styler.ApplyChoiceButton(choice, true);
            };
            ApplyLinkButton(choice);
        }
        _styler.ApplyChoiceButton(buttons[0], true);

        StackPanel panel = CapabilityPanel(
            title,
            Text(node, LumuiProtocol.Fields.Description));
        panel.Children.Add(choices);
        if (action.Length > 0)
        {
            panel.Children.Add(CapabilityActionButton(
                node,
                "Share selected contact",
                async () =>
                {
                    await _invoke(
                        id,
                        action,
                        new Dictionary<String, Object?>(StringComparer.Ordinal)
                        {
                            [id] = selected
                        });
                }));
        }
        return CapabilityCard(panel, title);
    }

private Control RenderLocationPicker(JsonElement node)
    {
        String id = Text(node, LumuiProtocol.Fields.Id);
        String action = Text(node, LumuiProtocol.Fields.Action);
        String title = Text(
            node,
            LumuiProtocol.Fields.Label,
            "Choose a location");
        Double latitudeValue = 52.3669D;
        Double longitudeValue = 4.9077D;

        Grid map = new Grid
        {
            Height = _settings.Profile.Kind is DeviceProfileKind.Phone
                or DeviceProfileKind.Watch
                    ? 160D
                    : 220D,
            Background = Brush(_appearance.SurfaceAlternate),
            ClipToBounds = true
        };
        map.Children.Add(CreateMapTileLayer(
            latitudeValue,
            longitudeValue,
            15));
        map.Children.Add(new FontAwesomeIcon
        {
            Icon = BrowserIcons.Location,
            IconSize = 34D,
            Foreground = Brush(_appearance.Accent),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });

        TextBox latitude = new TextBox
        {
            PlaceholderText = "Latitude",
            Text = latitudeValue.ToString(
                "F4",
                CultureInfo.InvariantCulture)
        };
        TextBox longitude = new TextBox
        {
            PlaceholderText = "Longitude",
            Text = longitudeValue.ToString(
                "F4",
                CultureInfo.InvariantCulture)
        };
        ApplyInput(latitude);
        ApplyInput(longitude);
        Grid coordinates = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 10D
        };
        coordinates.Children.Add(latitude);
        coordinates.Children.Add(longitude);
        Grid.SetColumn(longitude, 1);

        StackPanel panel = CapabilityPanel(
            title,
            Text(node, LumuiProtocol.Fields.Description));
        panel.Children.Add(new Border
        {
            Child = map,
            CornerRadius = new CornerRadius(UsesLumiStyle ? 0D : 16D),
            BorderBrush = Brush(_appearance.Border),
            BorderThickness = new Thickness(1D)
        });
        panel.Children.Add(coordinates);
        if (action.Length > 0)
        {
            panel.Children.Add(CapabilityActionButton(
                node,
                "Use this location",
                async () =>
                {
                    await _invoke(
                        id,
                        action,
                        new Dictionary<String, Object?>(StringComparer.Ordinal)
                        {
                            [id + ".latitude"] = latitude.Text,
                            [id + ".longitude"] = longitude.Text
                        });
                }));
        }
        return CapabilityCard(panel, title);
    }

    private Control RenderMap(JsonElement node)
    {
        String title = Text(
            node,
            LumuiProtocol.Fields.Label,
            "Map");
        Double latitude = 52.3676D;
        Double longitude = 4.9041D;
        if (node.TryGetProperty("center", out JsonElement center)
            && center.ValueKind == JsonValueKind.Object)
        {
            latitude = Number(center, "latitude", latitude);
            longitude = Number(center, "longitude", longitude);
        }
        Int32 zoom = Math.Clamp(
            (Int32)Number(node, "zoom", 15D),
            3,
            18);
        Grid map = new Grid
        {
            Height = _settings.Profile.Kind is DeviceProfileKind.Phone
                or DeviceProfileKind.Watch
                    ? 230D
                    : 320D,
            Background = Brush(_appearance.SurfaceAlternate),
            ClipToBounds = true
        };
        ContentControl tiles = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        map.Children.Add(tiles);
        Border marker = new Border
        {
            Width = 34D,
            Height = 34D,
            CornerRadius = new CornerRadius(17D, 17D, 17D, 4D),
            Background = Brush(_brand.Accent),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = new RotateTransform(-45D),
            Child = new Border
            {
                Width = 12D,
                Height = 12D,
                CornerRadius = new CornerRadius(6D),
                Background = Brush(_appearance.Surface)
            }
        };
        map.Children.Add(marker);
        Grid zoomControls = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto"),
            ColumnSpacing = 8D,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(12D)
        };
        Button zoomOut = IconButton(
            BrowserIcons.ZoomOut,
            "Zoom out",
            zoom > 3);
        Button zoomIn = IconButton(
            BrowserIcons.ZoomIn,
            "Zoom in",
            zoom < 18);
        zoomControls.Children.Add(zoomOut);
        zoomControls.Children.Add(zoomIn);
        Grid.SetColumn(zoomIn, 1);
        map.Children.Add(zoomControls);
        void UpdateMap()
        {
            tiles.Content = CreateMapTileLayer(
                latitude,
                longitude,
                zoom);
            zoomOut.IsEnabled = zoom > 3;
            zoomIn.IsEnabled = zoom < 18;
        }
        zoomOut.Click += (_, _) =>
        {
            zoom = Math.Max(3, zoom - 1);
            UpdateMap();
        };
        zoomIn.Click += (_, _) =>
        {
            zoom = Math.Min(18, zoom + 1);
            UpdateMap();
        };
        UpdateMap();
        Border mapFrame = new Border
        {
            Child = map,
            CornerRadius = new CornerRadius(UsesLumiStyle ? 0D : 18D),
            BorderBrush = Brush(_appearance.Border),
            BorderThickness = new Thickness(1D)
        };
        String address = String.Format(
            CultureInfo.InvariantCulture,
            "https://www.openstreetmap.org/?mlat={0}&mlon={1}#map=15/{0}/{1}",
            latitude,
            longitude);
        Button open = new Button
        {
            Content = "Open in OpenStreetMap",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        open.Classes.Add(BrowserStyleClasses.Link);
        ApplyLinkButton(open);
        open.Click += async (_, _) =>
        {
            if (Uri.TryCreate(address, UriKind.Absolute, out Uri? uri))
            {
                await _openExternal(uri);
            }
        };
        StackPanel panel = CapabilityPanel(
            title,
            Text(node, LumuiProtocol.Fields.Description));
        panel.Children.Add(mapFrame);
        panel.Children.Add(Body(
            String.Format(
                CultureInfo.InvariantCulture,
                "{0:F4}, {1:F4}",
                latitude,
                longitude)));
        panel.Children.Add(open);
        panel.Children.Add(Body("Map data © OpenStreetMap contributors"));
        return CapabilityCard(panel, title);
    }

    private Control CreateMapTileLayer(
        Double latitude,
        Double longitude,
        Int32 zoom)
    {
        Double size = Math.Pow(2D, zoom);
        Double x = ((longitude + 180D) / 360D) * size;
        Double latitudeRadians = latitude * Math.PI / 180D;
        Double y = (1D - Math.Asinh(Math.Tan(latitudeRadians)) / Math.PI)
            * 0.5D
            * size;
        Int32 centerX = (Int32)Math.Floor(x);
        Int32 centerY = (Int32)Math.Floor(y);
        Grid layer = new Grid
        {
            Width = 768D,
            Height = 768D,
            ColumnDefinitions = new ColumnDefinitions("256,256,256"),
            RowDefinitions = new RowDefinitions("256,256,256"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = new TranslateTransform(
                128D - ((x - centerX) * 256D),
                128D - ((y - centerY) * 256D))
        };
        Int32 tileLimit = (Int32)size;
        for (Int32 row = 0; row < 3; row++)
        {
            for (Int32 column = 0; column < 3; column++)
            {
                Int32 tileX = (centerX + column - 1 + tileLimit)
                    % tileLimit;
                Int32 tileY = Math.Clamp(
                    centerY + row - 1,
                    0,
                    tileLimit - 1);
                ContentControl tile = new ContentControl
                {
                    Width = 256D,
                    Height = 256D,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch
                };
                layer.Children.Add(tile);
                Grid.SetColumn(tile, column);
                Grid.SetRow(tile, row);
                Uri uri = new Uri(
                    "https://tile.openstreetmap.org/"
                    + zoom.ToString(CultureInfo.InvariantCulture)
                    + "/"
                    + tileX.ToString(CultureInfo.InvariantCulture)
                    + "/"
                    + tileY.ToString(CultureInfo.InvariantCulture)
                    + ".png");
                _ = _assetLoader.LoadMapTileAsync(tile, uri);
            }
        }
        return layer;
    }

private Control RenderGraphic(JsonElement node)
    {
        String title = Text(
            node,
            LumuiProtocol.Fields.Label,
            "Graphic");
        String purpose = Text(node, "purpose");
        String source = Text(node, LumuiProtocol.Fields.Source);
        Uri? uri = ResolveUri(source, allowExternal: true);
        if (uri is null)
        {
            return RenderFallback(node);
        }

        ContentControl image = new ContentControl
        {
            Height = _settings.Profile.Kind switch
            {
                DeviceProfileKind.Watch => 130D,
                DeviceProfileKind.Phone => 240D,
                _ => 380D
            },
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _ = _assetLoader.LoadAsync(image, uri, "image/svg+xml");
        Border frame = new Border
        {
            Child = image,
            CornerRadius = new CornerRadius(UsesLumiStyle ? 0D : 18D),
            BorderBrush = Brush(_appearance.Border),
            BorderThickness = new Thickness(1D),
            Background = Brush(_appearance.SurfaceAlternate),
            ClipToBounds = true
        };

        StackPanel panel = new StackPanel { Spacing = 12D };
        panel.Children.Add(frame);
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = Font(20D),
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush(_appearance.Text),
            TextWrapping = TextWrapping.Wrap
        });
        if (purpose.Length > 0)
        {
            panel.Children.Add(Body(purpose));
        }
        Border card = new Border
        {
            Child = panel,
            Padding = new Thickness(18D)
        };
        card.Classes.Add(BrowserStyleClasses.Soft);
        _styler.ApplyComponentPanel(card, _brand.AccentTertiary);
        AutomationProperties.SetName(card, title);
        return card;
    }

    private Control GraphicStep(
        String heading,
        String detail,
        Int32 index)
    {
        StackPanel content = new StackPanel
        {
            Spacing = 6D,
            Children =
            {
                new TextBlock
                {
                    Text = heading,
                    Foreground = Brush(_appearance.Text),
                    FontWeight = FontWeight.Bold,
                    FontSize = Font(18D),
                    TextAlignment = TextAlignment.Center
                },
                new TextBlock
                {
                    Text = detail,
                    Foreground = Brush(_appearance.Muted),
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center
                }
            }
        };
        Border card = new Border
        {
            Child = content,
            MinHeight = 130D,
            Padding = new Thickness(16D),
            VerticalAlignment = VerticalAlignment.Stretch
        };
        card.Classes.Add(BrowserStyleClasses.Soft);
        ApplySoftPanel(card);
        _styler.ApplyTileAccent(card, index + 1);
        return card;
    }

    private StackPanel CapabilityPanel(String title, String description)
    {
        StackPanel panel = new StackPanel
        {
            Spacing = 10D
        };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            FontSize = Font(20D),
            Foreground = Brush(_appearance.Text),
            TextWrapping = TextWrapping.Wrap
        });
        if (description.Length > 0)
        {
            panel.Children.Add(Body(description));
        }
        return panel;
    }

    private Control CapabilityCard(StackPanel panel, String title)
    {
        Border card = new Border
        {
            Child = panel,
            MaxWidth = 760D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        card.Classes.Add(BrowserStyleClasses.Card);
        _styler.ApplyComponentPanel(card, _brand.Accent);
        AutomationProperties.SetName(card, title);
        return card;
    }

    private Button CapabilityActionButton(
        JsonElement node,
        String label,
        Func<Task> action)
    {
        Button button = new Button
        {
            Content = label,
            HorizontalAlignment = HorizontalAlignment.Left,
            IsEnabled = !HasFalse(node, LumuiProtocol.Fields.Enabled)
        };
        button.Classes.Add(BrowserStyleClasses.Primary);
        ApplyPrimaryButton(button);
        AutomationProperties.SetName(button, label);
        button.Click += async (_, _) => await action();
        return button;
    }

    private Control RenderLink(JsonElement node)
    {
        String label = Text(
            node,
            LumuiProtocol.Fields.Label,
            RendererText.Open);
        String description = Text(node, LumuiProtocol.Fields.Description);
        String href = Text(node, LumuiProtocol.Fields.Href);
        Boolean download = Boolean(node, LumuiProtocol.Fields.Download);
        Boolean external = Boolean(node, LumuiProtocol.Fields.External);
        StackPanel textContent = new StackPanel { Spacing = 3 };
        textContent.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush(_appearance.Text),
            TextWrapping = TextWrapping.Wrap
        });
        if (description.Length > 0)
        {
            textContent.Children.Add(new TextBlock
            {
                Text = description,
                Foreground = Brush(_appearance.Muted),
                TextWrapping = TextWrapping.Wrap
            });
        }
        Grid content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 14D
        };
        content.Children.Add(textContent);
        Control arrow = new Border
            {
                Width = 36D,
                Height = 36D,
                CornerRadius = new CornerRadius(18D),
                Background = Brush(_appearance.SurfaceAlternate),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = download ? "↓" : external ? "↗" : "→",
                    Foreground = Brush(_appearance.Accent),
                    FontSize = Font(17D),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        content.Children.Add(arrow);
        Grid.SetColumn(arrow, 1);
        Button button = new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        AutomationProperties.SetName(button, label);
        AutomationProperties.SetHelpText(button, description);
        AutomationProperties.SetAutomationId(button, Text(node, LumuiProtocol.Fields.Id));
        button.Classes.Add(BrowserStyleClasses.Link);
        ApplyLinkButton(button);
        if (UsesWideSemanticLayout)
        {
            button.Padding = new Thickness(20D);
            button.CornerRadius = new CornerRadius(
                UsesLumiStyle ? 0D : 14D);
            button.MinHeight = 96D;
        }
        if (href.Length == 0)
        {
            button.IsEnabled = false;
            return button;
        }
        Uri? uri = ResolveUri(href, external);
        if (uri is null)
        {
            button.IsEnabled = false;
            return button;
        }
        button.Click += async (_, _) =>
        {
            if (download)
            {
                await _download(uri);
            }
            else if (external)
            {
                await _openExternal(uri);
            }
            else
            {
                await _navigate(uri);
            }
        };
        return button;
    }

private Control RenderButton(JsonElement node)
    {
        String componentId = Text(node, LumuiProtocol.Fields.Id);
        String actionId = Text(node, LumuiProtocol.Fields.Action);
        String label = Text(
            node,
            LumuiProtocol.Fields.Label,
            RendererText.Action);
        String iconName = Text(node, "icon");
        Object content = label;
        if (iconName.Length > 0)
        {
            content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 9D,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new FontAwesomeIcon
                    {
                        Icon = SemanticIcon(iconName),
                        IconSize = 16D,
                        Foreground = Brush(_appearance.AccentText),
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = label,
                        Foreground = Brush(_appearance.AccentText),
                        FontWeight = FontWeight.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            };
        }
        Button button = new Button
        {
            Content = content,
            IsEnabled = actionId.Length > 0
                && !HasFalse(node, LumuiProtocol.Fields.Enabled),
            MinWidth = UsesWideSemanticLayout ? 160D : 0D
        };
        AutomationProperties.SetName(button, label);
        AutomationProperties.SetAutomationId(button, componentId);
        button.Classes.Add(BrowserStyleClasses.Primary);
        ApplyPrimaryButton(button);
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            button.Opacity = 0.76D;
            try
            {
                await InvokeComponentActionAsync(node);
            }
            finally
            {
                if (!_disposed)
                {
                    button.IsEnabled = !HasFalse(
                        node,
                        LumuiProtocol.Fields.Enabled);
                    button.Opacity = 1D;
                }
            }
        };
        return button;
    }

private Control RenderTextInput(JsonElement node)
    {
        String id = Text(node, LumuiProtocol.Fields.Id);
        String kind = Text(node, LumuiProtocol.Fields.Kind);
        if (kind == LumuiProtocol.ComponentKinds.NumberField)
        {
            return RenderNumberInput(node);
        }
        if (kind == LumuiProtocol.ComponentKinds.DateField)
        {
            return RenderDateInput(node);
        }
        if (kind == LumuiProtocol.ComponentKinds.TimeField)
        {
            return RenderTimeInput(node);
        }
        if (kind == LumuiProtocol.ComponentKinds.DateTimeField)
        {
            return RenderDateTimeInput(node);
        }
        if (kind == LumuiProtocol.ComponentKinds.ColorField)
        {
            return RenderColorInput(node);
        }
        if (kind == LumuiProtocol.ComponentKinds.OtpField)
        {
            return RenderOtpInput(node);
        }
        if (kind == LumuiProtocol.ComponentKinds.PasswordField)
        {
            return RenderPasswordInput(node);
        }
        if (kind == LumuiProtocol.ComponentKinds.SearchField)
        {
            return RenderSearchInput(node);
        }
        Int32 maximum = Math.Max(
            0,
            (Int32)Number(node, "max_length"));
        TextBox input = new TextBox
        {
            Text = _inputSuggestion?.Invoke(node)
                ?? Text(node, LumuiProtocol.Fields.Value),
            PlaceholderText = Text(node, LumuiProtocol.Fields.Placeholder),
            IsReadOnly = Boolean(node, LumuiProtocol.Fields.Readonly),
            IsEnabled = !HasFalse(node, LumuiProtocol.Fields.Enabled),
            MaxLength = maximum > 0 ? maximum : Int32.MaxValue
        };
        _inputs[id] = () => input.Text ?? String.Empty;
        input.LostFocus += async (_, _) =>
            await InvokeComponentActionAsync(node);
        input.KeyUp += async (_, args) =>
        {
            if (args.Key == Avalonia.Input.Key.Enter)
            {
                await InvokeComponentActionAsync(node);
            }
        };
        return Field(node, input);
    }

private Control RenderNumberInput(JsonElement node)
    {
        String id = Text(node, LumuiProtocol.Fields.Id);
        Decimal minimum = node.TryGetProperty(
                LumuiProtocol.Fields.Min,
                out JsonElement minimumValue)
            && minimumValue.TryGetDecimal(out Decimal parsedMinimum)
                ? parsedMinimum
                : Decimal.MinValue;
        Decimal maximum = node.TryGetProperty(
                LumuiProtocol.Fields.Max,
                out JsonElement maximumValue)
            && maximumValue.TryGetDecimal(out Decimal parsedMaximum)
                ? parsedMaximum
                : Decimal.MaxValue;
        NumericUpDown input = new NumericUpDown
        {
            Minimum = minimum,
            Maximum = maximum,
            Increment = (Decimal)Math.Max(
                0.0001D,
                Number(node, LumuiProtocol.Fields.Step, 1D)),
            Value = (Decimal)Number(node, LumuiProtocol.Fields.Value),
            IsReadOnly = Boolean(node, LumuiProtocol.Fields.Readonly),
            IsEnabled = !HasFalse(node, LumuiProtocol.Fields.Enabled),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        String unit = Text(node, LumuiProtocol.Fields.Unit);
        Control control = input;
        if (unit.Length > 0)
        {
            TextBlock unitLabel = new TextBlock
            {
                Text = unit,
                Foreground = Brush(_appearance.Muted),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 10D,
                Children = { input, unitLabel }
            };
            Grid.SetColumn(unitLabel, 1);
            control = row;
        }
        _inputs[id] = () => input.Value;
        input.LostFocus += async (_, _) =>
            await InvokeComponentActionAsync(node);
        input.KeyUp += async (_, args) =>
        {
            if (args.Key == Avalonia.Input.Key.Enter)
            {
                await InvokeComponentActionAsync(node);
            }
        };
        return Field(node, control);
    }

private Control RenderDateInput(JsonElement node)
    {
        String id = Text(node, LumuiProtocol.Fields.Id);
        DatePicker input = new DatePicker
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = !HasFalse(node, LumuiProtocol.Fields.Enabled)
        };
        if (DateTimeOffset.TryParse(
                Text(node, LumuiProtocol.Fields.Value),
                out DateTimeOffset value))
        {
            input.SelectedDate = value;
        }
        if (DateTimeOffset.TryParse(
                Text(node, LumuiProtocol.Fields.Min),
                out DateTimeOffset minimum))
        {
            input.MinYear = minimum;
        }
        if (DateTimeOffset.TryParse(
                Text(node, LumuiProtocol.Fields.Max),
                out DateTimeOffset maximum))
        {
            input.MaxYear = maximum;
        }
        _inputs[id] = () => input.SelectedDate?.ToString(
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture);
        input.PropertyChanged += async (_, change) =>
        {
            if (change.Property == DatePicker.SelectedDateProperty)
            {
                await InvokeComponentActionAsync(node);
            }
        };
        return Field(node, input);
    }

private Control RenderTimeInput(JsonElement node)
    {
        String id = Text(node, LumuiProtocol.Fields.Id);
        TimePicker input = new TimePicker
        {
            ClockIdentifier = "24HourClock",
            MinuteIncrement = Math.Max(
                1,
                (Int32)Number(node, "step_minutes", 1D)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = !HasFalse(node, LumuiProtocol.Fields.Enabled)
        };
        if (TimeSpan.TryParse(
                Text(node, LumuiProtocol.Fields.Value),
                CultureInfo.InvariantCulture,
                out TimeSpan value))
        {
            input.SelectedTime = value;
        }
        _inputs[id] = () => input.SelectedTime?.ToString(
            @"hh\:mm",
            CultureInfo.InvariantCulture);
        input.PropertyChanged += async (_, change) =>
        {
            if (change.Property == TimePicker.SelectedTimeProperty)
            {
                await InvokeComponentActionAsync(node);
            }
        };
        return Field(node, input);
    }

private Control RenderDateTimeInput(JsonElement node)
    {
        String id = Text(node, LumuiProtocol.Fields.Id);
        String raw = Text(node, LumuiProtocol.Fields.Value);
        DateTime.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out DateTime parsed);
        DatePicker date = new DatePicker
        {
            SelectedDate = parsed == default
                ? null
                : new DateTimeOffset(parsed),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = !HasFalse(node, LumuiProtocol.Fields.Enabled)
        };
        TimePicker time = new TimePicker
        {
            ClockIdentifier = "24HourClock",
            SelectedTime = parsed == default ? null : parsed.TimeOfDay,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = !HasFalse(node, LumuiProtocol.Fields.Enabled)
        };
        ApplyInput(date);
        ApplyInput(time);
        Control inputs;
        if (_settings.Profile.Kind is
            DeviceProfileKind.Phone or DeviceProfileKind.Watch)
        {
            inputs = new StackPanel
            {
                Spacing = 9D,
                Children =
                {
                    LabeledCompactControl("Date", date),
                    LabeledCompactControl("Time", time)
                }
            };
        }
        else
        {
            Control dateField = LabeledCompactControl("Date", date);
            Control timeField = LabeledCompactControl("Time", time);
            Grid grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*"),
                ColumnSpacing = 12D,
                Children = { dateField, timeField }
            };
            Grid.SetColumn(timeField, 1);
            inputs = grid;
        }
        _inputs[id] = () =>
        {
            if (date.SelectedDate is null || time.SelectedTime is null)
            {
                return null;
            }
            return date.SelectedDate.Value.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture)
                + "T"
                + time.SelectedTime.Value.ToString(
                    @"hh\:mm",
                    CultureInfo.InvariantCulture);
        };
        date.PropertyChanged += async (_, change) =>
        {
            if (change.Property == DatePicker.SelectedDateProperty)
            {
                await InvokeComponentActionAsync(node);
            }
        };
        time.PropertyChanged += async (_, change) =>
        {
            if (change.Property == TimePicker.SelectedTimeProperty)
            {
                await InvokeComponentActionAsync(node);
            }
        };
        return Field(node, inputs);
    }

private Control RenderColorInput(JsonElement node)
    {
        String id = Text(node, LumuiProtocol.Fields.Id);
        String value = Text(node, LumuiProtocol.Fields.Value, "#006E63");
        Border preview = new Border
        {
            Width = 48D,
            Height = 48D,
            CornerRadius = new CornerRadius(UsesLumiStyle ? 0D : 14D),
            BorderBrush = Brush(_appearance.Border),
            BorderThickness = new Thickness(1D)
        };
        TextBox text = new TextBox
        {
            Text = value,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsReadOnly = HasFalse(node, "allow_custom")
        };
        ApplyInput(text);
        List<(Button Button, String Value)> swatches =
            new List<(Button Button, String Value)>();
        void Refresh()
        {
            preview.Background = Color.TryParse(
                text.Text,
                out Color color)
                    ? new SolidColorBrush(color)
                    : Brush(_appearance.SurfaceAlternate);
            foreach ((Button button, String colorValue) in swatches)
            {
                button.BorderBrush = Brush(
                    String.Equals(
                        text.Text,
                        colorValue,
                        StringComparison.OrdinalIgnoreCase)
                            ? _appearance.Text
                            : _appearance.Border);
                button.BorderThickness = new Thickness(
                    String.Equals(
                        text.Text,
                        colorValue,
                        StringComparison.OrdinalIgnoreCase)
                            ? 3D
                            : 1D);
            }
        }
        text.TextChanged += (_, _) => Refresh();
        text.KeyUp += async (_, args) =>
        {
            if (args.Key == Avalonia.Input.Key.Enter)
            {
                await InvokeComponentActionAsync(node);
            }
        };
        text.LostFocus += async (_, _) =>
            await InvokeComponentActionAsync(node);
        WrapPanel palette = new WrapPanel();
        if (node.TryGetProperty("palette", out JsonElement colors)
            && colors.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in colors.EnumerateArray())
            {
                String colorValue = Display(item);
                Button swatch = new Button
                {
                    Width = 42D,
                    Height = 42D,
                    Margin = new Thickness(0D, 0D, 9D, 9D),
                    Background = Color.TryParse(
                        colorValue,
                        out Color color)
                            ? new SolidColorBrush(color)
                            : Brush(_appearance.SurfaceAlternate),
                    BorderBrush = Brush(_appearance.Border),
                    BorderThickness = new Thickness(1D),
                    CornerRadius = new CornerRadius(UsesLumiStyle ? 0D : 12D)
                };
                AutomationProperties.SetName(swatch, colorValue);
                swatch.Click += async (_, _) =>
                {
                    text.Text = colorValue;
                    Refresh();
                    await InvokeComponentActionAsync(node);
                };
                swatches.Add((swatch, colorValue));
                palette.Children.Add(swatch);
            }
        }
        Grid row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 11D,
            Children = { preview, text }
        };
        Grid.SetColumn(text, 1);
        StackPanel content = new StackPanel
        {
            Spacing = 10D,
            Children = { row, palette }
        };
        _inputs[id] = () => text.Text ?? String.Empty;
        Refresh();
        return Field(node, content);
    }

private Control RenderOtpInput(JsonElement node)
    {
        String id = Text(node, LumuiProtocol.Fields.Id);
        Int32 length = Math.Clamp(
            (Int32)Number(node, "length", 6D),
            4,
            10);
        Boolean autoSubmit = Boolean(node, "auto_submit");
        List<TextBox> boxes = new List<TextBox>();
        Grid inputs = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(
                String.Join(",", Enumerable.Repeat("*", length))),
            ColumnSpacing = _settings.Profile.Kind
                == DeviceProfileKind.Phone
                    ? 4D
                    : 8D
        };
        async Task SubmitIfReadyAsync()
        {
            if (boxes.All(box => box.Text?.Length == 1)
                && autoSubmit)
            {
                await InvokeComponentActionAsync(node);
            }
        }
        for (Int32 index = 0; index < length; index++)
        {
            TextBox box = new TextBox
            {
                MaxLength = 1,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                FontWeight = FontWeight.Bold,
                FontSize = Font(18D),
                IsEnabled = !HasFalse(node, LumuiProtocol.Fields.Enabled)
            };
            ApplyInput(box);
            Int32 nextIndex = index + 1;
            box.TextChanged += async (_, _) =>
            {
                if (!String.IsNullOrEmpty(box.Text)
                    && nextIndex < boxes.Count)
                {
                    boxes[nextIndex].Focus();
                }
                await SubmitIfReadyAsync();
            };
            box.KeyUp += async (_, args) =>
            {
                if (args.Key == Avalonia.Input.Key.Enter)
                {
                    await InvokeComponentActionAsync(node);
                }
            };
            boxes.Add(box);
            inputs.Children.Add(box);
            Grid.SetColumn(box, index);
        }
        _inputs[id] = () => String.Concat(
            boxes.Select(box => box.Text ?? String.Empty));
        return Field(node, inputs);
    }

private Control RenderPasswordInput(JsonElement node)
    {
        String id = Text(node, LumuiProtocol.Fields.Id);
        TextBox input = new TextBox
        {
            Text = _inputSuggestion?.Invoke(node) ?? String.Empty,
            PlaceholderText = Text(node, LumuiProtocol.Fields.Placeholder),
            PasswordChar = '●',
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = !HasFalse(node, LumuiProtocol.Fields.Enabled),
            MaxLength = Math.Max(
                1,
                (Int32)Number(node, "max_length", 128D))
        };
        ApplyInput(input);
        ToggleButton reveal = new ToggleButton
        {
            Content = "Show",
            IsVisible = !HasFalse(node, "allow_reveal"),
            Margin = new Thickness(8D, 0D, 0D, 0D)
        };
        ApplySegmentButton(reveal);
        reveal.Click += (_, _) =>
        {
            Boolean visible = reveal.IsChecked == true;
            input.PasswordChar = visible ? '\0' : '●';
            reveal.Content = visible ? "Hide" : "Show";
        };
        input.KeyUp += async (_, args) =>
        {
            if (args.Key == Avalonia.Input.Key.Enter)
            {
                await InvokeComponentActionAsync(node);
            }
        };
        Grid row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { input, reveal }
        };
        Grid.SetColumn(reveal, 1);
        StackPanel content = new StackPanel
        {
            Spacing = 9D,
            Children = { row }
        };
        if (node.TryGetProperty("rules", out JsonElement rules)
            && rules.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement rule in rules.EnumerateArray())
            {
                content.Children.Add(new TextBlock
                {
                    Text = "• " + Display(rule),
                    Foreground = Brush(_appearance.Muted),
                    FontSize = Font(13D),
                    TextWrapping = TextWrapping.Wrap
                });
            }
        }
        _inputs[id] = () => input.Text ?? String.Empty;
        return Field(node, content);
    }

private Control RenderSearchInput(JsonElement node)
    {
        String id = Text(node, LumuiProtocol.Fields.Id);
        TextBox input = new TextBox
        {
            Text = Text(node, LumuiProtocol.Fields.Value),
            PlaceholderText = Text(
                node,
                LumuiProtocol.Fields.Placeholder,
                "Search"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = !HasFalse(node, LumuiProtocol.Fields.Enabled)
        };
        ApplyInput(input);
        Button clear = IconButton(
            BrowserIcons.Close,
            "Clear search",
            input.Text?.Length > 0);
        clear.Click += async (_, _) =>
        {
            input.Text = String.Empty;
            input.Focus();
            await InvokeComponentActionAsync(
                node,
                Text(node, "clear_action", Text(
                    node,
                    LumuiProtocol.Fields.Action)));
        };
        input.TextChanged += (_, _) =>
            clear.IsEnabled = input.Text?.Length > 0;
        input.KeyUp += async (_, args) =>
        {
            if (args.Key == Avalonia.Input.Key.Enter)
            {
                await InvokeComponentActionAsync(
                    node,
                    Text(node, "submit_action", Text(
                        node,
                        LumuiProtocol.Fields.Action)));
            }
        };
        Grid row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8D,
            Children = { input, clear }
        };
        Grid.SetColumn(clear, 1);
        StackPanel content = new StackPanel
        {
            Spacing = 9D,
            Children = { row }
        };
        if (node.TryGetProperty(
                "suggestions",
                out JsonElement suggestions)
            && suggestions.ValueKind == JsonValueKind.Array)
        {
            WrapPanel suggestionsPanel = new WrapPanel();
            foreach (JsonElement suggestion in suggestions.EnumerateArray())
            {
                String value = Text(
                    suggestion,
                    LumuiProtocol.Fields.Value,
                    Display(suggestion));
                Button suggestionButton = new Button
                {
                    Content = Text(
                        suggestion,
                        LumuiProtocol.Fields.Label,
                        value),
                    Margin = new Thickness(0D, 0D, 8D, 8D)
                };
                ApplyLinkButton(suggestionButton);
                suggestionButton.Click += async (_, _) =>
                {
                    input.Text = value;
                    await InvokeComponentActionAsync(node);
                };
                suggestionsPanel.Children.Add(suggestionButton);
            }
            content.Children.Add(suggestionsPanel);
        }
        String resultCount = DisplayProperty(node, "result_count");
        if (resultCount.Length > 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = resultCount + " results",
                Foreground = Brush(_appearance.Muted),
                FontSize = Font(13D)
            });
        }
        _inputs[id] = () => input.Text ?? String.Empty;
        return Field(node, content);
    }

private Control RenderTextArea(JsonElement node)
    {
        String id = Text(node, LumuiProtocol.Fields.Id);
        Int32 maximum = Math.Max(
            0,
            (Int32)Number(node, "max_length"));
        TextBox input = new TextBox
        {
            Text = Text(node, LumuiProtocol.Fields.Value),
            PlaceholderText = Text(node, LumuiProtocol.Fields.Placeholder),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 132D,
            IsReadOnly = Boolean(node, LumuiProtocol.Fields.Readonly),
            IsEnabled = !HasFalse(node, LumuiProtocol.Fields.Enabled),
            MaxLength = maximum > 0 ? maximum : Int32.MaxValue
        };
        TextBlock count = new TextBlock
        {
            Foreground = Brush(_appearance.Muted),
            FontSize = Font(13D),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsVisible = maximum > 0
        };
        void UpdateCount()
        {
            count.Text = (input.Text?.Length ?? 0).ToString(
                    CultureInfo.CurrentCulture)
                + " of "
                + maximum.ToString(CultureInfo.CurrentCulture);
        }
        input.TextChanged += (_, _) => UpdateCount();
        input.LostFocus += async (_, _) =>
            await InvokeComponentActionAsync(node);
        UpdateCount();
        StackPanel content = new StackPanel
        {
            Spacing = 6D,
            Children = { input, count }
        };
        _inputs[id] = () => input.Text ?? String.Empty;
        return Field(node, content);
    }

private Control RenderCheck(JsonElement node)
    {
        String id = Text(node, LumuiProtocol.Fields.Id);
        String kind = Text(node, LumuiProtocol.Fields.Kind);
        String label = Text(
            node,
            LumuiProtocol.Fields.Label,
            RendererText.Option);
        String description = Text(node, LumuiProtocol.Fields.Description);
        Boolean initial = Boolean(node, LumuiProtocol.Fields.Value);
        if (kind == LumuiProtocol.ComponentKinds.Toggle)
        {
            StackPanel narrative = ChoiceNarrative(label, description);
            ToggleSwitch toggle = new ToggleSwitch
            {
                IsChecked = initial,
                IsEnabled = !HasFalse(node, LumuiProtocol.Fields.Enabled),
                OnContent = "On",
                OffContent = "Off",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Control content;
            if (_settings.Profile.Kind == DeviceProfileKind.Watch)
            {
                content = new StackPanel
                {
                    Spacing = 10D,
                    Children = { narrative, toggle }
                };
                toggle.HorizontalAlignment = HorizontalAlignment.Left;
            }
            else
            {
                Grid layout = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    ColumnSpacing = 16D,
                    Children = { narrative, toggle }
                };
                Grid.SetColumn(toggle, 1);
                content = layout;
            }
            Border panel = new Border { Child = content };
            _styler.ApplyChoiceCard(panel, initial);
            AutomationProperties.SetName(toggle, label);
            AutomationProperties.SetAutomationId(toggle, id);
            AutomationProperties.SetHelpText(toggle, description);
            _inputs[id] = () => toggle.IsChecked == true;
            toggle.Click += async (_, _) =>
            {
                _styler.ApplyChoiceCard(panel, toggle.IsChecked == true);
                await InvokeComponentActionAsync(node);
            };
            return panel;
        }
        StackPanel checkboxContent = ChoiceNarrative(label, description);
        CheckBox check = new CheckBox
        {
            Content = checkboxContent,
            IsChecked = initial,
            IsEnabled = !HasFalse(node, LumuiProtocol.Fields.Enabled),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Border card = new Border { Child = check };
        _styler.ApplyChoiceCard(card, initial);
        AutomationProperties.SetName(check, label);
        AutomationProperties.SetAutomationId(check, id);
        AutomationProperties.SetHelpText(check, description);
        AutomationProperties.SetIsRequiredForForm(
            check,
            Boolean(node, LumuiProtocol.Fields.Required));
        _inputs[id] = () => check.IsChecked == true;
        check.Click += async (_, _) =>
        {
            _styler.ApplyChoiceCard(card, check.IsChecked == true);
            await InvokeComponentActionAsync(node);
        };
        return card;
    }

    private Control ChoicePanel(
        JsonElement node,
        Control choice,
        String label)
    {
        StackPanel content = new StackPanel
        {
            Spacing = 8D,
            Children = { choice }
        };
        String description = Text(node, LumuiProtocol.Fields.Description);
        if (description.Length > 0)
        {
            content.Children.Add(Body(description));
        }
        return ComponentPanel(content, label);
    }

private Control RenderChoice(JsonElement node)
    {
        String id = Text(node, LumuiProtocol.Fields.Id);
        String kind = Text(node, LumuiProtocol.Fields.Kind);
        String current = Text(node, LumuiProtocol.Fields.Value);
        Boolean enabled = !HasFalse(node, LumuiProtocol.Fields.Enabled);
        if (kind == LumuiProtocol.ComponentKinds.RadioGroup)
        {
            List<(RadioButton Control, Border Card, String Value)> entries =
                new List<(RadioButton Control, Border Card, String Value)>();
            StackPanel choices = new StackPanel { Spacing = 8D };
            if (node.TryGetProperty(
                    LumuiProtocol.Fields.Options,
                    out JsonElement radioOptions)
                && radioOptions.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement option in radioOptions.EnumerateArray())
                {
                    String value = Text(
                        option,
                        LumuiProtocol.Fields.Value,
                        Text(option, LumuiProtocol.Fields.Id));
                    String optionLabel = Text(
                        option,
                        LumuiProtocol.Fields.Label,
                        value);
                    RadioButton radio = new RadioButton
                    {
                        Content = optionLabel,
                        GroupName = id,
                        Tag = value,
                        IsChecked = value == current,
                        IsEnabled = enabled
                            && !HasFalse(option, LumuiProtocol.Fields.Enabled),
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    Border card = new Border { Child = radio };
                    _styler.ApplyChoiceCard(card, value == current);
                    entries.Add((radio, card, value));
                    choices.Children.Add(card);
                }
            }
            _inputs[id] = () => current;
            foreach ((RadioButton radio, Border _, String value) in entries)
            {
                radio.Click += async (_, _) =>
                {
                    if (radio.IsChecked != true)
                    {
                        return;
                    }
                    current = value;
                    foreach ((RadioButton _, Border card, String optionValue)
                        in entries)
                    {
                        _styler.ApplyChoiceCard(
                            card,
                            optionValue == current);
                    }
                    await InvokeComponentActionAsync(node);
                };
            }
            return LabeledChoiceGroup(node, choices);
        }
        if (kind == LumuiProtocol.ComponentKinds.Choice)
        {
            List<(Button Control, String Value)> entries =
                new List<(Button Control, String Value)>();
            WrapPanel choices = new WrapPanel();
            if (node.TryGetProperty(
                    LumuiProtocol.Fields.Options,
                    out JsonElement choiceOptions)
                && choiceOptions.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement option in choiceOptions.EnumerateArray())
                {
                    String value = Text(
                        option,
                        LumuiProtocol.Fields.Value,
                        Text(option, LumuiProtocol.Fields.Id));
                    Button choice = new Button
                    {
                        Content = Text(
                            option,
                            LumuiProtocol.Fields.Label,
                            value),
                        Tag = value,
                        IsEnabled = enabled
                            && !HasFalse(option, LumuiProtocol.Fields.Enabled),
                        Margin = new Thickness(0D, 0D, 8D, 8D)
                    };
                    if (value == current)
                    {
                        ApplyPrimaryButton(choice);
                    }
                    else
                    {
                        ApplyLinkButton(choice);
                    }
                    entries.Add((choice, value));
                    choices.Children.Add(choice);
                }
            }
            _inputs[id] = () => current;
            foreach ((Button choice, String value) in entries)
            {
                choice.Click += async (_, _) =>
                {
                    current = value;
                    foreach ((Button option, String optionValue) in entries)
                    {
                        if (optionValue == current)
                        {
                            ApplyPrimaryButton(option);
                        }
                        else
                        {
                            ApplyLinkButton(option);
                        }
                    }
                    await InvokeComponentActionAsync(node);
                };
            }
            return LabeledChoiceGroup(node, choices);
        }
        ComboBox combo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 46D,
            IsEnabled = enabled
        };
        List<ComboBoxItem> optionItems = new List<ComboBoxItem>();
        if (node.TryGetProperty(
                LumuiProtocol.Fields.Options,
                out JsonElement options)
            && options.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement option in options.EnumerateArray())
            {
                String value = Text(
                    option,
                    LumuiProtocol.Fields.Value,
                    Text(option, LumuiProtocol.Fields.Id));
                ComboBoxItem item = new ComboBoxItem
                {
                    Content = Text(
                        option,
                        LumuiProtocol.Fields.Label,
                        value),
                    Tag = value,
                    IsEnabled = !HasFalse(
                        option,
                        LumuiProtocol.Fields.Enabled)
                };
                optionItems.Add(item);
                if (value == current)
                {
                    combo.SelectedItem = item;
                }
            }
        }
        combo.ItemsSource = optionItems;
        if (combo.SelectedItem is null && optionItems.Count > 0)
        {
            combo.SelectedItem = optionItems[0];
            current = optionItems[0].Tag?.ToString() ?? String.Empty;
        }
        _inputs[id] = () =>
            (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        combo.SelectionChanged += async (_, _) =>
        {
            current = (combo.SelectedItem as ComboBoxItem)
                ?.Tag
                ?.ToString() ?? String.Empty;
            await InvokeComponentActionAsync(node);
        };
        return Field(node, combo);
    }

private Control RenderProgress(JsonElement node)
    {
        Double minimum = Number(node, LumuiProtocol.Fields.Min);
        Double maximum = Number(node, LumuiProtocol.Fields.Max, 100D);
        if (maximum <= minimum)
        {
            maximum = minimum + 1D;
        }
        Double value = Math.Clamp(
            Number(node, LumuiProtocol.Fields.Value),
            minimum,
            maximum);
        String unit = Text(node, LumuiProtocol.Fields.Unit);
        String label = Text(
            node,
            LumuiProtocol.Fields.Label,
            RendererText.Progress);
        Grid heading = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*")
        };
        heading.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush(_appearance.Text),
            TextWrapping = TextWrapping.Wrap
        });
        TextBlock output = new TextBlock
        {
            Text = value.ToString(
                    "0.##",
                    CultureInfo.CurrentCulture)
                + unit,
            Foreground = Brush(_appearance.Text),
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        heading.Children.Add(output);
        Grid.SetColumn(output, 1);
        StackPanel content = new StackPanel
        {
            Spacing = 12D,
            Children =
            {
                heading,
                ProgressTrack(
                    value,
                    minimum,
                    maximum,
                    _brand.Accent)
            }
        };
        Border frame = new Border { Child = content };
        _styler.ApplyShowcaseCard(frame);
        AutomationProperties.SetName(
            frame,
            label + ", " + output.Text);
        AutomationProperties.SetAutomationId(
            frame,
            Text(node, LumuiProtocol.Fields.Id));
        return frame;
    }

private Control RenderMeter(JsonElement node)
    {
        Double minimum = Number(node, LumuiProtocol.Fields.Min);
        Double maximum = Number(node, LumuiProtocol.Fields.Max, 100D);
        if (maximum <= minimum)
        {
            maximum = minimum + 1D;
        }
        Double value = Math.Clamp(
            Number(node, LumuiProtocol.Fields.Value),
            minimum,
            maximum);
        Double low = Number(node, "low", minimum);
        Double high = Number(node, "high", maximum);
        String unit = Text(node, LumuiProtocol.Fields.Unit);
        String label = Text(node, LumuiProtocol.Fields.Label, "Meter");
        String accent = value < low
            ? _brand.Highlight
            : value > high
                ? _brand.AccentSecondary
                : _brand.Accent;
        Grid heading = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12D
        };
        heading.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Brush(_appearance.Text),
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        TextBlock output = new TextBlock
        {
            Text = value.ToString(
                    "0.##",
                    CultureInfo.CurrentCulture)
                + unit,
            Foreground = Brush(_appearance.Text),
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        heading.Children.Add(output);
        Grid.SetColumn(output, 1);
        Grid scale = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*")
        };
        scale.Children.Add(new TextBlock
        {
            Text = minimum.ToString(
                "0.##",
                CultureInfo.CurrentCulture),
            Foreground = Brush(_appearance.Muted),
            FontSize = Font(12D)
        });
        TextBlock maximumLabel = new TextBlock
        {
            Text = maximum.ToString(
                "0.##",
                CultureInfo.CurrentCulture),
            Foreground = Brush(_appearance.Muted),
            FontSize = Font(12D),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        scale.Children.Add(maximumLabel);
        Grid.SetColumn(maximumLabel, 1);
        StackPanel content = new StackPanel
        {
            Spacing = 10D,
            Children =
            {
                heading,
                ProgressTrack(value, minimum, maximum, accent),
                scale
            }
        };
        Border frame = new Border { Child = content };
        _styler.ApplyShowcaseCard(frame);
        AutomationProperties.SetName(
            frame,
            label + ", " + output.Text);
        AutomationProperties.SetAutomationId(
            frame,
            Text(node, LumuiProtocol.Fields.Id));
        return frame;
    }

private Control RenderRating(JsonElement node)
    {
        String id = Text(node, LumuiProtocol.Fields.Id);
        Int32 minimum = Math.Max(
            0,
            (Int32)Number(node, LumuiProtocol.Fields.Min));
        Int32 maximum = Math.Clamp(
            (Int32)Number(node, LumuiProtocol.Fields.Max, 5D),
            1,
            10);
        Double current = Math.Clamp(
            Number(node, LumuiProtocol.Fields.Value),
            minimum,
            maximum);
        TextBlock output = new TextBlock
        {
            Foreground = Brush(_appearance.Muted),
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        WrapPanel stars = new WrapPanel();
        List<Button> buttons = new List<Button>();
        void Update()
        {
            for (Int32 index = 0; index < buttons.Count; index++)
            {
                buttons[index].Content = index + 1 <= current ? "★" : "☆";
                buttons[index].Foreground = Brush(
                    index + 1 <= current
                        ? _brand.Highlight
                        : _appearance.Muted);
            }
            output.Text = current.ToString(
                    "0.#",
                    CultureInfo.CurrentCulture)
                + " of "
                + maximum.ToString(CultureInfo.CurrentCulture);
        }
        for (Int32 value = 1; value <= maximum; value++)
        {
            Int32 selected = value;
            Button star = new Button
            {
                MinWidth = 42D,
                MinHeight = 42D,
                Padding = new Thickness(4D),
                FontSize = Font(27D),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0D),
                IsEnabled = !HasFalse(node, LumuiProtocol.Fields.Enabled)
            };
            AutomationProperties.SetName(
                star,
                selected.ToString(CultureInfo.CurrentCulture)
                    + (selected == 1 ? " star" : " stars"));
            star.Click += async (_, _) =>
            {
                current = selected;
                Update();
                await InvokeComponentActionAsync(node);
            };
            buttons.Add(star);
            stars.Children.Add(star);
        }
        Update();
        Grid rating = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12D,
            Children = { stars, output }
        };
        Grid.SetColumn(output, 1);
        StackPanel content = new StackPanel
        {
            Spacing = 12D,
            Children =
            {
                ChoiceNarrative(
                    Text(node, LumuiProtocol.Fields.Label, "Rating"),
                    Text(node, LumuiProtocol.Fields.Description)),
                rating
            }
        };
        _inputs[id] = () => current;
        Border frame = new Border { Child = content };
        _styler.ApplyShowcaseCard(frame);
        AutomationProperties.SetAutomationId(frame, id);
        return frame;
    }

private Control RenderActivity(JsonElement node)
    {
        String label = Text(
            node,
            LumuiProtocol.Fields.Label,
            Text(node, LumuiProtocol.Fields.Title, "Working"));
        String description = Text(
            node,
            LumuiProtocol.Fields.Description,
            Text(node, LumuiProtocol.Fields.Message));
        StackPanel narrative = ChoiceNarrative(label, description);
        ProgressBar activity = new ProgressBar
        {
            IsIndeterminate = true,
            Height = 4D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        StackPanel content = new StackPanel
        {
            Spacing = 12D,
            Children = { narrative, activity }
        };
        Border frame = new Border { Child = content };
        _styler.ApplyStatusPanel(frame, "info");
        AutomationProperties.SetName(
            frame,
            description.Length > 0
                ? label + ". " + description
                : label);
        AutomationProperties.SetAutomationId(
            frame,
            Text(node, LumuiProtocol.Fields.Id));
        return frame;
    }

    private Control RenderImage(JsonElement node)
    {
        Uri? uri = SourceUri(node);
        if (uri is null)
        {
            return RenderFallback(node);
        }
        String alt = Text(
            node,
            LumuiProtocol.Fields.Alt,
            Text(node, LumuiProtocol.Fields.Caption, RendererText.Image));
        ContentControl image = new ContentControl
        {
            Height = _settings.Profile.Kind switch
            {
                DeviceProfileKind.Watch => 140D,
                DeviceProfileKind.Phone => 220D,
                _ => 300D
            },
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new TextBlock
            {
                Text = alt,
                Foreground = Brush(_appearance.Muted),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(18D)
            }
        };
        Border frame = new Border { Child = image };
        _styler.ApplyMediaFrame(frame);
        StackPanel panel = new StackPanel
        {
            Spacing = 10D,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children = { frame }
        };
        String caption = Text(node, LumuiProtocol.Fields.Caption);
        if (caption.Length > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = caption,
                Foreground = Brush(_appearance.Text),
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
        }
        if (!Boolean(node, "decorative"))
        {
            AutomationProperties.SetName(frame, alt);
        }
        _ = _assetLoader.LoadAsync(image, uri, MediaType(node));
        return panel;
    }

    private Control RenderMedia(JsonElement node)
    {
        String kind = Text(node, LumuiProtocol.Fields.Kind);
        Boolean video = kind is LumuiProtocol.ComponentKinds.Video
            or LumuiProtocol.ComponentKinds.VideoPlayer;
        String title = Text(
            node,
            LumuiProtocol.Fields.Label,
            Text(node, LumuiProtocol.Fields.Title, video ? "Video" : "Audio"));
        String description = Text(node, LumuiProtocol.Fields.Description);
        Double duration = MediaNumber(node, "duration_ms");
        Double position = MediaNumber(node, "position_ms");
        String state = MediaText(node, "state");
        IReadOnlyList<MediaSourceDescriptor> sources = MediaSources(node);

        StackPanel content = new StackPanel { Spacing = 14D };
        Grid heading = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*")
        };
        StackPanel titles = new StackPanel { Spacing = 3D };
        titles.Children.Add(new TextBlock
        {
            Text = video ? "VIDEO" : "AUDIO",
            Foreground = Brush(_appearance.Accent),
            FontWeight = FontWeight.Bold,
            FontSize = Font(11D),
            LetterSpacing = 1.2D
        });
        titles.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brush(_appearance.Text),
            FontWeight = FontWeight.SemiBold,
            FontSize = Font(21D),
            TextWrapping = TextWrapping.Wrap
        });
        heading.Children.Add(titles);
        content.Children.Add(heading);

        String previewField = video ? "poster" : "artwork";
        Uri? previewUri = ResourceUri(node, previewField);
        Double mediaHeight = MediaHeight(node, video);
        Border mediaFrame = new Border
        {
            Height = mediaHeight,
            ClipToBounds = true
        };
        _styler.ApplyMediaFrame(mediaFrame);
        if (previewUri is not null)
        {
            ContentControl preview = new ContentControl
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            };
            mediaFrame.Child = preview;
            _ = _assetLoader.LoadAsync(
                preview,
                previewUri,
                ResourceMediaType(node, previewField));
        }
        else
        {
            mediaFrame.Child = new StackPanel
            {
                Spacing = 10D,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new FontAwesomeIcon
                    {
                        Icon = BrowserIcons.Play,
                        IconSize = video ? 48D : 40D,
                        Foreground = Brush(_appearance.Accent),
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = video
                            ? "Video ready"
                            : "Audio ready",
                        Foreground = Brush(_appearance.Muted),
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            };
        }
        if (sources.Count > 0)
        {
            NativeMediaPalette mediaPalette = new NativeMediaPalette(
                Brush(_appearance.Surface),
                Brush(_appearance.SurfaceAlternate),
                Brush(_appearance.Text),
                Brush(_appearance.Muted),
                Brush(_appearance.Accent),
                Brush(_appearance.AccentText),
                Brush(_appearance.Border),
                new FontFamily(_appearance.FontFamily));
            NativeMediaPlayer mediaPlayer = new NativeMediaPlayer(
                sources,
                ResourceUris(node, "captions"),
                video,
                title,
                mediaFrame,
                mediaHeight,
                duration,
                position,
                state,
                !video || !_settings.ReducedMotion,
                mediaPalette,
                _status)
            {
                IsEnabled = !HasFalse(node, LumuiProtocol.Fields.Enabled)
                    && !Boolean(node, LumuiProtocol.Fields.Readonly)
            };
            _mediaPlayers.Add(mediaPlayer);
            content.Children.Add(mediaPlayer);
        }
        else
        {
            content.Children.Add(mediaFrame);
            content.Children.Add(Body(
                node.TryGetProperty("session", out JsonElement session)
                    && session.ValueKind is not JsonValueKind.Null
                    && session.ValueKind is not JsonValueKind.Undefined
                        ? "This media session does not expose a directly playable source."
                        : "Media unavailable."));
            if (node.TryGetProperty(
                    LumuiProtocol.Fields.Fallback,
                    out JsonElement fallback)
                && fallback.ValueKind is not JsonValueKind.Null
                && fallback.ValueKind is not JsonValueKind.Undefined)
            {
                content.Children.Add(RenderDeclaredFallback(node));
            }
        }

        String creator = Text(node, "artist");
        String collection = Text(node, "album");
        if (creator.Length > 0 || collection.Length > 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = String.Join(
                    " · ",
                    new String[] { creator, collection }
                        .Where(value => value.Length > 0)),
                Foreground = Brush(_appearance.Muted),
                FontSize = Font(14D),
                TextWrapping = TextWrapping.Wrap
            });
        }

        if (description.Length > 0)
        {
            content.Children.Add(Body(description));
        }

        WrapPanel resourceLinks = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Left
        };
        AddMediaResourceLinks(
            resourceLinks,
            node,
            "transcript",
            "Source and license");
        AddMediaResourceLinks(
            resourceLinks,
            node,
            "captions",
            "Captions");
        AddMediaResourceLinks(
            resourceLinks,
            node,
            "audio_description",
            "Audio description");
        if (sources.Count > 0 && Boolean(node, LumuiProtocol.Fields.Download))
        {
            AddMediaLink(
                resourceLinks,
                "Download media",
                sources[0].Uri,
                _download);
        }
        if (resourceLinks.Children.Count > 0)
        {
            content.Children.Add(resourceLinks);
        }

        if (node.TryGetProperty(LumuiProtocol.Fields.Actions, out JsonElement playerActions)
            && playerActions.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement action in playerActions.EnumerateArray())
            {
                String actionId = Display(action);
                if (actionId.Length > 0
                    && !actionId.Equals(
                        "component_demo",
                        StringComparison.Ordinal))
                {
                    content.Children.Add(RenderActionReference(node, actionId));
                }
            }
        }

        if (_embeddedPresentation)
        {
            AutomationProperties.SetName(content, title);
            AutomationProperties.SetHelpText(content, description);
            AutomationProperties.SetAutomationId(
                content,
                Text(node, LumuiProtocol.Fields.Id));
            return content;
        }
        Border card = new Border
        {
            Child = content,
            Padding = new Thickness(22D)
        };
        card.Classes.Add(BrowserStyleClasses.Soft);
        _styler.ApplyComponentPanel(
            card,
            video ? _brand.AccentSecondary : _brand.Accent);
        AutomationProperties.SetName(card, title);
        AutomationProperties.SetHelpText(card, description);
        AutomationProperties.SetAutomationId(
            card,
            Text(node, LumuiProtocol.Fields.Id));
        return card;
    }

    private Control RenderPreview(JsonElement node) =>
        RenderPreview(node, false, 0D);

    private Control RenderPreview(
        JsonElement node,
        Boolean constrained,
        Double constrainedHeight)
    {
        String title = Text(
            node,
            LumuiProtocol.Fields.Label,
            RendererText.Preview);
        String description = Text(node, LumuiProtocol.Fields.Description);
        Control rendered;
        if (node.TryGetProperty(
                LumuiProtocol.Fields.Content,
                out JsonElement content)
            && content.ValueKind == JsonValueKind.Object)
        {
            rendered = RenderNode(content);
        }
        else if (node.TryGetProperty(
                LumuiProtocol.Fields.Fallback,
                out JsonElement fallback)
            && fallback.ValueKind == JsonValueKind.Object)
        {
            rendered = RenderNode(fallback);
        }
        else
        {
            rendered = PreviewMessage(RendererText.PreviewUnavailable);
        }

        Border frame = new Border
        {
            Child = rendered,
            Padding = new Thickness(constrained ? 8D : 18D),
            Background = Brush(_appearance.Surface),
            BorderBrush = Brush(_appearance.Border),
            BorderThickness = new Thickness(1D),
            CornerRadius = new CornerRadius(UsesLumiStyle ? 0D : 16D),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _styler.ApplyComponentPanel(frame, _brand.Accent);

        Control result;
        if (constrained)
        {
            result = new Border
            {
                Height = constrainedHeight,
                ClipToBounds = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Child = new Viewbox
                {
                    Child = frame,
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.DownOnly,
                    IsHitTestVisible = false,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                }
            };
        }
        else
        {
            StackPanel panel = CapabilityPanel(title, description);
            panel.Children.Add(frame);
            result = panel;
        }
        AutomationProperties.SetName(result, title);
        AutomationProperties.SetHelpText(result, description);
        AutomationProperties.SetAutomationId(
            result,
            Text(node, LumuiProtocol.Fields.Id));
        return result;
    }

    private Control PreviewMessage(String message)
    {
        Border border = new Border
        {
            Child = new TextBlock
            {
                Text = message,
                Foreground = Brush(_appearance.Muted),
                TextWrapping = TextWrapping.Wrap
            }
        };
        border.Classes.Add(BrowserStyleClasses.Soft);
        ApplySoftPanel(border);
        return border;
    }

    private static String SurfaceIdentity(Uri uri)
    {
        UriBuilder builder = new UriBuilder(uri)
        {
            Fragment = String.Empty
        };
        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }

    private Control RenderImageOption(JsonElement node)
    {
        String id = Text(node, LumuiProtocol.Fields.Id);
        String label = Text(
            node,
            LumuiProtocol.Fields.Label,
            RendererText.Option);
        String description = Text(node, LumuiProtocol.Fields.Description);
        Boolean selected = Boolean(node, "selected");
        ContentControl preview = new ContentControl
        {
            Height = _settings.Profile.Kind == DeviceProfileKind.Watch
                ? 110D
                : 190D,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = Body(label)
        };
        Border previewFrame = new Border { Child = preview };
        _styler.ApplyMediaFrame(previewFrame);
        Uri? uri = SourceUri(node);
        if (uri is not null)
        {
            _ = _assetLoader.LoadAsync(preview, uri, MediaType(node));
        }
        Grid heading = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10D
        };
        heading.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Brush(_appearance.Text),
            FontWeight = FontWeight.SemiBold,
            FontSize = Font(16D),
            TextWrapping = TextWrapping.Wrap
        });
        FontAwesomeIcon selectedIcon = new FontAwesomeIcon
        {
            Icon = BrowserIcons.Check,
            IconSize = 17D,
            Foreground = Brush(_appearance.Accent),
            IsVisible = selected,
            VerticalAlignment = VerticalAlignment.Center
        };
        heading.Children.Add(selectedIcon);
        Grid.SetColumn(selectedIcon, 1);
        StackPanel content = new StackPanel
        {
            Spacing = 11D,
            Children = { previewFrame, heading }
        };
        if (description.Length > 0)
        {
            content.Children.Add(Body(description));
        }
        String actionId = Text(node, LumuiProtocol.Fields.Action);
        Button button = new Button
        {
            Content = content,
            IsEnabled = actionId.Length > 0
                && !HasFalse(node, LumuiProtocol.Fields.Enabled),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _styler.ApplyChoiceButton(button, selected);
        _inputs[id] = () => Text(
            node,
            LumuiProtocol.Fields.Value,
            selected ? "true" : "false");
        button.Click += async (_, _) =>
        {
            selected = true;
            selectedIcon.IsVisible = true;
            _styler.ApplyChoiceButton(button, true);
            await InvokeComponentActionAsync(node);
        };
        AutomationProperties.SetName(button, label);
        AutomationProperties.SetAutomationId(button, id);
        AutomationProperties.SetHelpText(button, description);
        return button;
    }

    private Control RenderDeclaredFallback(JsonElement node)
    {
        if (node.TryGetProperty(
                LumuiProtocol.Fields.Fallback,
                out JsonElement fallback))
        {
            if (fallback.ValueKind == JsonValueKind.Object)
            {
                return RenderNode(fallback);
            }
            if (fallback.ValueKind == JsonValueKind.String)
            {
                Border text = new Border
                {
                    Child = Body(fallback.GetString() ?? String.Empty)
                };
                ApplySoftPanel(text);
                return text;
            }
        }
        return RenderFallback(node);
    }

    private Control RenderFallback(JsonElement node)
    {
        if (node.TryGetProperty(LumuiProtocol.Fields.Fallback, out JsonElement fallback) && fallback.ValueKind == JsonValueKind.Object)
        {
            return RenderNode(fallback);
        }
        Border border = new Border
        {
            Child = Body(Text(
                node,
                LumuiProtocol.Fields.Label,
                RendererText.Unsupported
                    + Text(
                        node,
                        LumuiProtocol.Fields.Kind,
                        RendererText.Component)))
        };
        border.Classes.Add(BrowserStyleClasses.Soft);
        ApplySoftPanel(border);
        return border;
    }

    private void AddMediaResourceLinks(
        WrapPanel links,
        JsonElement node,
        String field,
        String label)
    {
        IReadOnlyList<Uri> resources = ResourceUris(node, field);
        for (Int32 index = 0; index < resources.Count; index++)
        {
            AddMediaLink(
                links,
                resources.Count == 1
                    ? label
                    : $"{label} {index + 1}",
                resources[index],
                _openExternal);
        }
    }

    private void AddMediaLink(
        WrapPanel links,
        String label,
        Uri uri,
        Func<Uri, Task> open)
    {
        TextBlock text = new TextBlock
        {
            Text = label,
            Foreground = Brush(_appearance.Accent),
            FontFamily = new FontFamily(_appearance.FontFamily),
            FontSize = Font(13D),
            FontWeight = FontWeight.SemiBold,
            TextDecorations = TextDecorations.Underline
        };
        Button button = new Button
        {
            Content = text,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0D),
            Padding = new Thickness(0D),
            MinWidth = 0D,
            MinHeight = 0D,
            Margin = new Thickness(0D, 0D, 16D, 4D),
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(button, label);
        ToolTip.SetTip(button, uri.AbsoluteUri);
        button.Click += async (_, _) => await open(uri);
        links.Children.Add(button);
    }

    private IReadOnlyList<MediaSourceDescriptor> MediaSources(JsonElement node)
    {
        List<MediaSourceDescriptor> sources = new List<MediaSourceDescriptor>();
        HashSet<String> identities = new HashSet<String>(
            StringComparer.OrdinalIgnoreCase);
        if (node.TryGetProperty(
                LumuiProtocol.Fields.Source,
                out JsonElement source))
        {
            AddMediaSourcesFromValue(
                source,
                sources,
                identities,
                Text(node, LumuiProtocol.Fields.Type));
        }
        if (node.TryGetProperty("session", out JsonElement session)
            && session.ValueKind == JsonValueKind.Object)
        {
            AddMediaSourcesFromValue(session, sources, identities);
        }
        if (node.TryGetProperty("variants", out JsonElement variants))
        {
            AddMediaSourcesFromValue(variants, sources, identities);
        }
        return sources;
    }

    private void AddMediaSourcesFromValue(
        JsonElement value,
        List<MediaSourceDescriptor> sources,
        HashSet<String> identities,
        String inheritedMimeType = "")
    {
        String mimeType = value.ValueKind == JsonValueKind.Object
            ? Text(value, LumuiProtocol.Fields.Type, inheritedMimeType)
            : inheritedMimeType;
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                AddMediaSourcesFromValue(
                    item,
                    sources,
                    identities,
                    mimeType);
            }
            return;
        }

        Uri? uri = SourceUriValue(value);
        if (uri is not null && identities.Add(uri.AbsoluteUri))
        {
            sources.Add(new MediaSourceDescriptor(
                uri,
                mimeType));
        }
        if (value.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        if (value.TryGetProperty(
                LumuiProtocol.Fields.Source,
                out JsonElement nestedSource))
        {
            AddMediaSourcesFromValue(
                nestedSource,
                sources,
                identities,
                mimeType);
        }
        if (value.TryGetProperty("variants", out JsonElement variants))
        {
            AddMediaSourcesFromValue(
                variants,
                sources,
                identities,
                mimeType);
        }
        if (value.TryGetProperty("sources", out JsonElement sourceList))
        {
            AddMediaSourcesFromValue(
                sourceList,
                sources,
                identities,
                mimeType);
        }
    }

    private Uri? ResourceUri(JsonElement node, String field)
    {
        IReadOnlyList<Uri> resources = ResourceUris(node, field);
        return resources.Count == 0 ? null : resources[0];
    }

    private IReadOnlyList<Uri> ResourceUris(
        JsonElement node,
        String field)
    {
        List<Uri> resources = new List<Uri>();
        HashSet<String> identities = new HashSet<String>(
            StringComparer.OrdinalIgnoreCase);
        if (node.TryGetProperty(field, out JsonElement direct))
        {
            AddResourceUris(direct, resources, identities);
        }
        if (node.TryGetProperty(
                LumuiProtocol.Fields.Source,
                out JsonElement source))
        {
            AddResourceFieldUris(
                source,
                field,
                resources,
                identities);
        }
        if (node.TryGetProperty("session", out JsonElement session))
        {
            AddResourceFieldUris(
                session,
                field,
                resources,
                identities);
        }
        if (node.TryGetProperty("variants", out JsonElement variants))
        {
            AddResourceFieldUris(
                variants,
                field,
                resources,
                identities);
        }
        return resources;
    }

    private void AddResourceFieldUris(
        JsonElement value,
        String field,
        List<Uri> resources,
        HashSet<String> identities)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                AddResourceFieldUris(
                    item,
                    field,
                    resources,
                    identities);
            }
            return;
        }
        if (value.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        if (value.TryGetProperty(field, out JsonElement resource))
        {
            AddResourceUris(resource, resources, identities);
        }
        foreach (String child in new String[]
                 {
                     LumuiProtocol.Fields.Source,
                     "sources",
                     "variants"
                 })
        {
            if (value.TryGetProperty(child, out JsonElement nested))
            {
                AddResourceFieldUris(
                    nested,
                    field,
                    resources,
                    identities);
            }
        }
    }

    private void AddResourceUris(
        JsonElement value,
        List<Uri> resources,
        HashSet<String> identities)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                AddResourceUris(item, resources, identities);
            }
            return;
        }
        Uri? uri = SourceUriValue(value);
        if (uri is not null && identities.Add(uri.AbsoluteUri))
        {
            resources.Add(uri);
        }
        if (value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty(
                LumuiProtocol.Fields.Source,
                out JsonElement nested))
        {
            AddResourceUris(nested, resources, identities);
        }
    }

    private static String ResourceMediaType(
        JsonElement node,
        String field)
    {
        if (node.TryGetProperty(field, out JsonElement resource)
            && resource.ValueKind == JsonValueKind.Object)
        {
            return Text(resource, LumuiProtocol.Fields.Type);
        }
        return String.Empty;
    }

    private static Double MediaNumber(JsonElement node, String field)
    {
        if (node.TryGetProperty(field, out JsonElement direct)
            && direct.TryGetDouble(out Double value))
        {
            return value;
        }
        foreach (String parent in new String[]
                 {
                     LumuiProtocol.Fields.Source,
                     "session"
                 })
        {
            if (node.TryGetProperty(parent, out JsonElement container)
                && container.ValueKind == JsonValueKind.Object
                && container.TryGetProperty(field, out JsonElement nested)
                && nested.TryGetDouble(out value))
            {
                return value;
            }
        }
        return 0D;
    }

    private static String MediaText(JsonElement node, String field)
    {
        String value = Text(node, field);
        if (value.Length > 0)
        {
            return value;
        }
        foreach (String parent in new String[]
                 {
                     LumuiProtocol.Fields.Source,
                     "session"
                 })
        {
            if (node.TryGetProperty(parent, out JsonElement container)
                && container.ValueKind == JsonValueKind.Object)
            {
                value = Text(container, field);
                if (value.Length > 0)
                {
                    return value;
                }
            }
        }
        return String.Empty;
    }

    private Double MediaHeight(JsonElement node, Boolean video)
    {
        if (_embeddedPresentation)
        {
            return video ? 180D : 90D;
        }
        Double defaultHeight = _settings.Profile.Kind switch
        {
            DeviceProfileKind.Watch => 100D,
            DeviceProfileKind.Phone => video ? 190D : 146D,
            _ => video ? 300D : 180D
        };
        if (!video)
        {
            return defaultHeight;
        }

        String aspectRatio = MediaText(node, "intrinsic_aspect_ratio");
        String[] parts = aspectRatio.Split(
            ':',
            StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !Double.TryParse(
                parts[0],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out Double width)
            || !Double.TryParse(
                parts[1],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out Double height)
            || width <= 0D
            || height <= 0D)
        {
            return defaultHeight;
        }
        Double availableWidth = Math.Min(
            _settings.Profile.ContentWidth,
            _settings.Profile.Kind == DeviceProfileKind.Phone
                ? 420D
                : 540D);
        return Math.Clamp(availableWidth * height / width, 150D, 360D);
    }

    private Uri? SourceUri(JsonElement node)
    {
        if (!node.TryGetProperty(LumuiProtocol.Fields.Source, out JsonElement source))
        {
            return null;
        }
        return SourceUriValue(source);
    }

    private static String MediaType(JsonElement node)
    {
        String mediaType = Text(node, LumuiProtocol.Fields.Type);
        if (mediaType.Length > 0)
        {
            return mediaType;
        }
        if (node.TryGetProperty(
                LumuiProtocol.Fields.Source,
                out JsonElement source)
            && source.ValueKind == JsonValueKind.Object)
        {
            return Text(source, LumuiProtocol.Fields.Type);
        }
        return String.Empty;
    }

    private Uri? SourceUriValue(JsonElement source)
    {
        String value = source.ValueKind switch
        {
            JsonValueKind.String => source.GetString() ?? String.Empty,
            JsonValueKind.Object => Text(
                source,
                LumuiProtocol.Fields.Src,
                Text(
                    source,
                    LumuiProtocol.Fields.Href,
                    Text(source, "url", Text(source, "uri")))),
            _ => String.Empty
        };
        return ResolveUri(value, allowExternal: true);
    }

    private Uri? ResolveUri(String value, Boolean allowExternal)
    {
        if (String.IsNullOrWhiteSpace(value) || !Uri.TryCreate(_baseUri, value, out Uri? uri))
        {
            return null;
        }
        Boolean secureWeb = uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        Boolean localWeb = uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && uri.IsLoopback;
        if (allowExternal
            && (uri.Scheme.Equals(LumuiProtocol.Schemes.Mail, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals(
                    LumuiProtocol.Schemes.Telephone,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return uri;
        }
        if (!secureWeb && !localWeb)
        {
            return null;
        }
        if (!allowExternal
            && (!_baseUri.Scheme.Equals(uri.Scheme, StringComparison.OrdinalIgnoreCase)
                || !_baseUri.Host.Equals(uri.Host, StringComparison.OrdinalIgnoreCase)
                || _baseUri.Port != uri.Port))
        {
            return null;
        }
        return uri;
    }

    private Control ComponentPanel(
        Control content,
        String title,
        String? accent = null)
    {
        Border panel = new Border
        {
            Child = content,
            MaxWidth = 760D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _styler.ApplyComponentPanel(panel, accent);
        AutomationProperties.SetName(panel, title);
        return panel;
    }

    private StackPanel ChoiceNarrative(
        String label,
        String description)
    {
        StackPanel narrative = new StackPanel { Spacing = 4D };
        narrative.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Brush(_appearance.Text),
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        if (description.Length > 0)
        {
            narrative.Children.Add(Body(description));
        }
        return narrative;
    }

    private Control LabeledChoiceGroup(
        JsonElement node,
        Control choices)
    {
        String label = Text(
            node,
            LumuiProtocol.Fields.Label,
            RendererText.Option);
        String description = Text(node, LumuiProtocol.Fields.Description);
        StackPanel group = new StackPanel
        {
            Spacing = 11D,
            MaxWidth = 760D,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                ChoiceNarrative(label, description),
                choices
            }
        };
        AutomationProperties.SetName(choices, label);
        AutomationProperties.SetAutomationId(
            choices,
            Text(node, LumuiProtocol.Fields.Id));
        AutomationProperties.SetHelpText(choices, description);
        return group;
    }

    private Button IconButton(
        String icon,
        String name,
        Boolean enabled)
    {
        Button button = new Button
        {
            Content = new FontAwesomeIcon
            {
                Icon = icon,
                IconSize = 15D,
                Foreground = Brush(_appearance.Text),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            IsEnabled = enabled,
            MinWidth = 46D,
            MinHeight = 46D,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        ApplyLinkButton(button);
        AutomationProperties.SetName(button, name);
        return button;
    }

    private Control LabeledCompactControl(
        String label,
        Control control)
    {
        TextBlock caption = new TextBlock
        {
            Text = label,
            Foreground = Brush(_appearance.Muted),
            FontSize = Font(13D),
            FontWeight = FontWeight.SemiBold
        };
        StackPanel field = new StackPanel
        {
            Spacing = 5D,
            Children = { caption, control }
        };
        AutomationProperties.SetLabeledBy(control, caption);
        return field;
    }

    private Border ProgressTrack(
        Double value,
        Double minimum,
        Double maximum,
        String accent)
    {
        Double range = Math.Max(0.001D, maximum - minimum);
        Double position = Math.Clamp(value - minimum, 0D, range);
        Grid fill = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(
                Math.Max(0.001D, position).ToString(
                    CultureInfo.InvariantCulture)
                + "*,"
                + Math.Max(0.001D, range - position).ToString(
                    CultureInfo.InvariantCulture)
                + "*")
        };
        fill.Children.Add(new Border
        {
            Background = Brush(accent),
            CornerRadius = new CornerRadius(UsesLumiStyle ? 0D : 999D)
        });
        return new Border
        {
            Child = fill,
            Height = 14D,
            Background = Brush(_appearance.SurfaceAlternate),
            BorderBrush = Brush(_appearance.Border),
            BorderThickness = new Thickness(1D),
            CornerRadius = new CornerRadius(UsesLumiStyle ? 0D : 999D),
            ClipToBounds = true
        };
    }

    private Task InvokeComponentActionAsync(JsonElement node) =>
        InvokeComponentActionAsync(
            node,
            Text(node, LumuiProtocol.Fields.Action));

    private async Task InvokeComponentActionAsync(
        JsonElement node,
        String actionId)
    {
        if (_disposed)
        {
            return;
        }
        String componentId = Text(node, LumuiProtocol.Fields.Id);
        if (actionId.Length == 0)
        {
            return;
        }
        Dictionary<String, Object?> input = _inputs.ToDictionary(
            pair => pair.Key,
            pair => pair.Value(),
            StringComparer.Ordinal);
        await _invoke(componentId, actionId, input);
    }

    private static String SemanticIcon(String symbol) =>
        symbol.Trim().ToLowerInvariant() switch
        {
            "calendar" => BrowserIcons.Calendar,
            "check" or "success" => BrowserIcons.Check,
            "close" => BrowserIcons.Close,
            "bookmark" => BrowserIcons.Bookmark,
            "search" => BrowserIcons.Find,
            "home" => BrowserIcons.Home,
            "menu" => BrowserIcons.Menu,
            "settings" or "preferences" => BrowserIcons.Settings,
            "download" => BrowserIcons.Download,
            "upload" => BrowserIcons.Upload,
            "play" => BrowserIcons.Play,
            "pause" => BrowserIcons.Pause,
            "user" or "person" => BrowserIcons.User,
            "location" or "map-pin" => BrowserIcons.Location,
            "phone" => BrowserIcons.Phone,
            "email" or "mail" => BrowserIcons.Email,
            "edit" => BrowserIcons.Edit,
            "delete" or "remove" => BrowserIcons.Clear,
            "back" or "arrow-left" => BrowserIcons.Back,
            "forward" or "arrow-right" => BrowserIcons.Forward,
            "warning" or "error" => BrowserIcons.CircleWarning,
            "info" => BrowserIcons.CircleInfo,
            _ => BrowserIcons.CircleInfo
        };

    private Control Field(JsonElement node, Control input)
    {
        ApplyInput(input);
        StackPanel stack = new StackPanel
        {
            Spacing = 7D,
            MaxWidth = 680D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        TextBlock label = new TextBlock
        {
            Text = Text(
                node,
                LumuiProtocol.Fields.Label,
                RendererText.Value),
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush(_appearance.Text)
        };
        stack.Children.Add(label);
        stack.Children.Add(input);
        String description = Text(node, LumuiProtocol.Fields.Description);
        if (description.Length > 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = description,
                Foreground = Brush(_appearance.Muted),
                TextWrapping = TextWrapping.Wrap
            });
        }
        AutomationProperties.SetLabeledBy(input, label);
        AutomationProperties.SetName(
            input,
            Text(
                node,
                LumuiProtocol.Fields.Label,
                RendererText.Value));
        AutomationProperties.SetHelpText(input, description);
        AutomationProperties.SetAutomationId(input, Text(node, LumuiProtocol.Fields.Id));
        AutomationProperties.SetIsRequiredForForm(input, Boolean(node, LumuiProtocol.Fields.Required));
        return stack;
    }

    private TextBlock Body(String value)
    {
        TextBlock text = new TextBlock()
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(_appearance.Muted),
            FontSize = Font(16D),
            LineHeight = Font(25D)
        };
        ReadingTextFormatter.Apply(text, value, _settings.BionicReading);
        return text;
    }

    private static String Text(JsonElement element, String name, String fallback = "")
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out JsonElement value))
        {
            return fallback;
        }
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? fallback;
        }
        if (value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty(LumuiProtocol.Fields.Fallback, out JsonElement localized)
            && localized.ValueKind == JsonValueKind.String)
        {
            return localized.GetString() ?? fallback;
        }
        if (value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty(LumuiProtocol.Fields.Ref, out JsonElement reference)
            && reference.ValueKind == JsonValueKind.String)
        {
            return reference.GetString() ?? fallback;
        }
        return fallback;
    }

    private static String DisplayProperty(JsonElement element, String name, String fallback = "")
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out JsonElement value)
                ? Display(value)
                : fallback;
    }

    private static String FirstAction(JsonElement element)
    {
        String action = Text(element, LumuiProtocol.Fields.Action);
        if (action.Length > 0)
        {
            return action;
        }
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(LumuiProtocol.Fields.Actions, out JsonElement actions)
            && actions.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement candidate in actions.EnumerateArray())
            {
                String value = Display(candidate);
                if (value.Length > 0)
                {
                    return value;
                }
            }
        }
        return String.Empty;
    }

    private static String Display(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? String.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => RendererText.True,
            JsonValueKind.False => RendererText.False,
            JsonValueKind.Null or JsonValueKind.Undefined => String.Empty,
            JsonValueKind.Object => Text(
                value,
                LumuiProtocol.Fields.Label,
                Text(
                    value,
                    LumuiProtocol.Fields.Title,
                    Text(
                        value,
                        LumuiProtocol.Fields.Value,
                        RendererText.Item))),
            JsonValueKind.Array => String.Join(", ", value.EnumerateArray().Select(Display)),
            _ => String.Empty
        };
    }

    private static Boolean Boolean(JsonElement element, String name)
    {
        return element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind is JsonValueKind.True;
    }

    private static Boolean HasFalse(JsonElement element, String name)
    {
        return element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind is JsonValueKind.False;
    }

    private static Double Number(JsonElement element, String name, Double fallback = 0)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.TryGetDouble(out Double number)
            ? number
            : fallback;
    }

    private void ApplyPrimaryButton(Button button)
    {
        _styler.ApplyPrimaryButton(button);
        button.FontFamily = new FontFamily(_appearance.FontFamily);
        if (UsesWideSemanticLayout)
        {
            button.Background = Brush(_brand.Accent);
            button.BorderBrush = Brush(_brand.Accent);
            button.CornerRadius = new CornerRadius(
                UsesLumiStyle ? 0D : 11D);
            button.MinHeight = 46D;
            button.Padding = new Thickness(16D, 11D);
        }
        if (_settings.Profile.Kind == DeviceProfileKind.Kiosk
            || _settings.Interaction.Mode == InteractionMode.Guided)
        {
            button.MinHeight = 54D;
            button.FontSize = Font(17D);
        }
    }

    private void ApplyLinkButton(Button button)
    {
        _styler.ApplyLinkButton(button);
        button.FontFamily = new FontFamily(_appearance.FontFamily);
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        if (_settings.Interaction.Mode == InteractionMode.Guided)
        {
            button.MinHeight = 54D;
            button.FontSize = Font(17D);
        }
    }

    private void ApplyCard(Border border)
    {
        _styler.ApplyCard(
            border,
            LumuiProtocol.RegionRoles.Supporting,
            LumuiProtocol.Priorities.Normal);
    }

    private void ApplySoftPanel(Border border)
    {
        _styler.ApplySoftPanel(border);
    }

    private void ApplyInput(Control input)
    {
        input.MinHeight = Math.Max(input.MinHeight, 46D);
        input.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (_settings.Interaction.Mode == InteractionMode.Guided)
        {
            input.MinHeight = 52D;
        }
        if (input is TextBox textBox)
        {
            textBox.Background = Brush(_appearance.Surface);
            textBox.Foreground = Brush(_appearance.Text);
            textBox.BorderBrush = Brush(_appearance.Border);
            textBox.CaretBrush = Brush(_appearance.Accent);
            textBox.CornerRadius = new CornerRadius(
                UsesLumiStyle
                    ? 0D
                    : Math.Max(12D, _appearance.ControlRadius));
            textBox.Padding = new Thickness(13D, 10D);
        }
        else if (input is ComboBox comboBox)
        {
            comboBox.Background = Brush(_appearance.Surface);
            comboBox.Foreground = Brush(_appearance.Text);
            comboBox.BorderBrush = Brush(_appearance.Border);
            comboBox.CornerRadius = new CornerRadius(
                UsesLumiStyle
                    ? 0D
                    : Math.Max(12D, _appearance.ControlRadius));
            comboBox.Padding = new Thickness(13D, 8D);
        }
        else if (input is NumericUpDown numeric)
        {
            numeric.Background = Brush(_appearance.Surface);
            numeric.Foreground = Brush(_appearance.Text);
            numeric.BorderBrush = Brush(_appearance.Border);
            numeric.Padding = new Thickness(13D, 8D);
        }
        else if (input is ToggleButton toggle)
        {
            toggle.Foreground = Brush(_appearance.Text);
        }
    }

    private Double Font(Double value)
    {
        return value * _settings.TextScale * _settings.PageScale;
    }

    private Boolean UsesWideSemanticLayout =>
        _settings.Profile.Kind is DeviceProfileKind.Web
            or DeviceProfileKind.Desktop;

    private Boolean UsesLumiStyle =>
        _appearance.Kind is AppearanceKind.Material or AppearanceKind.Metro;

    private static IBrush Brush(String value) =>
        BrowserBrushCache.Get(value);

    private static String CleanInlineMarkdown(String value)
    {
        String result = value
            .Replace("**", String.Empty, StringComparison.Ordinal)
            .Replace("`", String.Empty, StringComparison.Ordinal);
        Int32 position = 0;
        while ((position = result.IndexOf('[', position)) >= 0)
        {
            Int32 labelEnd = result.IndexOf(']', position + 1);
            if (labelEnd < 0 || labelEnd + 1 >= result.Length || result[labelEnd + 1] != '(')
            {
                position++;
                continue;
            }
            Int32 targetEnd = result.IndexOf(')', labelEnd + 2);
            if (targetEnd < 0)
            {
                break;
            }
            result = result[..position] + result[(position + 1)..labelEnd] + result[(targetEnd + 1)..];
        }
        return result;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _renderCancellation.Cancel();
        foreach (ViewerWorkspaceRenderer renderer in _workspaceRenderers)
        {
            DeferredDisposalQueue.Shared.Enqueue(renderer);
        }
        _workspaceRenderers.Clear();
        foreach (NativeMediaPlayer mediaPlayer in _mediaPlayers)
        {
            DeferredDisposalQueue.Shared.Enqueue(mediaPlayer);
        }
        _mediaPlayers.Clear();
        _renderCancellation.Dispose();
    }
}
