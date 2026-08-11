using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Lumui.Browser.Configuration;
using Lumui.Browser.Presentation;
using Lumui.Browser.Shell;

namespace Lumui.Browser.Views;

public sealed partial class PersonalizationPanel : UserControl
{
    private const String DefaultFontLabel = "Default";
    private static readonly String[] AccentLabels = new String[]
    {
        "LUMUI green",
        "Blue",
        "Purple",
        "Red",
        "Orange",
        "Amber",
        "Emerald",
        "Violet"
    };
    private static readonly String[] AccentValues = new String[]
    {
        BrowserPreferences.DefaultAccentColor,
        "#1BA1E2",
        "#A200FF",
        "#E51400",
        "#D24726",
        "#D98A00",
        "#00A300",
        "#6A00FF"
    };
    private readonly BrowserPreferences _preferences;
    private IReadOnlyList<String> _fontFamilies = new String[] { DefaultFontLabel };
    private Task? _fontLoadTask;
    private Boolean _updating;

    public PersonalizationPanel()
        : this(new BrowserPreferences(), null)
    {
    }

    public PersonalizationPanel(BrowserPreferences preferences)
        : this(preferences, null)
    {
    }

    public PersonalizationPanel(
        BrowserPreferences preferences,
        BrowserShellRenderer? renderer)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        InitializeComponent();
        ThemeBox.ItemsSource = new String[] { "Light", "Dark" };
        AccentColorBox.ItemsSource = AccentLabels;
        FontBox.ItemsSource = _fontFamilies;
        ColorVisionBox.ItemsSource = new String[]
        {
            "Default",
            "Red and green",
            "Green and red",
            "Blue and yellow"
        };
        ThemeBox.SelectionChanged += SelectionChanged;
        AccentColorBox.SelectionChanged += SelectionChanged;
        FontBox.SelectionChanged += SelectionChanged;
        ColorVisionBox.SelectionChanged += SelectionChanged;
        TextSizeDownButton.Click += (_, _) => ChangeTextSize(-10);
        TextSizeUpButton.Click += (_, _) => ChangeTextSize(10);
        Bind(BionicReadingToggle);
        Bind(SimpleReadingToggle);
        Bind(HighContrastToggle);
        Bind(ReducedMotionToggle);
        Bind(SeniorModeToggle);
        ResetButton.Click += ResetClicked;
        renderer?.ApplyControl(ThemeBox, "reading.theme");
        renderer?.ApplyControl(FontBox, "reading.font");
        renderer?.ApplyControl(ColorVisionBox, "reading.colorVision");
        renderer?.ApplyControl(BionicReadingToggle, "reading.bionic");
        renderer?.ApplyControl(SimpleReadingToggle, "reading.readingView");
        renderer?.ApplyControl(HighContrastToggle, "reading.highContrast");
        renderer?.ApplyControl(ReducedMotionToggle, "reading.reducedMotion");
        renderer?.ApplyControl(SeniorModeToggle, "reading.guided");
        renderer?.ApplyButton(ResetButton, "reading.restore", true);
        Refresh();
    }

    public event Action? PreferencesChanged;

    public Task PrepareAsync()
    {
        _fontLoadTask ??= LoadFontsAsync();
        return _fontLoadTask;
    }

    public void ShowTextSettings() => SelectPage("Text");

    public void ShowColorSettings() => SelectPage("Colors");

    public void ShowComfortSettings() => SelectPage("Comfort");

    public void Refresh()
    {
        _updating = true;
        ThemeBox.SelectedIndex = (Int32)_preferences.ColorScheme;
        AccentColorBox.SelectedIndex = Array.FindIndex(
            AccentValues,
            (String color) => color.Equals(
                _preferences.AccentColor,
                StringComparison.OrdinalIgnoreCase));
        if (AccentColorBox.SelectedIndex < 0)
        {
            AccentColorBox.SelectedIndex = 0;
        }
        String selectedFont = _preferences.FontFamily;
        if (String.IsNullOrWhiteSpace(selectedFont) && _preferences.Font != FontPreference.Default)
        {
            selectedFont = FontPreferenceCatalog.Resolve(_preferences.Font);
        }
        FontBox.SelectedItem = String.IsNullOrWhiteSpace(selectedFont)
            ? DefaultFontLabel
            : _fontFamilies.FirstOrDefault((String family) => family.Equals(
                selectedFont,
                StringComparison.CurrentCultureIgnoreCase)) ?? DefaultFontLabel;
        ColorVisionBox.SelectedIndex = (Int32)_preferences.ColorVision;
        TextSizeValue.Text = _preferences.TextScalePercent + "%";
        BionicReadingToggle.IsChecked = _preferences.BionicReading;
        SimpleReadingToggle.IsChecked = _preferences.SimpleReadingView;
        HighContrastToggle.IsChecked = _preferences.HighContrast;
        ReducedMotionToggle.IsChecked = _preferences.ReducedMotion;
        SeniorModeToggle.IsChecked = _preferences.SeniorMode;
        ResetButton.IsVisible = HasCustomReadingSettings();
        _updating = false;
    }

    public void SetDarkMode(Boolean dark, Boolean highContrast)
    {
        SetClass(ReadingRoot, "dark", dark && !highContrast);
        SetClass(ReadingRoot, "high-contrast", highContrast);
    }

    private void Bind(ToggleSwitch toggle) => toggle.IsCheckedChanged += SettingChanged;

    private async Task LoadFontsAsync()
    {
        String[] names = FontManager.Current.SystemFonts
            .Select((FontFamily family) => family.Name)
            .Where((String name) => !String.IsNullOrWhiteSpace(name))
            .ToArray();
        String[] sorted = await Task.Run(() => names
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy((String name) => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray());
        _fontFamilies = new String[] { DefaultFontLabel }.Concat(sorted).ToArray();
        _updating = true;
        FontBox.ItemsSource = _fontFamilies;
        _updating = false;
        Refresh();
    }

    private void SelectPage(String page)
    {
        TextPage.IsVisible = page == "Text";
        ColorsPage.IsVisible = page == "Colors";
        ComfortPage.IsVisible = page == "Comfort";
    }

    private void SelectionChanged(Object? sender, SelectionChangedEventArgs eventArgs) => ReadPreferences();

    private void SettingChanged(Object? sender, RoutedEventArgs eventArgs) => ReadPreferences();

    private void ChangeTextSize(Int32 amount)
    {
        _preferences.TextScalePercent = Math.Clamp(_preferences.TextScalePercent + amount, 90, 180);
        Refresh();
        PreferencesChanged?.Invoke();
    }

    private void ReadPreferences()
    {
        if (_updating)
        {
            return;
        }
        _preferences.ColorScheme = EnumValue(ThemeBox.SelectedIndex, BrowserColorScheme.Light);
        _preferences.AccentColor = AccentColorBox.SelectedIndex >= 0
            && AccentColorBox.SelectedIndex < AccentValues.Length
                ? AccentValues[AccentColorBox.SelectedIndex]
                : BrowserPreferences.DefaultAccentColor;
        _preferences.Font = FontPreference.Default;
        String selectedFont = FontBox.SelectedItem as String ?? DefaultFontLabel;
        _preferences.FontFamily = selectedFont == DefaultFontLabel ? String.Empty : selectedFont;
        _preferences.ColorVision = EnumValue(ColorVisionBox.SelectedIndex, ColorVisionMode.Default);
        _preferences.BionicReading = BionicReadingToggle.IsChecked == true;
        _preferences.SimpleReadingView = SimpleReadingToggle.IsChecked == true;
        _preferences.HighContrast = HighContrastToggle.IsChecked == true;
        _preferences.ReducedMotion = ReducedMotionToggle.IsChecked == true;
        _preferences.SeniorMode = SeniorModeToggle.IsChecked == true;
        _preferences.Normalize();
        ResetButton.IsVisible = HasCustomReadingSettings();
        PreferencesChanged?.Invoke();
    }

    private Boolean HasCustomReadingSettings() =>
        _preferences.ColorScheme != BrowserColorScheme.Light
        || !_preferences.AccentColor.Equals(
            BrowserPreferences.DefaultAccentColor,
            StringComparison.OrdinalIgnoreCase)
        || _preferences.Font != FontPreference.Default
        || _preferences.FontFamily.Length > 0
        || _preferences.ColorVision != ColorVisionMode.Default
        || _preferences.TextScalePercent != 100
        || _preferences.HighContrast
        || _preferences.ReducedMotion
        || _preferences.BionicReading
        || _preferences.SeniorMode
        || _preferences.SimpleReadingView;

    private void ResetClicked(Object? sender, RoutedEventArgs eventArgs)
    {
        _preferences.Reset();
        Refresh();
        PreferencesChanged?.Invoke();
    }

    private static T EnumValue<T>(Int32 value, T fallback)
        where T : struct, Enum =>
        Enum.IsDefined(typeof(T), value)
            ? (T)Enum.ToObject(typeof(T), value)
            : fallback;

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
