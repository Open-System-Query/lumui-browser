using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Lumui.Browser.Configuration;
using Lumui.Browser.Shell;

namespace Lumui.Browser.Views;

public sealed partial class BrowserSettingsPanel : UserControl
{
    private readonly BrowserPreferences _preferences;
    private readonly PersonalizationPanel _readingSettings;
    private Boolean _updating;

    public BrowserSettingsPanel()
        : this(new BrowserPreferences(), null)
    {
    }

    public BrowserSettingsPanel(BrowserPreferences preferences)
        : this(preferences, null)
    {
    }

    public BrowserSettingsPanel(
        BrowserPreferences preferences,
        BrowserShellRenderer? renderer)
        : this(
            preferences,
            renderer,
            new PersonalizationPanel(preferences, renderer))
    {
    }

    public BrowserSettingsPanel(
        BrowserPreferences preferences,
        BrowserShellRenderer? renderer,
        PersonalizationPanel readingSettings)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _readingSettings = readingSettings
            ?? throw new ArgumentNullException(nameof(readingSettings));
        InitializeComponent();
        ReadingSettingsHost.Content = _readingSettings;
        StartupBox.ItemsSource = new String[] { "Home page", "Previous tabs" };
        NewTabModeBox.ItemsSource = new String[] { "Home page", "Blank page", "Custom page" };
        StartupBox.SelectionChanged += SettingChanged;
        NewTabModeBox.SelectionChanged += NewTabSettingChanged;
        HomePageBox.LostFocus += TextSettingChanged;
        NewTabPageBox.LostFocus += TextSettingChanged;
        DownloadFolderBox.LostFocus += TextSettingChanged;
        Bind(ConfirmTabsToggle);
        Bind(HistoryToggle);
        Bind(DoNotTrackToggle);
        Bind(ClearOnExitToggle);
        Bind(AskDownloadToggle);
        Bind(SavePasswordsToggle);
        Bind(AutoFillToggle);
        Bind(PermissionsToggle);
        GeneralNavButton.Click += (_, _) => SelectPage("General");
        TextNavButton.Click += (_, _) => SelectPage("Text");
        ColorsNavButton.Click += (_, _) => SelectPage("Colors");
        ComfortNavButton.Click += (_, _) => SelectPage("Comfort");
        PrivacyNavButton.Click += (_, _) => SelectPage("Privacy");
        DownloadsNavButton.Click += (_, _) => SelectPage("Downloads");
        PermissionsNavButton.Click += (_, _) => SelectPage("Permissions");
        SettingsSearchBox.TextChanged += SearchChanged;
        OpenPasswordsButton.Click += (_, _) => OpenPasswordsRequested?.Invoke();
        ClearBrowsingDataButton.Click += (_, _) =>
            ClearBrowsingDataRequested?.Invoke();
        _readingSettings.PreferencesChanged += ReadingPreferencesChanged;
        renderer?.ApplyControl(StartupBox, "settings.startup");
        renderer?.ApplyControl(HomePageBox, "settings.homePage");
        renderer?.ApplyControl(NewTabModeBox, "settings.newTabs");
        renderer?.ApplyControl(NewTabPageBox, "settings.newTabPage");
        renderer?.ApplyControl(ConfirmTabsToggle, "settings.confirmTabs");
        renderer?.ApplyControl(HistoryToggle, "settings.history");
        renderer?.ApplyControl(DoNotTrackToggle, "settings.doNotTrack");
        renderer?.ApplyControl(ClearOnExitToggle, "settings.clearOnExit");
        renderer?.ApplyControl(SavePasswordsToggle, "settings.savePasswords");
        renderer?.ApplyControl(AutoFillToggle, "settings.fillPasswords");
        renderer?.ApplyControl(DownloadFolderBox, "settings.downloadFolder");
        renderer?.ApplyControl(AskDownloadToggle, "settings.askDownload");
        renderer?.ApplyControl(PermissionsToggle, "settings.sensitiveAccess");
        renderer?.ApplyButton(OpenPasswordsButton, "settings.passwords", false);
        renderer?.ApplyButton(
            ClearBrowsingDataButton,
            "settings.clearData",
            false);
        Refresh();
    }

    public event Action? PreferencesChanged;

    public event Action? OpenPasswordsRequested;

    public event Action? ClearBrowsingDataRequested;

    public void ShowTextSettings() => SelectPage("Text");

    public void Refresh()
    {
        _updating = true;
        StartupBox.SelectedIndex = (Int32)_preferences.StartupMode;
        HomePageBox.Text = _preferences.HomePage;
        NewTabModeBox.SelectedIndex = (Int32)_preferences.NewTabMode;
        NewTabPageBox.Text = _preferences.NewTabPage;
        NewTabPageRow.IsVisible = _preferences.NewTabMode == BrowserNewTabMode.Custom;
        DownloadFolderBox.Text = _preferences.DownloadFolder;
        ConfirmTabsToggle.IsChecked = _preferences.ConfirmClosingMultipleTabs;
        HistoryToggle.IsChecked = _preferences.RememberHistory;
        DoNotTrackToggle.IsChecked = _preferences.SendDoNotTrack;
        ClearOnExitToggle.IsChecked = _preferences.ClearBrowsingDataOnExit;
        AskDownloadToggle.IsChecked = _preferences.AskWhereToSaveDownloads;
        SavePasswordsToggle.IsChecked = _preferences.OfferToSavePasswords;
        AutoFillToggle.IsChecked = _preferences.AutoFillPasswords;
        PermissionsToggle.IsChecked = _preferences.AskBeforeSensitivePermissions;
        _readingSettings.Refresh();
        _updating = false;
    }

    public void SetDarkMode(Boolean dark, Boolean highContrast)
    {
        SetClass(SettingsRoot, "dark", dark && !highContrast);
        SetClass(SettingsRoot, "high-contrast", highContrast);
        _readingSettings.SetDarkMode(dark, highContrast);
    }

    private void Bind(ToggleSwitch toggle) => toggle.IsCheckedChanged += ToggleSettingChanged;

    private void SelectPage(String page)
    {
        GeneralPage.IsVisible = page == "General";
        Boolean reading = page is "Text" or "Colors" or "Comfort";
        ReadingSettingsHost.IsVisible = reading;
        PrivacyPage.IsVisible = page == "Privacy";
        DownloadsPage.IsVisible = page == "Downloads";
        PermissionsPage.IsVisible = page == "Permissions";
        SetSelected(GeneralNavButton, GeneralPage.IsVisible);
        SetSelected(TextNavButton, page == "Text");
        SetSelected(ColorsNavButton, page == "Colors");
        SetSelected(ComfortNavButton, page == "Comfort");
        SetSelected(PrivacyNavButton, PrivacyPage.IsVisible);
        SetSelected(DownloadsNavButton, DownloadsPage.IsVisible);
        SetSelected(PermissionsNavButton, PermissionsPage.IsVisible);
        if (page == "Text")
        {
            _readingSettings.ShowTextSettings();
            _ = _readingSettings.PrepareAsync();
        }
        else if (page == "Colors")
        {
            _readingSettings.ShowColorSettings();
        }
        else if (page == "Comfort")
        {
            _readingSettings.ShowComfortSettings();
        }
    }

    private void SearchChanged(Object? sender, TextChangedEventArgs eventArgs)
    {
        String query = (SettingsSearchBox.Text ?? String.Empty).Trim();
        GeneralNavButton.IsVisible = Matches("general startup home new tabs passwords", query);
        TextNavButton.IsVisible = Matches("text reading font size bionic", query);
        ColorsNavButton.IsVisible = Matches("colors theme contrast vision", query);
        ComfortNavButton.IsVisible = Matches("comfort motion guided reading", query);
        PrivacyNavButton.IsVisible = Matches(
            "privacy history browsing data cookies tracking passwords clear",
            query);
        DownloadsNavButton.IsVisible = Matches("downloads files folder save", query);
        PermissionsNavButton.IsVisible = Matches("permissions access capabilities data", query);
    }

    private static Boolean Matches(String terms, String query) =>
        query.Length == 0 || terms.Contains(query, StringComparison.CurrentCultureIgnoreCase);

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

    private void SettingChanged(Object? sender, SelectionChangedEventArgs eventArgs) => ReadPreferences();

    private void NewTabSettingChanged(Object? sender, SelectionChangedEventArgs eventArgs)
    {
        NewTabPageRow.IsVisible = NewTabModeBox.SelectedIndex == (Int32)BrowserNewTabMode.Custom;
        ReadPreferences();
    }

    private void ToggleSettingChanged(Object? sender, RoutedEventArgs eventArgs) => ReadPreferences();

    private void TextSettingChanged(Object? sender, RoutedEventArgs eventArgs) => ReadPreferences();

    private void ReadingPreferencesChanged() => PreferencesChanged?.Invoke();

    private void ReadPreferences()
    {
        if (_updating)
        {
            return;
        }
        _preferences.StartupMode = StartupBox.SelectedIndex == 1
            ? BrowserStartupMode.RestorePreviousSession
            : BrowserStartupMode.Home;
        _preferences.HomePage = HomePageBox.Text ?? String.Empty;
        _preferences.NewTabMode = NewTabModeBox.SelectedIndex switch
        {
            1 => BrowserNewTabMode.Blank,
            2 => BrowserNewTabMode.Custom,
            _ => BrowserNewTabMode.Home
        };
        _preferences.NewTabPage = NewTabPageBox.Text ?? String.Empty;
        _preferences.DownloadFolder = DownloadFolderBox.Text ?? String.Empty;
        _preferences.ConfirmClosingMultipleTabs = ConfirmTabsToggle.IsChecked == true;
        _preferences.RememberHistory = HistoryToggle.IsChecked == true;
        _preferences.SendDoNotTrack = DoNotTrackToggle.IsChecked == true;
        _preferences.ClearBrowsingDataOnExit = ClearOnExitToggle.IsChecked == true;
        _preferences.AskWhereToSaveDownloads = AskDownloadToggle.IsChecked == true;
        _preferences.OfferToSavePasswords = SavePasswordsToggle.IsChecked == true;
        _preferences.AutoFillPasswords = AutoFillToggle.IsChecked == true;
        _preferences.AskBeforeSensitivePermissions = PermissionsToggle.IsChecked == true;
        _preferences.Normalize();
        PreferencesChanged?.Invoke();
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
