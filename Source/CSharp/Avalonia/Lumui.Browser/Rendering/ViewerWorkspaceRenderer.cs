using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Lumui.Browser.Controls;
using Lumui.Browser.Presentation;
using Lumui.Client;

namespace Lumui.Browser.Rendering;

public sealed class ViewerWorkspaceRenderer : IDisposable
{
    private const String WorkspaceProfile = "viewer-workspace";
    private const Int32 MaximumDepth = 8;
    private readonly LumuiClient _client;
    private readonly Uri _viewerUri;
    private readonly RendererSettings _settings;
    private readonly Func<Uri, Task> _openExternal;
    private readonly Func<Uri, Task> _download;
    private readonly IReadOnlySet<String> _ancestry;
    private readonly Func<JsonElement, String?>? _inputSuggestion;
    private readonly Boolean _embedded;
    private readonly CancellationTokenSource _lifetime =
        new CancellationTokenSource();
    private readonly SemanticAssetLoader _assetLoader;
    private readonly IReadOnlyList<DeviceProfileDefinition> _profiles;
    private readonly Grid _workspace = new Grid();
    private readonly Grid _stageSlots = new Grid();
    private readonly ContentControl _panelHost = new ContentControl();
    private readonly TextBox _address = new TextBox();
    private readonly TextBlock _statusText = new TextBlock();
    private readonly Border _statusDot = new Border();
    private readonly TextBlock _deviceName = new TextBlock();
    private readonly TextBlock _deviceDimensions = new TextBlock();
    private readonly Button _backButton = new Button();
    private readonly Button _forwardButton = new Button();
    private readonly ViewerWorkspaceSlot _primary;
    private readonly ViewerWorkspaceSlot _secondary;
    private ScrollViewer? _primaryViewport;
    private ScrollViewer? _secondaryViewport;
    private Boolean _compare;
    private Boolean _disposed;
    private Double? _previewScale;

    public ViewerWorkspaceRenderer(
        LumuiClient client,
        Uri viewerUri,
        RendererSettings settings,
        Func<Uri, Task> openExternal,
        Func<Uri, Task> download,
        IReadOnlySet<String> ancestry,
        Func<JsonElement, String?>? inputSuggestion,
        Boolean embedded)
    {
        _client = client;
        _viewerUri = viewerUri;
        _settings = settings;
        _openExternal = openExternal;
        _download = download;
        _ancestry = ancestry;
        _inputSuggestion = inputSuggestion;
        _embedded = embedded;
        _assetLoader = new SemanticAssetLoader(
            client,
            viewerUri,
            message => SetStatus(message, true),
            _lifetime.Token);
        _profiles = ViewerDeviceCatalog.All;
        _primary = CreateSlot(_profiles[0]);
        _secondary = CreateSlot(_profiles[4]);
    }

    public static Boolean Matches(JsonElement surface)
    {
        return surface.TryGetProperty("metadata", out JsonElement metadata)
            && metadata.ValueKind == JsonValueKind.Object
            && metadata.TryGetProperty("renderer", out JsonElement renderer)
            && renderer.ValueKind == JsonValueKind.Object
            && renderer.TryGetProperty("profile", out JsonElement profile)
            && profile.ValueKind == JsonValueKind.String
            && String.Equals(
                profile.GetString(),
                WorkspaceProfile,
                StringComparison.Ordinal);
    }

    public Control Render(JsonElement surface)
    {
        Control? unavailable = UnavailableWorkspace();
        if (unavailable is not null)
        {
            return unavailable;
        }
        Grid application = BeginWorkspace(surface);
        AddStage(application);
        AddPanel(application);
        return StartWorkspace(surface);
    }

    public async Task<Control> RenderAsync(
        JsonElement surface,
        CancellationToken cancellationToken = default)
    {
        Control? unavailable = UnavailableWorkspace();
        if (unavailable is not null)
        {
            return unavailable;
        }
        Grid application = BeginWorkspace(surface);
        await YieldAsync(cancellationToken);
        AddStage(application);
        await YieldAsync(cancellationToken);
        AddPanel(application);
        await YieldAsync(cancellationToken);
        return StartWorkspace(surface);
    }

    private Control? UnavailableWorkspace()
    {
        if (_ancestry.Count >= MaximumDepth)
        {
            return Message(
                "Viewer depth reached",
                "Return to an earlier viewer before opening another nested viewer.");
        }
        if (_settings.Profile.Kind is DeviceProfileKind.Watch
            or DeviceProfileKind.Appliance)
        {
            return Message(
                "Use a larger screen",
                "The full viewer is available on phones, tablets, computers, kiosks and large displays.");
        }
        return null;
    }

    private Grid BeginWorkspace(JsonElement surface)
    {
        _workspace.ColumnDefinitions = new ColumnDefinitions("*");
        _workspace.RowDefinitions = new RowDefinitions("*");
        _workspace.MinHeight = _embedded ? WorkspaceHeight() : 0D;
        _workspace.HorizontalAlignment = HorizontalAlignment.Stretch;
        _workspace.VerticalAlignment = VerticalAlignment.Stretch;
        _workspace.Background = Brush("#FFFFFF");

        Grid application = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Background = Brush("#FFFFFF")
        };
        application.Children.Add(Header(surface));
        _workspace.Children.Add(application);
        return application;
    }

    private void AddStage(Grid application)
    {
        Control stage = Stage();
        application.Children.Add(stage);
        Grid.SetRow(stage, 1);
    }

    private void AddPanel(Grid application)
    {
        _panelHost.Background = Brush("#FFFFFF");
        _panelHost.BorderBrush = Brush("#B8B8B8");
        _panelHost.BorderThickness = new Thickness(1D, 0D, 0D, 0D);
        _panelHost.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _panelHost.VerticalContentAlignment = VerticalAlignment.Stretch;
        _panelHost.IsVisible = false;
        _panelHost.HorizontalAlignment = HorizontalAlignment.Right;
        application.Children.Add(_panelHost);
        Grid.SetRow(_panelHost, 1);
    }

    private Control StartWorkspace(JsonElement surface)
    {
        Uri source = SourceAddress(surface);
        _address.Text = source.AbsoluteUri;
        UpdateSlotState();
        RebuildStage();
        _ = _primary.OpenAsync(source);
        return _workspace;
    }

    private static async Task YieldAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.Yield(DispatcherPriority.Background);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private ViewerWorkspaceSlot CreateSlot(DeviceProfileDefinition profile)
    {
        ViewerWorkspaceSlot slot = new ViewerWorkspaceSlot(
            _client,
            _settings,
            profile,
            _openExternal,
            _download,
            _ancestry,
            _inputSuggestion);
        slot.Changed += SlotChanged;
        slot.StatusChanged += SlotStatusChanged;
        slot.ViewportResetRequested += SlotViewportResetRequested;
        return slot;
    }

    private Control Header(JsonElement surface)
    {
        if (_settings.Profile.Kind is DeviceProfileKind.Phone
            or DeviceProfileKind.Tablet)
        {
            return CompactHeader(surface);
        }
        Grid header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
            ColumnSpacing = 14D,
            MinHeight = 66D,
            Background = Brush("#FFFFFF")
        };

        Control brand = Brand(surface);
        header.Children.Add(brand);

        StackPanel history = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5D,
            VerticalAlignment = VerticalAlignment.Center
        };
        ConfigureIconButton(_backButton, BrowserIcons.Back, "Back");
        ConfigureIconButton(_forwardButton, BrowserIcons.Forward, "Forward");
        Button reload = new Button();
        ConfigureIconButton(reload, BrowserIcons.Reload, "Reload");
        _backButton.Click += async (_, _) => await _primary.GoBackAsync();
        _forwardButton.Click += async (_, _) => await _primary.GoForwardAsync();
        reload.Click += async (_, _) => await _primary.ReloadAsync();
        history.Children.Add(_backButton);
        history.Children.Add(_forwardButton);
        history.Children.Add(reload);
        header.Children.Add(history);
        Grid.SetColumn(history, 1);

        Grid addressForm = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 8D,
            Margin = new Thickness(0D, 8D),
            VerticalAlignment = VerticalAlignment.Stretch
        };
        addressForm.Children.Add(new TextBlock
        {
            Text = "■",
            Foreground = Brush(_settings.Appearance.Accent),
            VerticalAlignment = VerticalAlignment.Center
        });
        _address.MinHeight = 46D;
        _address.Padding = new Thickness(12D, 8D);
        _address.BorderBrush = Brush("#777777");
        _address.BorderThickness = new Thickness(2D);
        _address.CornerRadius = new CornerRadius(0D);
        _address.FontSize = 16D;
        _address.PlaceholderText = "example.com or https://…";
        _address.KeyDown += async (_, eventArgs) =>
        {
            if (eventArgs.Key == Avalonia.Input.Key.Enter)
            {
                eventArgs.Handled = true;
                await OpenAddressAsync();
            }
        };
        addressForm.Children.Add(_address);
        Grid.SetColumn(_address, 1);
        Button open = PrimaryButton("Open");
        open.Click += async (_, _) => await OpenAddressAsync();
        addressForm.Children.Add(open);
        Grid.SetColumn(open, 2);
        header.Children.Add(addressForm);
        Grid.SetColumn(addressForm, 2);

        StackPanel tools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5D,
            Margin = new Thickness(0D, 8D, 12D, 8D),
            VerticalAlignment = VerticalAlignment.Center
        };
        Button devices = ToolbarButton("▣", "Devices");
        Button view = ToolbarButton("◐", "View");
        Button compare = ToolbarButton("▥", "Compare");
        Button inspect = ToolbarButton("⌘", "Tools");
        devices.Click += (_, _) => OpenPanel(DevicePanel(), 420D);
        view.Click += (_, _) => OpenPanel(ViewPanel(), 440D);
        compare.Click += async (_, _) => await ToggleCompareAsync();
        inspect.Click += (_, _) => OpenPanel(ToolsPanel(), 540D);
        tools.Children.Add(devices);
        tools.Children.Add(view);
        tools.Children.Add(compare);
        tools.Children.Add(inspect);
        header.Children.Add(tools);
        Grid.SetColumn(tools, 3);

        return new Border
        {
            BorderBrush = Brush(_settings.Appearance.Accent),
            BorderThickness = new Thickness(0D, 0D, 0D, 4D),
            Child = header
        };
    }

    private Control CompactHeader(JsonElement surface)
    {
        Grid header = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Background = Brush("#FFFFFF")
        };
        header.Children.Add(Brand(surface));

        StackPanel history = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2D,
            Margin = new Thickness(0D, 8D, 8D, 6D),
            VerticalAlignment = VerticalAlignment.Center
        };
        ConfigureIconButton(_backButton, BrowserIcons.Back, "Back");
        ConfigureIconButton(_forwardButton, BrowserIcons.Forward, "Forward");
        Button reload = new Button();
        ConfigureIconButton(reload, BrowserIcons.Reload, "Reload");
        _backButton.Click += async (_, _) => await _primary.GoBackAsync();
        _forwardButton.Click += async (_, _) => await _primary.GoForwardAsync();
        reload.Click += async (_, _) => await _primary.ReloadAsync();
        history.Children.Add(_backButton);
        history.Children.Add(_forwardButton);
        history.Children.Add(reload);
        header.Children.Add(history);
        Grid.SetColumn(history, 1);

        Grid addressForm = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 6D,
            Margin = new Thickness(10D, 2D, 10D, 6D)
        };
        addressForm.Children.Add(new TextBlock
        {
            Text = "■",
            Foreground = Brush(_settings.Appearance.Accent),
            VerticalAlignment = VerticalAlignment.Center
        });
        _address.MinHeight = 42D;
        _address.Padding = new Thickness(10D, 7D);
        _address.BorderBrush = Brush("#777777");
        _address.BorderThickness = new Thickness(2D);
        _address.CornerRadius = new CornerRadius(0D);
        _address.PlaceholderText = "Website address";
        _address.KeyDown += async (_, eventArgs) =>
        {
            if (eventArgs.Key == Avalonia.Input.Key.Enter)
            {
                eventArgs.Handled = true;
                await OpenAddressAsync();
            }
        };
        addressForm.Children.Add(_address);
        Grid.SetColumn(_address, 1);
        Button open = PrimaryButton("Open");
        open.Click += async (_, _) => await OpenAddressAsync();
        addressForm.Children.Add(open);
        Grid.SetColumn(open, 2);
        header.Children.Add(addressForm);
        Grid.SetRow(addressForm, 1);
        Grid.SetColumnSpan(addressForm, 2);

        WrapPanel tools = new WrapPanel
        {
            Margin = new Thickness(8D, 0D, 8D, 7D),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Button devices = ToolbarButton("▣", "Devices");
        Button view = ToolbarButton("◐", "View");
        Button compare = ToolbarButton("▥", "Compare");
        Button inspect = ToolbarButton("⌘", "Tools");
        devices.Click += (_, _) => OpenPanel(DevicePanel(), 340D);
        view.Click += (_, _) => OpenPanel(ViewPanel(), 340D);
        compare.Click += async (_, _) => await ToggleCompareAsync();
        inspect.Click += (_, _) => OpenPanel(ToolsPanel(), 360D);
        tools.Children.Add(devices);
        tools.Children.Add(view);
        tools.Children.Add(compare);
        tools.Children.Add(inspect);
        header.Children.Add(tools);
        Grid.SetRow(tools, 2);
        Grid.SetColumnSpan(tools, 2);
        return new Border
        {
            BorderBrush = Brush("#B8B8B8"),
            BorderThickness = new Thickness(0D, 0D, 0D, 1D),
            Child = header
        };
    }

    private Control Brand(JsonElement surface)
    {
        Grid brand = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto"),
            ColumnSpacing = 9D,
            Margin = new Thickness(12D, 7D),
            VerticalAlignment = VerticalAlignment.Center
        };
        ContentControl mark = new ContentControl
        {
            Width = 42D,
            Height = 42D,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        brand.Children.Add(mark);
        StackPanel copy = new StackPanel
        {
            Spacing = 0D,
            VerticalAlignment = VerticalAlignment.Center
        };
        copy.Children.Add(new TextBlock
        {
            Text = "lumui",
            FontWeight = FontWeight.Light,
            FontSize = 24D,
            Foreground = Brush("#111111")
        });
        copy.Children.Add(new TextBlock
        {
            Text = "VIEWER",
            FontSize = 11D,
            FontWeight = FontWeight.Bold,
            LetterSpacing = 1.1D,
            Foreground = Brush("#5A5A5A")
        });
        brand.Children.Add(copy);
        Grid.SetColumn(copy, 1);

        if (TryLogo(surface, out Uri? uri, out String mediaType)
            && uri is not null)
        {
            _ = _assetLoader.LoadAsync(mark, uri, mediaType);
        }
        return brand;
    }

    private Control Stage()
    {
        IBrush canvas = StageBrush();
        Grid stage = new Grid
        {
            RowDefinitions = new RowDefinitions("52,*,27"),
            Background = Brushes.White
        };
        stage.Children.Add(StageToolbar());
        Grid canvasHost = new Grid
        {
            Background = canvas,
            ClipToBounds = true
        };
        _stageSlots.Background = Brushes.Transparent;
        canvasHost.Children.Add(_stageSlots);
        stage.Children.Add(canvasHost);
        Grid.SetRow(canvasHost, 1);
        Control statusBar = StatusBar();
        stage.Children.Add(statusBar);
        Grid.SetRow(statusBar, 2);
        return stage;
    }

    private Control StageToolbar()
    {
        Grid toolbar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Background = Brush("#FFFFFF"),
            MinHeight = 52D
        };
        StackPanel current = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12D,
            Margin = new Thickness(16D, 0D),
            VerticalAlignment = VerticalAlignment.Center
        };
        _deviceName.FontWeight = FontWeight.Bold;
        _deviceName.Foreground = Brush("#111111");
        _deviceDimensions.Foreground = Brush("#5A5A5A");
        current.Children.Add(_deviceName);
        current.Children.Add(_deviceDimensions);
        current.Children.Add(new TextBlock
        {
            Text = "Full page",
            Foreground = Brush("#5A5A5A")
        });
        toolbar.Children.Add(current);

        StackPanel actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8D,
            Margin = new Thickness(0D, 5D, 14D, 5D),
            VerticalAlignment = VerticalAlignment.Center
        };
        actions.Children.Add(new TextBlock
        {
            Text = "Zoom",
            Foreground = Brush("#5A5A5A"),
            VerticalAlignment = VerticalAlignment.Center
        });
        ComboBox zoom = new ComboBox
        {
            ItemsSource = new String[] { "Fit", "50%", "75%", "100%", "125%" },
            SelectedIndex = 0,
            MinWidth = 76D,
            MinHeight = 40D
        };
        zoom.SelectionChanged += async (_, _) =>
        {
            _previewScale = zoom.SelectedIndex switch
            {
                1 => 0.5D,
                2 => 0.75D,
                3 => 1D,
                4 => 1.25D,
                _ => null
            };
            await RebuildStageAsync();
        };
        actions.Children.Add(zoom);
        Button rotate = QuietButton("Rotate");
        rotate.Click += async (_, _) => await RotatePrimaryAsync();
        actions.Children.Add(rotate);
        toolbar.Children.Add(actions);
        Grid.SetColumn(actions, 1);
        return new Border
        {
            BorderBrush = Brush("#B8B8B8"),
            BorderThickness = new Thickness(0D, 0D, 0D, 1D),
            Child = toolbar
        };
    }

    private Control StatusBar()
    {
        Grid status = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Background = Brush("#FFFFFF"),
            MinHeight = 27D
        };
        _statusDot.Width = 8D;
        _statusDot.Height = 8D;
        _statusDot.CornerRadius = new CornerRadius(4D);
        _statusDot.Background = Brush("#43A97D");
        _statusDot.Margin = new Thickness(14D, 0D, 8D, 0D);
        _statusDot.VerticalAlignment = VerticalAlignment.Center;
        status.Children.Add(_statusDot);
        _statusText.Text = "Ready";
        _statusText.FontSize = 12D;
        _statusText.Foreground = Brush("#5A5A5A");
        _statusText.VerticalAlignment = VerticalAlignment.Center;
        status.Children.Add(_statusText);
        Grid.SetColumn(_statusText, 1);
        TextBlock summary = new TextBlock
        {
            Text = "LUMUI",
            FontSize = 12D,
            Foreground = Brush("#5A5A5A"),
            Margin = new Thickness(10D, 0D, 14D, 0D),
            VerticalAlignment = VerticalAlignment.Center
        };
        status.Children.Add(summary);
        Grid.SetColumn(summary, 2);
        return new Border
        {
            BorderBrush = Brush("#B8B8B8"),
            BorderThickness = new Thickness(0D, 1D, 0D, 0D),
            Child = status
        };
    }

    private void RebuildStage()
    {
        DetachViewports();
        _stageSlots.Children.Clear();
        BuildStage();
    }

    private async Task RebuildStageAsync()
    {
        DetachViewports();
        _stageSlots.Children.Clear();
        await Dispatcher.Yield(DispatcherPriority.Background);
        BuildStage();
    }

    private void BuildStage()
    {
        _stageSlots.ColumnDefinitions.Clear();
        _stageSlots.RowDefinitions = new RowDefinitions("*");
        _stageSlots.Margin = new Thickness(18D);
        _stageSlots.ColumnSpacing = 22D;
        _stageSlots.ColumnDefinitions.Add(
            new ColumnDefinition(new GridLength(1D, GridUnitType.Star)));
        Control primary = DeviceSlot(_primary, "primary");
        _stageSlots.Children.Add(primary);
        if (_compare)
        {
            _stageSlots.ColumnDefinitions.Add(
                new ColumnDefinition(new GridLength(1D, GridUnitType.Star)));
            Control secondary = DeviceSlot(_secondary, "secondary");
            _stageSlots.Children.Add(secondary);
            Grid.SetColumn(secondary, 1);
        }
    }

    private void DetachViewports()
    {
        if (_primaryViewport is not null)
        {
            _primaryViewport.Content = null;
            _primaryViewport = null;
        }
        if (_secondaryViewport is not null)
        {
            _secondaryViewport.Content = null;
            _secondaryViewport = null;
        }
    }

    private Control DeviceSlot(ViewerWorkspaceSlot slot, String name)
    {
        Grid container = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        StackPanel label = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12D,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0D, 0D, 0D, 10D)
        };
        label.Children.Add(new TextBlock
        {
            Text = slot.Profile.Label,
            FontWeight = FontWeight.Bold,
            Foreground = Brush("#111111"),
            VerticalAlignment = VerticalAlignment.Center
        });
        Button change = new Button
        {
            Content = "Change",
            Background = Brushes.Transparent,
            Foreground = Brush(_settings.Appearance.Accent),
            BorderThickness = new Thickness(0D),
            Padding = new Thickness(2D, 0D),
            MinHeight = 28D,
            Height = 28D,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        change.Click += (_, _) => OpenPanel(DevicePanel(slot), 420D);
        label.Children.Add(change);
        container.Children.Add(label);

        Control fitted = _previewScale is Double scale
            ? new ScrollViewer
            {
                Content = ScaledDeviceFrame(slot, scale),
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            }
            : new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Child = DeviceFrame(slot)
            };
        AutomationProperties.SetName(
            fitted,
            slot.Profile.Label + " application preview " + name);
        container.Children.Add(fitted);
        Grid.SetRow(fitted, 1);
        return container;
    }

    private Control ScaledDeviceFrame(
        ViewerWorkspaceSlot slot,
        Double scale)
    {
        Control frame = DeviceFrame(slot);
        frame.RenderTransform = new ScaleTransform(scale, scale);
        frame.RenderTransformOrigin = RelativePoint.TopLeft;
        return new Grid
        {
            Width = slot.Profile.FrameWidth * scale,
            Height = slot.Profile.FrameHeight * scale,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { frame }
        };
    }

    private Control DeviceFrame(ViewerWorkspaceSlot slot)
    {
        DeviceProfileDefinition profile = slot.Profile;
        Boolean windowFrame = profile.Shape == "web";
        Boolean portable = profile.Shape is "phone"
            or "tablet"
            or "rugged"
            or "scanner";
        Boolean wearable = profile.Shape is "watch"
            or "watch-round"
            or "band";
        Grid frame = new Grid
        {
            Width = profile.FrameWidth,
            Height = profile.FrameHeight,
            RowDefinitions = windowFrame
                ? new RowDefinitions("34,*")
                : new RowDefinitions("*"),
            Background = Brush("#111111")
        };
        if (windowFrame)
        {
            StackPanel dots = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7D,
                Margin = new Thickness(13D, 0D),
                VerticalAlignment = VerticalAlignment.Center
            };
            for (Int32 index = 0; index < 3; index++)
            {
                dots.Children.Add(new Border
                {
                    Width = 7D,
                    Height = 7D,
                    CornerRadius = new CornerRadius(4D),
                    Background = Brush("#777777")
                });
            }
            frame.Children.Add(dots);
        }

        ScrollViewer screen = new ScrollViewer
        {
            Content = slot.Host,
            Background = Brushes.White,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = profile.Kind is DeviceProfileKind.Watch
                or DeviceProfileKind.Kiosk
                or DeviceProfileKind.Appliance
                ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
                : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        if (ReferenceEquals(slot, _primary))
        {
            _primaryViewport = screen;
        }
        else
        {
            _secondaryViewport = screen;
        }
        Border viewport = new Border
        {
            Margin = new Thickness(profile.FrameBorder),
            CornerRadius = new CornerRadius(
                windowFrame
                    ? 9D
                    : Math.Max(0D, profile.FrameRadius - profile.FrameBorder)),
            ClipToBounds = true,
            Background = Brushes.White,
            Child = screen
        };
        frame.Children.Add(viewport);
        if (windowFrame)
        {
            Grid.SetRow(viewport, 1);
            viewport.Margin = new Thickness(
                profile.FrameBorder,
                0D,
                profile.FrameBorder,
                profile.FrameBorder);
        }
        Border body = new Border
        {
            CornerRadius = new CornerRadius(profile.FrameRadius),
            ClipToBounds = true,
            Background = Brush("#111111"),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetY = 10D,
                Blur = 22D,
                Color = Color.FromArgb(34, 11, 48, 42)
            }),
            Child = frame
        };
        if (portable)
        {
            Border speaker = new Border
            {
                Width = profile.Shape == "tablet" ? 72D : 54D,
                Height = 5D,
                CornerRadius = new CornerRadius(3D),
                Background = Brush("#5A5A5A"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0D, Math.Max(5D, profile.FrameBorder / 2D), 0D, 0D),
                IsHitTestVisible = false
            };
            frame.Children.Add(speaker);
        }

        if (wearable)
        {
            Grid wearableFrame = new Grid
            {
                Width = profile.FrameWidth,
                Height = profile.FrameHeight + 120D
            };
            Border strap = new Border
            {
                Width = profile.Shape == "band"
                    ? profile.FrameWidth * 0.7D
                    : profile.FrameWidth * 0.42D,
                CornerRadius = new CornerRadius(26D),
                Background = Brush("#333333"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            wearableFrame.Children.Add(strap);
            body.VerticalAlignment = VerticalAlignment.Center;
            wearableFrame.Children.Add(body);
            return wearableFrame;
        }

        if (profile.Shape is "desktop" or "laptop")
        {
            StackPanel monitor = new StackPanel
            {
                Spacing = 0D,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            monitor.Children.Add(body);
            Border stand = new Border
            {
                Width = profile.Shape == "laptop"
                    ? profile.FrameWidth * 0.76D
                    : profile.FrameWidth * 0.22D,
                Height = profile.Shape == "laptop" ? 22D : 30D,
                CornerRadius = new CornerRadius(0D, 0D, 8D, 8D),
                Background = Gradient("#777777", "#DADADA"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            monitor.Children.Add(stand);
            return monitor;
        }
        return body;
    }

    private Control DevicePanel(ViewerWorkspaceSlot? target = null)
    {
        ViewerWorkspaceSlot slot = target ?? _primary;
        StackPanel content = PanelContent(
            "PREVIEW",
            "Choose a device",
            "Choose the screen or output you want to test.");
        TextBox search = new TextBox
        {
            PlaceholderText = "Find a device",
            MinHeight = 48D,
            Margin = new Thickness(0D, 8D, 0D, 14D)
        };
        content.Children.Add(search);
        StackPanel devices = new StackPanel { Spacing = 18D };
        PopulateDevices(devices, slot, String.Empty);
        search.TextChanged += (_, _) => PopulateDevices(
            devices,
            slot,
            search.Text ?? String.Empty);
        content.Children.Add(devices);
        return PanelScroll(content);
    }

    private void PopulateDevices(
        StackPanel host,
        ViewerWorkspaceSlot slot,
        String query)
    {
        host.Children.Clear();
        String filter = query.Trim();
        if (filter.Length == 0)
        {
            host.Children.Add(SectionLabel("Quick choices"));
            AdaptiveGridPanel quick = new AdaptiveGridPanel
            {
                MinimumItemWidth = 108D,
                MaximumColumns = 3,
                ColumnSpacing = 8D,
                RowSpacing = 8D
            };
            foreach (DeviceProfileDefinition profile in ViewerDeviceCatalog.Quick)
            {
                quick.Children.Add(DeviceButton(profile, slot, true));
            }
            host.Children.Add(quick);
        }

        IEnumerable<DeviceProfileDefinition> matches = _profiles.Where(profile =>
            filter.Length == 0
            || (profile.Label + " " + profile.Group + " " + profile.Description)
                .Contains(filter, StringComparison.OrdinalIgnoreCase));
        foreach (IGrouping<String, DeviceProfileDefinition> group in matches
            .GroupBy(profile => profile.Group))
        {
            host.Children.Add(SectionLabel(group.Key));
            AdaptiveGridPanel choices = new AdaptiveGridPanel
            {
                MinimumItemWidth = 175D,
                MaximumColumns = 2,
                ColumnSpacing = 8D,
                RowSpacing = 8D
            };
            foreach (DeviceProfileDefinition profile in group)
            {
                choices.Children.Add(DeviceButton(profile, slot, false));
            }
            host.Children.Add(choices);
        }
        if (!matches.Any())
        {
            host.Children.Add(new TextBlock
            {
                Text = "No matching device",
                Foreground = Brush("#5A5A5A"),
                Margin = new Thickness(0D, 12D)
            });
        }
    }

    private Button DeviceButton(
        DeviceProfileDefinition profile,
        ViewerWorkspaceSlot slot,
        Boolean quick)
    {
        Button button = new Button
        {
            Content = DeviceChoice(
                profile,
                quick,
                _settings.Appearance.Accent),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = quick ? 76D : 72D,
            Background = String.Equals(
                profile.Id,
                slot.Profile.Id,
                StringComparison.Ordinal)
                ? Brush(_settings.Appearance.SurfaceAlternate)
                : Brushes.White,
            BorderBrush = Brush(String.Equals(
                profile.Id,
                slot.Profile.Id,
                StringComparison.Ordinal)
                    ? _settings.Appearance.Accent
                    : "#B8B8B8"),
            BorderThickness = String.Equals(
                profile.Id,
                slot.Profile.Id,
                StringComparison.Ordinal)
                    ? new Thickness(4D, 1D, 1D, 1D)
                    : new Thickness(1D),
            CornerRadius = new CornerRadius(0D),
            Padding = new Thickness(12D, 9D)
        };
        button.Click += async (_, _) =>
        {
            ClosePanel();
            await slot.SetProfileAsync(profile);
            await RebuildStageAsync();
        };
        return button;
    }

    private Control ViewPanel()
    {
        StackPanel content = PanelContent(
            "PREFERENCES",
            "View settings",
            "Change how the app looks and reads.");
        content.Children.Add(SectionLabel("Quick settings"));
        AdaptiveGridPanel quick = new AdaptiveGridPanel
        {
            MinimumItemWidth = 150D,
            MaximumColumns = 2,
            ColumnSpacing = 8D,
            RowSpacing = 8D
        };
        Button text = PanelButton("Larger text");
        Button contrast = PanelButton("More contrast");
        Button motion = PanelButton("Less motion");
        Button guided = PanelButton("Step by step");
        text.Click += (_, _) => ApplyToSlots(slot => slot.SetTextScale(1.25D));
        contrast.Click += (_, _) => ApplyToSlots(slot => slot.SetHighContrast(true));
        motion.Click += (_, _) => ApplyToSlots(slot => slot.SetReducedMotion(true));
        guided.Click += (_, _) => ApplyToSlots(slot => slot.SetGuided(true));
        quick.Children.Add(text);
        quick.Children.Add(contrast);
        quick.Children.Add(motion);
        quick.Children.Add(guided);
        content.Children.Add(quick);

        content.Children.Add(SectionLabel("Color theme"));
        ComboBox theme = new ComboBox
        {
            ItemsSource = new String[] { "Light", "Dark" },
            SelectedIndex = String.Equals(
                _settings.Appearance.Id,
                "dark",
                StringComparison.Ordinal)
                ? 1
                : 0,
            MinHeight = 46D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        theme.SelectionChanged += (_, _) => ApplyToSlots(slot =>
            slot.SetAppearance(theme.SelectedIndex == 1
                ? AppearanceCatalog.Dark
                : AppearanceCatalog.Metro));
        content.Children.Add(theme);
        return PanelScroll(content);
    }

    private Control ToolsPanel()
    {
        StackPanel content = PanelContent(
            "INSPECT",
            "Developer tools",
            "See the page, actions and any problems.");
        WrapPanel tabs = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Border detail = new Border
        {
            Margin = new Thickness(0D, 14D, 0D, 0D),
            Padding = new Thickness(18D),
            BorderBrush = Brush("#B8B8B8"),
            BorderThickness = new Thickness(1D),
            CornerRadius = new CornerRadius(0D),
            Background = Brush("#F7F7F7")
        };
        foreach (String name in new String[]
        {
            "Overview",
            "Source",
            "Structure",
            "Network",
            "Problems",
            "Accessibility",
            "Actions",
            "Performance",
            "Security"
        })
        {
            Button button = QuietButton(name);
            button.Margin = new Thickness(0D, 0D, 5D, 5D);
            button.Click += (_, _) => detail.Child = ToolDetail(name);
            tabs.Children.Add(button);
        }
        content.Children.Add(tabs);
        detail.Child = ToolDetail("Overview");
        content.Children.Add(detail);
        return PanelScroll(content);
    }

    private Control ToolDetail(String section)
    {
        String address = _primary.Address?.AbsoluteUri ?? "No application loaded";
        String description = section switch
        {
            "Source" => address,
            "Structure" => "The semantic page is rendered as native controls in reading order.",
            "Network" => "Requests stay inside this viewer session.",
            "Problems" => _primary.Problem
                ?? "No renderer problem has been reported.",
            "Accessibility" => "Text, contrast, motion and reading preferences are applied by the renderer.",
            "Actions" => "Actions are validated and sent to the application that owns this preview.",
            "Performance" => "The preview uses the native renderer and an isolated navigation session.",
            "Security" => "Navigation, actions and assets follow the application origin policy.",
            _ => _primary.Profile.Label + " · " + address
        };
        StackPanel content = new StackPanel { Spacing = 7D };
        content.Children.Add(new TextBlock
        {
            Text = section,
            FontSize = 19D,
            FontWeight = FontWeight.Bold,
            Foreground = Brush("#111111")
        });
        content.Children.Add(new TextBlock
        {
            Text = description,
            Foreground = Brush("#5A5A5A"),
            TextWrapping = TextWrapping.Wrap
        });
        return content;
    }

    private async Task OpenAddressAsync()
    {
        Uri? uri = NormaliseAddress(_address.Text ?? String.Empty);
        if (uri is null)
        {
            SetStatus("Enter a valid application address.", true);
            return;
        }
        ClosePanel();
        await _primary.OpenAsync(uri);
        if (_compare)
        {
            await _secondary.OpenAsync(uri);
        }
    }

    private async Task ToggleCompareAsync()
    {
        _compare = !_compare;
        if (_compare && _primary.Address is Uri uri)
        {
            await _secondary.OpenAsync(uri);
        }
        await RebuildStageAsync();
    }

    private async Task RotatePrimaryAsync()
    {
        DeviceProfileDefinition source = _primary.Profile;
        DeviceProfileDefinition rotated = new DeviceProfileDefinition(
            source.Id + ".rotated",
            source.Label,
            source.Kind,
            source.FrameHeight,
            source.FrameWidth,
            source.FrameBorder,
            source.FrameRadius,
            source.ChromeHeight,
            source.ContentWidth,
            source.Group,
            source.Description,
            source.Shape);
        await _primary.SetProfileAsync(rotated);
        await RebuildStageAsync();
    }

    private void SlotChanged(ViewerWorkspaceSlot slot)
    {
        if (!ReferenceEquals(slot, _primary))
        {
            return;
        }
        if (slot.Address is Uri uri)
        {
            _address.Text = uri.AbsoluteUri;
        }
        UpdateSlotState();
    }

    private void SlotStatusChanged(
        ViewerWorkspaceSlot slot,
        String message,
        Boolean error)
    {
        if (ReferenceEquals(slot, _primary))
        {
            SetStatus(message, error);
        }
    }

    private void SlotViewportResetRequested(ViewerWorkspaceSlot slot)
    {
        ScrollViewer? viewport = ReferenceEquals(slot, _primary)
            ? _primaryViewport
            : _secondaryViewport;
        if (viewport is not null)
        {
            viewport.Offset = default(Vector);
        }
    }

    private void UpdateSlotState()
    {
        ReadingTextFormatter.Apply(
            _deviceName,
            _primary.Profile.Label,
            _settings.BionicReading);
        ReadingTextFormatter.Apply(
            _deviceDimensions,
            $"{_primary.Profile.FrameWidth:0} × {_primary.Profile.FrameHeight:0}",
            _settings.BionicReading);
        _backButton.IsEnabled = _primary.CanGoBack;
        _forwardButton.IsEnabled = _primary.CanGoForward;
    }

    private void SetStatus(String message, Boolean error)
    {
        _statusDot.Background = Brush(error ? "#D24B4B" : "#43A97D");
        ReadingTextFormatter.Apply(
            _statusText,
            message,
            _settings.BionicReading);
    }

    private void ApplyToSlots(Action<ViewerWorkspaceSlot> action)
    {
        action(_primary);
        if (_compare)
        {
            action(_secondary);
        }
    }

    private void OpenPanel(Control content, Double width)
    {
        _panelHost.Content = content;
        _panelHost.Width = UsesCompactWorkspace
            ? Math.Min(width, 360D)
            : width;
        _panelHost.IsVisible = true;
        ReadingTextFormatter.ApplyTree(content, _settings.BionicReading);
    }

    private void ClosePanel()
    {
        _panelHost.Content = null;
        _panelHost.IsVisible = false;
        _panelHost.Width = Double.NaN;
    }

    private Boolean UsesCompactWorkspace =>
        _settings.Profile.Kind is DeviceProfileKind.Phone
            or DeviceProfileKind.Tablet;

    private Double WorkspaceHeight()
    {
        if (_settings.Profile.Kind == DeviceProfileKind.Desktop)
        {
            return 720D;
        }
        Double chrome = _settings.Profile.Shape == "web" ? 34D : 0D;
        Double border = _settings.Profile.Shape == "web"
            ? _settings.Profile.FrameBorder
            : _settings.Profile.FrameBorder * 2D;
        return Math.Max(
            320D,
            _settings.Profile.FrameHeight
                - chrome
                - border);
    }

    private StackPanel PanelContent(
        String eyebrow,
        String title,
        String description)
    {
        StackPanel content = new StackPanel
        {
            Spacing = 12D,
            Margin = new Thickness(24D)
        };
        Grid heading = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };
        StackPanel copy = new StackPanel { Spacing = 5D };
        copy.Children.Add(new TextBlock
        {
            Text = eyebrow,
            Foreground = Brush(_settings.Appearance.Accent),
            FontWeight = FontWeight.Bold,
            FontSize = 12D,
            LetterSpacing = 1.2D
        });
        copy.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brush("#111111"),
            FontWeight = FontWeight.Light,
            FontSize = 34D
        });
        copy.Children.Add(new TextBlock
        {
            Text = description,
            Foreground = Brush("#5A5A5A"),
            TextWrapping = TextWrapping.Wrap
        });
        heading.Children.Add(copy);
        Button close = QuietButton("×");
        AutomationProperties.SetName(close, "Close");
        close.Click += (_, _) => ClosePanel();
        heading.Children.Add(close);
        Grid.SetColumn(close, 1);
        content.Children.Add(heading);
        content.Children.Add(new Border
        {
            Height = 1D,
            Background = Brush("#B8B8B8"),
            Margin = new Thickness(-24D, 4D, -24D, 4D)
        });
        return content;
    }

    private static Control PanelScroll(Control content) =>
        new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };

    private static Control DeviceChoice(
        DeviceProfileDefinition profile,
        Boolean quick,
        String accent)
    {
        TextBlock label = new TextBlock
        {
            Text = profile.Label,
            FontWeight = FontWeight.Bold,
            Foreground = Brush("#111111"),
            TextWrapping = TextWrapping.Wrap
        };
        TextBlock description = new TextBlock
        {
            Text = quick
                ? $"{profile.FrameWidth:0} × {profile.FrameHeight:0}"
                : profile.Description,
            FontSize = 12D,
            Foreground = Brush("#5A5A5A"),
            TextWrapping = TextWrapping.Wrap
        };
        if (quick)
        {
            label.TextAlignment = TextAlignment.Center;
            description.TextAlignment = TextAlignment.Center;
            StackPanel card = new StackPanel
            {
                Spacing = 5D,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            Control icon = DeviceIcon(profile, accent);
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            card.Children.Add(icon);
            card.Children.Add(label);
            card.Children.Add(description);
            return card;
        }

        Grid content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 12D
        };
        Control device = DeviceIcon(profile, accent);
        device.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(device);
        StackPanel copy = new StackPanel
        {
            Spacing = 2D,
            VerticalAlignment = VerticalAlignment.Center
        };
        copy.Children.Add(label);
        copy.Children.Add(description);
        content.Children.Add(copy);
        Grid.SetColumn(copy, 1);
        return content;
    }

    private static Control DeviceIcon(
        DeviceProfileDefinition profile,
        String accent)
    {
        const Double maximumWidth = 36D;
        const Double maximumHeight = 28D;
        Double ratio = Math.Clamp(
            profile.FrameWidth / Math.Max(1D, profile.FrameHeight),
            0.32D,
            3D);
        Double width;
        Double height;
        if (ratio >= maximumWidth / maximumHeight)
        {
            width = maximumWidth;
            height = Math.Max(10D, width / ratio);
        }
        else
        {
            height = maximumHeight;
            width = Math.Max(10D, height * ratio);
        }
        Boolean round = profile.Shape is "watch-round"
            or "appliance-round"
            or "thermostat";
        Boolean compact = profile.Kind is DeviceProfileKind.Phone
            or DeviceProfileKind.Watch;
        Double radius = round
            ? Math.Min(width, height) / 2D
            : compact
                ? Math.Min(6D, width / 2D)
                : 4D;
        Border screen = new Border
        {
            Width = width,
            Height = height,
            BorderBrush = Brush("#626262"),
            BorderThickness = new Thickness(2D),
            CornerRadius = new CornerRadius(radius),
            Child = new Border
            {
                Margin = new Thickness(3D),
                Background = Brush(accent),
                CornerRadius = new CornerRadius(Math.Max(1D, radius - 3D))
            },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        return new Grid
        {
            Width = 40D,
            Height = 32D,
            Children = { screen }
        };
    }

    private TextBlock SectionLabel(String text) =>
        new TextBlock
        {
            Text = text.ToUpperInvariant(),
            Foreground = Brush(_settings.Appearance.Accent),
            FontSize = 12D,
            FontWeight = FontWeight.Bold,
            LetterSpacing = 1.1D,
            Margin = new Thickness(0D, 10D, 0D, 2D)
        };

    private static Button PanelButton(String text)
    {
        Button button = QuietButton(text);
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.BorderBrush = Brush("#B8B8B8");
        button.BorderThickness = new Thickness(1D);
        return button;
    }

    private Button PrimaryButton(String text) =>
        new Button
        {
            Content = text,
            MinHeight = 44D,
            Padding = new Thickness(18D, 8D),
            Background = Brush(_settings.Appearance.Accent),
            Foreground = Brushes.White,
            BorderBrush = Brush(_settings.Appearance.Accent),
            BorderThickness = new Thickness(2D),
            CornerRadius = new CornerRadius(0D),
            FontWeight = FontWeight.SemiBold
        };

    private static Button ToolbarButton(String symbol, String label)
    {
        StackPanel content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6D
        };
        content.Children.Add(new TextBlock { Text = symbol });
        content.Children.Add(new TextBlock { Text = label });
        Button button = QuietButton(String.Empty);
        button.Content = content;
        AutomationProperties.SetName(button, label);
        return button;
    }

    private static Button QuietButton(String text) =>
        new Button
        {
            Content = text,
            MinHeight = 40D,
            Padding = new Thickness(12D, 7D),
            Background = Brushes.Transparent,
            Foreground = Brush("#111111"),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1D),
            CornerRadius = new CornerRadius(0D)
        };

    private static void ConfigureIconButton(
        Button button,
        String icon,
        String name)
    {
        button.Content = new FontAwesomeIcon
        {
            Icon = icon,
            IconSize = 16D,
            Foreground = Brush("#111111"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Classes.Add("viewer-history");
        button.Width = 42D;
        button.Height = 42D;
        button.Padding = new Thickness(0D);
        button.Background = Brushes.Transparent;
        button.Foreground = Brush("#111111");
        button.BorderThickness = new Thickness(0D);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(button, name);
    }

    private Uri SourceAddress(JsonElement surface)
    {
        String source = "/";
        if (surface.TryGetProperty("state", out JsonElement state)
            && state.ValueKind == JsonValueKind.Object)
        {
            source = Text(state, "source_surface", source);
        }
        return Uri.TryCreate(source, UriKind.Absolute, out Uri? absolute)
            ? absolute
            : new Uri(_viewerUri, source);
    }

    private Uri? NormaliseAddress(String value)
    {
        String candidate = value.Trim();
        if (candidate.Length == 0)
        {
            return null;
        }
        if (!candidate.Contains("://", StringComparison.Ordinal))
        {
            candidate = "https://" + candidate;
        }
        return Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri)
            ? uri
            : null;
    }

    private Boolean TryLogo(
        JsonElement surface,
        out Uri? uri,
        out String mediaType)
    {
        uri = null;
        mediaType = String.Empty;
        if (!surface.TryGetProperty("identity", out JsonElement identity)
            || identity.ValueKind != JsonValueKind.Object
            || !identity.TryGetProperty("logo", out JsonElement logo)
            || logo.ValueKind != JsonValueKind.Object)
        {
            return false;
        }
        String source = Text(logo, "source");
        if (source.Length == 0)
        {
            return false;
        }
        uri = Uri.TryCreate(source, UriKind.Absolute, out Uri? absolute)
            ? absolute
            : new Uri(_viewerUri, source);
        mediaType = Text(logo, "type");
        return true;
    }

    private static String Text(
        JsonElement element,
        String name,
        String fallback = "") =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static IBrush StageBrush()
    {
        return Brush("#EDEDED");
    }

    private static IBrush Gradient(String start, String end)
    {
        GradientStops stops = new GradientStops
        {
            new GradientStop(Color.Parse(start), 0D),
            new GradientStop(Color.Parse(end), 1D)
        };
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0D, 0D, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1D, 1D, RelativeUnit.Relative),
            GradientStops = stops
        };
    }

    private static Control Message(String title, String description)
    {
        StackPanel content = new StackPanel
        {
            Spacing = 10D,
            MaxWidth = 560D,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 38D,
            FontWeight = FontWeight.Light,
            Foreground = Brush("#111111"),
            TextAlignment = TextAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = description,
            Foreground = Brush("#5A5A5A"),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });
        return new Grid
        {
            MinHeight = 620D,
            Background = Brushes.White,
            Children = { content }
        };
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
        _lifetime.Cancel();
        _primary.Changed -= SlotChanged;
        _primary.StatusChanged -= SlotStatusChanged;
        _primary.ViewportResetRequested -= SlotViewportResetRequested;
        _secondary.Changed -= SlotChanged;
        _secondary.StatusChanged -= SlotStatusChanged;
        _secondary.ViewportResetRequested -= SlotViewportResetRequested;
        _primary.Dispose();
        _secondary.Dispose();
        _lifetime.Dispose();
    }
}
