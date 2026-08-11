using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Lumui.Browser.Configuration;
using Lumui.Browser.Presentation;
using Lumui.Browser.Rendering;

namespace Lumui.Browser.DeveloperTools;

public sealed partial class DeveloperToolsWindow : Window
{
    private const String PlacementKey = "developer-tools";
    private readonly BrowserWindowPlacementStore _placementStore = new BrowserWindowPlacementStore();
    private readonly DeveloperToolsPanel _panel;
    private CancellationTokenSource? _readingCancellation;

    public DeveloperToolsWindow()
        : this(new DeveloperToolsPanel())
    {
    }

    public DeveloperToolsWindow(DeveloperToolsPanel panel)
    {
        InitializeComponent();
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        ToolsHost.Content = _panel;
        AutomationProperties.SetName(this, "Developer tools");
        _placementStore.Apply(this, PlacementKey);
        Opened += WindowOpened;
        Closed += WindowClosed;
        KeyDown += WindowKeyDown;
    }

    private void WindowOpened(Object? sender, EventArgs eventArgs)
    {
        _panel.RefreshDisplay();
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
                _panel,
                Classes.Contains("bionic"),
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

    public void ApplyPreferences(BrowserPreferences preferences)
    {
        Boolean dark = preferences.ColorScheme == BrowserColorScheme.Dark;
        BrowserWindowAppearance.Apply(this, preferences);
        _panel.SetDarkMode(dark, preferences.HighContrast);
        if (IsVisible)
        {
            ScheduleReadingFormat();
        }
    }

    private void WindowKeyDown(Object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.F12 || eventArgs.KeyModifiers == KeyModifiers.Control && eventArgs.Key == Key.W)
        {
            eventArgs.Handled = true;
            Close();
        }
    }

    private void WindowClosed(Object? sender, EventArgs eventArgs)
    {
        _readingCancellation?.Cancel();
        _readingCancellation?.Dispose();
        _readingCancellation = null;
        Opened -= WindowOpened;
        _placementStore.Save(this, PlacementKey);
        ToolsHost.Content = null;
    }

}
