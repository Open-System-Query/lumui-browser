using System.Text;
using System.Text.Json;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Lumui.Browser.Presentation;
using Lumui.Browser.Shell;
using Lumui.Client;

namespace Lumui.Browser.DeveloperTools;

public sealed partial class DeveloperToolsPanel : UserControl
{
    private readonly JsonSourcePresenter _sourcePresenter;
    private readonly ObservableCollection<RequestTraceViewModel> _requestItems =
        new ObservableCollection<RequestTraceViewModel>();
    private readonly StringBuilder _diagnostics = new StringBuilder();
    private readonly List<String> _problemEntries = new List<String>();
    private readonly SemaphoreSlim _inspectionGate = new SemaphoreSlim(1, 1);
    private String _rawSource = String.Empty;
    private String _formattedSource = String.Empty;
    private String _displayedSource = String.Empty;
    private Boolean _sourcePending;
    private TimeSpan _loadDuration;
    private JsonElement _surfaceRoot;
    private Boolean _structurePending;
    private CancellationTokenSource? _inspectionCancellation;
    private LoadedSurface? _inspectedSurface;
    private LoadedSurface? _pendingSurface;
    private RendererSettings? _pendingSettings;
    private TimeSpan _pendingLoadDuration;

    public DeveloperToolsPanel()
        : this(null)
    {
    }

    public DeveloperToolsPanel(BrowserShellRenderer? renderer)
    {
        InitializeComponent();
        _sourcePresenter = new JsonSourcePresenter();
        SourceHost.Content = _sourcePresenter;
        FindButton.Click += FindClicked;
        RawButton.Click += RawClicked;
        FormatButton.Click += FormattedClicked;
        CopyButton.Click += CopyClicked;
        SaveButton.Click += SaveClicked;
        ClearNetworkButton.Click += ClearNetworkClicked;
        ClearDiagnosticsButton.Click += ClearDiagnosticsClicked;
        RequestList.SelectionChanged += RequestSelectionChanged;
        SemanticTree.SelectionChanged += SemanticTreeSelectionChanged;
        RequestList.ItemsSource = _requestItems;
        OverviewNavButton.Click += (_, _) => SelectPage("Overview");
        SourceNavButton.Click += (_, _) => SelectPage("Source");
        StructureNavButton.Click += (_, _) => SelectPage("Structure");
        NetworkNavButton.Click += (_, _) => SelectPage("Network");
        ProblemsNavButton.Click += (_, _) => SelectPage("Problems");
        AccessibilityNavButton.Click += (_, _) => SelectPage("Accessibility");
        ActionsNavButton.Click += (_, _) => SelectPage("Actions");
        DiagnosticsNavButton.Click += (_, _) => SelectPage("Diagnostics");
        renderer?.ApplyControl(ToolsNavigation, "tools.tabs");
    }

    public void SetSurface(
        LoadedSurface loaded,
        RendererSettings settings,
        TimeSpan loadDuration)
    {
        _pendingSurface = loaded;
        _pendingSettings = settings;
        _pendingLoadDuration = loadDuration;
        _loadDuration = loadDuration;
        if (TopLevel.GetTopLevel(this) is null)
        {
            _inspectionCancellation?.Cancel();
            _inspectedSurface = null;
            return;
        }
        String source = loaded.Source;
        Uri address = loaded.Address;
        Uri surfaceUri = loaded.SurfaceUri;
        Uri? descriptorUri = loaded.DescriptorUri;
        Uri? actionUri = loaded.ActionUri;
        String? entityTag = loaded.EntityTag?.ToString();
        StartInspection(async (CancellationToken token) =>
        {
            await _inspectionGate.WaitAsync(token);
            (JsonElement root,
                String formatted,
                String overview,
                IReadOnlyList<String> problems,
                String accessibility,
                String actions) result;
            try
            {
                result = await Task.Run(
                    () =>
                    {
                        token.ThrowIfCancellationRequested();
                        JsonElement root = ParseSurface(source);
                        return (
                            root,
                            Format(root),
                            DocumentInspector.Describe(
                                root,
                                address,
                                surfaceUri,
                                descriptorUri,
                                actionUri,
                                entityTag,
                                source,
                                settings,
                                loadDuration),
                            ProblemInspector.Find(root),
                            AccessibilityInspector.Describe(root, settings),
                            ActionInspector.Describe(root));
                    },
                    token);
            }
            finally
            {
                _inspectionGate.Release();
            }
            token.ThrowIfCancellationRequested();
            _rawSource = source;
            _formattedSource = result.formatted;
            ShowSource(_formattedSource);
            OverviewText.Text = result.overview;
            _surfaceRoot = result.root;
            _structurePending = true;
            SemanticTree.ItemsSource = Array.Empty<TreeViewItem>();
            if (StructurePage.IsVisible)
            {
                EnsureStructure();
            }
            InspectorText.Text = _formattedSource;
            _problemEntries.Clear();
            _problemEntries.AddRange(result.problems);
            UpdateProblems();
            AccessibilityText.Text = result.accessibility;
            ActionsText.Text = result.actions;
            _inspectedSurface = loaded;
            AddLog(DeveloperLogLevel.Information, DeveloperToolsText.Validated);
        });
    }

    public void SetSettings(
        LoadedSurface loaded,
        RendererSettings settings)
    {
        _pendingSurface = loaded;
        _pendingSettings = settings;
        if (TopLevel.GetTopLevel(this) is null)
        {
            return;
        }
        if (!ReferenceEquals(_inspectedSurface, loaded))
        {
            SetSurface(loaded, settings, _loadDuration);
            return;
        }
        String source = loaded.Source;
        Uri address = loaded.Address;
        Uri surfaceUri = loaded.SurfaceUri;
        Uri? descriptorUri = loaded.DescriptorUri;
        Uri? actionUri = loaded.ActionUri;
        String? entityTag = loaded.EntityTag?.ToString();
        StartInspection(async (CancellationToken token) =>
        {
            await _inspectionGate.WaitAsync(token);
            (String overview, String accessibility) result;
            try
            {
                result = await Task.Run(
                    () =>
                    {
                        token.ThrowIfCancellationRequested();
                        JsonElement root = ParseSurface(source);
                        return (
                            DocumentInspector.Describe(
                                root,
                                address,
                                surfaceUri,
                                descriptorUri,
                                actionUri,
                                entityTag,
                                source,
                                settings,
                                _loadDuration),
                            AccessibilityInspector.Describe(root, settings));
                    },
                    token);
            }
            finally
            {
                _inspectionGate.Release();
            }
            token.ThrowIfCancellationRequested();
            OverviewText.Text = result.overview;
            AccessibilityText.Text = result.accessibility;
            AddLog(
                DeveloperLogLevel.Information,
                DeveloperToolsText.Presentation(
                    settings.Profile.Label,
                    settings.Appearance.Label,
                    settings.Output.Label,
                    settings.Interaction.Label,
                    settings.AccessibilitySummary));
        });
    }

    private static JsonElement ParseSurface(String source)
    {
        using JsonDocument document = JsonDocument.Parse(source);
        return document.RootElement.Clone();
    }

    private void StartInspection(Func<CancellationToken, Task> inspect)
    {
        _inspectionCancellation?.Cancel();
        CancellationTokenSource request = new CancellationTokenSource();
        _inspectionCancellation = request;
        _ = RunInspectionAsync(inspect, request);
    }

    private async Task RunInspectionAsync(
        Func<CancellationToken, Task> inspect,
        CancellationTokenSource request)
    {
        try
        {
            await inspect(request.Token);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            AddException(exception);
        }
        finally
        {
            if (ReferenceEquals(_inspectionCancellation, request))
            {
                _inspectionCancellation = null;
            }
            request.Dispose();
        }
    }

    public void AddRequest(LumuiRequestTrace trace)
    {
        RequestTraceViewModel item = new RequestTraceViewModel(trace);
        _requestItems.Add(item);
        if (_requestItems.Count > 1000)
        {
            _requestItems.RemoveAt(0);
        }
        if (!trace.Succeeded)
        {
            _problemEntries.Add(
                trace.Method
                + " "
                + trace.RequestUri
                + ": "
                + (trace.Error.Length > 0
                    ? trace.Error
                    : "request failed"));
            UpdateProblems();
        }
        AddLog(
            trace.Succeeded
                ? DeveloperLogLevel.Information
                : DeveloperLogLevel.Error,
            DeveloperToolsText.RequestResult(
                trace.Method,
                trace.RequestUri,
                trace.StatusCode?.ToString()
                    ?? DeveloperToolsText.RequestError,
                trace.Duration.TotalMilliseconds));
    }

    public void SetRequests(IReadOnlyList<LumuiRequestTrace> traces)
    {
        _requestItems.Clear();
        foreach (LumuiRequestTrace trace in traces.TakeLast(1000))
        {
            _requestItems.Add(new RequestTraceViewModel(trace));
        }
    }

    public void AddLog(
        DeveloperLogLevel level,
        String message)
    {
        DeveloperLogEntry entry = new DeveloperLogEntry(
            DateTimeOffset.Now,
            level,
            message);
        if (_diagnostics.Length > 0)
        {
            _diagnostics.AppendLine();
        }
        _diagnostics.Append(entry);
        if (_diagnostics.Length > 1_000_000)
        {
            _diagnostics.Remove(0, 500_000);
        }
        if (TopLevel.GetTopLevel(this) is not null)
        {
            DiagnosticsText.Text = _diagnostics.ToString();
        }
    }

    public void AddException(Exception exception)
    {
        AddLog(DeveloperLogLevel.Error, exception.ToString());
        _problemEntries.Add(exception.Message);
        UpdateProblems();
    }

    public void SelectSource()
    {
        SelectPage("Source");
        SourceSearchBox.Focus();
    }

    public event Action? RequestsCleared;

    public void RefreshDisplay()
    {
        DiagnosticsText.Text = _diagnostics.ToString();
        ProblemsText.Text = ProblemInspector.Describe(_problemEntries);
        if (_pendingSurface is null || _pendingSettings is null)
        {
            return;
        }
        if (ReferenceEquals(_inspectedSurface, _pendingSurface))
        {
            SetSettings(_pendingSurface, _pendingSettings);
        }
        else
        {
            SetSurface(
                _pendingSurface,
                _pendingSettings,
                _pendingLoadDuration);
        }
    }

    public void SetDarkMode(Boolean dark, Boolean highContrast)
    {
        SetClass(ToolsRoot, "dark", dark && !highContrast);
        SetClass(ToolsRoot, "high-contrast", highContrast);
    }

    private void ShowSource(String source)
    {
        _displayedSource = source;
        _sourcePending = true;
        EnsureSource();
    }

    private void SelectPage(String page)
    {
        OverviewPage.IsVisible = page == "Overview";
        SourcePage.IsVisible = page == "Source";
        StructurePage.IsVisible = page == "Structure";
        NetworkPage.IsVisible = page == "Network";
        ProblemsPage.IsVisible = page == "Problems";
        AccessibilityPage.IsVisible = page == "Accessibility";
        ActionsPage.IsVisible = page == "Actions";
        DiagnosticsPage.IsVisible = page == "Diagnostics";
        SetSelected(OverviewNavButton, OverviewPage.IsVisible);
        SetSelected(SourceNavButton, SourcePage.IsVisible);
        SetSelected(StructureNavButton, StructurePage.IsVisible);
        SetSelected(NetworkNavButton, NetworkPage.IsVisible);
        SetSelected(ProblemsNavButton, ProblemsPage.IsVisible);
        SetSelected(AccessibilityNavButton, AccessibilityPage.IsVisible);
        SetSelected(ActionsNavButton, ActionsPage.IsVisible);
        SetSelected(DiagnosticsNavButton, DiagnosticsPage.IsVisible);
        if (StructurePage.IsVisible)
        {
            EnsureStructure();
        }
        if (SourcePage.IsVisible)
        {
            EnsureSource();
        }
    }

    private void EnsureSource()
    {
        if (!_sourcePending || !SourcePage.IsVisible)
        {
            return;
        }
        _sourcePresenter.SetText(_displayedSource);
        _sourcePending = false;
    }

    private void EnsureStructure()
    {
        if (!_structurePending)
        {
            return;
        }
        SemanticTree.ItemsSource = SemanticTreeBuilder.Build(_surfaceRoot);
        _structurePending = false;
    }

    private static void SetSelected(Button button, Boolean selected)
    {
        if (selected && !button.Classes.Contains("selected"))
        {
            button.Classes.Add("selected");
        }
        else if (!selected)
        {
            button.Classes.Remove("selected");
        }
    }

    private void UpdateProblems()
    {
        if (TopLevel.GetTopLevel(this) is not null)
        {
            ProblemsText.Text = ProblemInspector.Describe(_problemEntries);
        }
    }

    private void RequestSelectionChanged(
        Object? sender,
        SelectionChangedEventArgs eventArgs)
    {
        RequestText.Text = RequestList.SelectedItem
            is RequestTraceViewModel item
                ? DescribeRequest(item.Trace)
                : String.Empty;
    }

    private void SemanticTreeSelectionChanged(
        Object? sender,
        SelectionChangedEventArgs eventArgs)
    {
        if (SemanticTree.SelectedItem is not TreeViewItem item
            || item.DataContext is not String source)
        {
            return;
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(source);
            InspectorText.Text = Format(document.RootElement);
        }
        catch (JsonException)
        {
            InspectorText.Text = source;
        }
    }

    private static String DescribeRequest(LumuiRequestTrace trace)
    {
        StringBuilder output = new StringBuilder();
        output.AppendLine(trace.Method + " " + trace.RequestUri);
        output.AppendLine(
            DeveloperToolsText.Status
            + ": "
            + (trace.StatusCode?.ToString() ?? DeveloperToolsText.None)
            + " "
            + trace.ReasonPhrase);
        output.AppendLine(
            DeveloperToolsText.FinalAddress
            + ": "
            + (trace.ResponseUri?.AbsoluteUri ?? DeveloperToolsText.None));
        output.AppendLine(
            DeveloperToolsText.ContentType
            + ": "
            + trace.ContentType);
        output.AppendLine(
            DeveloperToolsText.Duration
            + ": "
            + DeveloperToolsText.DurationValue(
                trace.Duration.TotalMilliseconds));
        if (trace.Error.Length > 0)
        {
            output.AppendLine(
                DeveloperToolsText.Error
                + ": "
                + trace.Error);
        }
        AppendHeaders(
            output,
            DeveloperToolsText.RequestHeaders,
            trace.RequestHeaders);
        AppendHeaders(
            output,
            DeveloperToolsText.ResponseHeaders,
            trace.ResponseHeaders);
        return output.ToString();
    }

    private static String Format(JsonElement value)
    {
        using MemoryStream stream = new MemoryStream();
        using (Utf8JsonWriter writer = new Utf8JsonWriter(
            stream,
            new JsonWriterOptions
            {
                Indented = true
            }))
        {
            value.WriteTo(writer);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void AppendHeaders(
        StringBuilder output,
        String title,
        IReadOnlyDictionary<String, String> headers)
    {
        output.AppendLine();
        output.AppendLine(title);
        foreach (KeyValuePair<String, String> header in headers.OrderBy(
            (KeyValuePair<String, String> item) => item.Key,
            StringComparer.OrdinalIgnoreCase))
        {
            output.AppendLine(header.Key + ": " + header.Value);
        }
    }

    private void FindClicked(
        Object? sender,
        RoutedEventArgs eventArgs)
    {
        String query = SourceSearchBox.Text ?? String.Empty;
        if (!_sourcePresenter.Find(query))
        {
            AddLog(
                DeveloperLogLevel.Information,
                DeveloperToolsText.SourceNotFound);
        }
    }

    private void RawClicked(
        Object? sender,
        RoutedEventArgs eventArgs) =>
        ShowSource(_rawSource);

    private void FormattedClicked(
        Object? sender,
        RoutedEventArgs eventArgs) =>
        ShowSource(_formattedSource);

    private async void CopyClicked(
        Object? sender,
        RoutedEventArgs eventArgs)
    {
        try
        {
            IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                return;
            }
            await ClipboardExtensions.SetTextAsync(
                clipboard,
                _displayedSource);
            AddLog(
                DeveloperLogLevel.Information,
                DeveloperToolsText.SourceCopied);
        }
        catch (Exception exception)
        {
            AddException(exception);
        }
    }

    private async void SaveClicked(
        Object? sender,
        RoutedEventArgs eventArgs)
    {
        try
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null)
            {
                return;
            }
            IStorageFile? file =
                await topLevel.StorageProvider.SaveFilePickerAsync(
                    new FilePickerSaveOptions
                    {
                        Title = DeveloperToolsText.SaveSource,
                        SuggestedFileName =
                            DeveloperToolsText.SourceFilename,
                        FileTypeChoices = new FilePickerFileType[]
                        {
                            new FilePickerFileType(
                                DeveloperToolsText.Json)
                            {
                                Patterns = new String[]
                                {
                                    DeveloperToolsText.JsonPattern
                                },
                                MimeTypes = new String[]
                                {
                                    DeveloperToolsText.JsonMediaType
                                }
                            }
                        }
                    });
            if (file is null)
            {
                return;
            }
            await using Stream stream = await file.OpenWriteAsync();
            await using StreamWriter writer = new StreamWriter(stream);
            await writer.WriteAsync(_displayedSource);
            AddLog(
                DeveloperLogLevel.Information,
                DeveloperToolsText.SourceSaved);
        }
        catch (Exception exception)
        {
            AddException(exception);
        }
    }

    private void ClearDiagnosticsClicked(
        Object? sender,
        RoutedEventArgs eventArgs)
    {
        _diagnostics.Clear();
        DiagnosticsText.Text = String.Empty;
    }

    private void ClearNetworkClicked(
        Object? sender,
        RoutedEventArgs eventArgs)
    {
        _requestItems.Clear();
        RequestText.Text = String.Empty;
        RequestsCleared?.Invoke();
    }

    private static void SetClass(Control control, String name, Boolean enabled)
    {
        if (enabled && !control.Classes.Contains(name))
        {
            control.Classes.Add(name);
        }
        else if (!enabled)
        {
            control.Classes.Remove(name);
        }
    }
}
