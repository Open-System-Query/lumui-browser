using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Lumui.Browser.Configuration;
using Lumui.Browser.Presentation;

namespace Lumui.Browser.Views;

public sealed partial class BrowserSettingsWindow : Window
{
    private const String PlacementKey = "settings";
    private readonly BrowserWindowPlacementStore _placementStore = new BrowserWindowPlacementStore();
    private readonly BrowserSettingsPanel _panel;

    public BrowserSettingsWindow()
        : this(new BrowserSettingsPanel())
    {
    }

    public BrowserSettingsWindow(BrowserSettingsPanel panel)
    {
        InitializeComponent();
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        SettingsHost.Content = _panel;
        AutomationProperties.SetName(this, "Browser settings");
        _placementStore.Apply(this, PlacementKey);
        Closed += WindowClosed;
        KeyDown += WindowKeyDown;
    }

    public void ApplyPreferences(BrowserPreferences preferences)
    {
        Boolean dark = preferences.ColorScheme == BrowserColorScheme.Dark;
        BrowserWindowAppearance.Apply(this, preferences);
        _panel.SetDarkMode(dark, preferences.HighContrast);
    }

    private void WindowKeyDown(Object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyModifiers == KeyModifiers.Control && eventArgs.Key == Key.W)
        {
            eventArgs.Handled = true;
            Close();
        }
    }

    private void WindowClosed(Object? sender, EventArgs eventArgs)
    {
        _placementStore.Save(this, PlacementKey);
        SettingsHost.Content = null;
    }

}
