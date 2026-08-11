namespace Lumui.Cli.Configuration;

public sealed class CliPreferencesStore
{
    private const Int32 CurrentPreferencesVersion = 3;

    private static readonly String[] PreviousDefaultAccentColors =
    {
        "#1BA1E2",
        "#008F80",
        "#007F72"
    };

    public CliPreferences Load()
    {
        CliPreferences preferences = new CliPreferences();
        if (!File.Exists(CliPaths.PreferencesFile))
        {
            return preferences;
        }

        try
        {
            Int32 preferencesVersion = 1;
            foreach (String line in File.ReadLines(CliPaths.PreferencesFile))
            {
                Int32 separator = line.IndexOf('=');
                if (separator > 0)
                {
                    String key = line[..separator].Trim();
                    String value = line[(separator + 1)..].Trim();
                    if (key == "preferencesVersion"
                        && Int32.TryParse(value, out Int32 parsedVersion))
                    {
                        preferencesVersion = parsedVersion;
                    }
                    else
                    {
                        Apply(preferences, key, value);
                    }
                }
            }
            if (preferencesVersion < CurrentPreferencesVersion
                && PreviousDefaultAccentColors.Contains(
                    preferences.AccentColor,
                    StringComparer.OrdinalIgnoreCase))
            {
                preferences.AccentColor = CliPreferences.DefaultAccentColor;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new CliPreferences();
        }

        preferences.Normalize();
        return preferences;
    }

    public void Save(CliPreferences preferences)
    {
        preferences.Normalize();
        Directory.CreateDirectory(CliPaths.DataFolder);
        String temporary = CliPaths.PreferencesFile + ".tmp";
        File.WriteAllLines(temporary, new String[]
        {
            "preferencesVersion=" + CurrentPreferencesVersion,
            "homePage=" + preferences.HomePage,
            "startupMode=" + preferences.StartupMode,
            "newTabMode=" + preferences.NewTabMode,
            "newTabPage=" + preferences.NewTabPage,
            "downloadFolder=" + preferences.DownloadFolder,
            "askWhereToSaveDownloads=" + preferences.AskWhereToSaveDownloads,
            "offerToSavePasswords=" + preferences.OfferToSavePasswords,
            "autoFillPasswords=" + preferences.AutoFillPasswords,
            "rememberHistory=" + preferences.RememberHistory,
            "clearBrowsingDataOnExit=" + preferences.ClearBrowsingDataOnExit,
            "sendDoNotTrack=" + preferences.SendDoNotTrack,
            "askBeforeSensitivePermissions=" + preferences.AskBeforeSensitivePermissions,
            "confirmClosingMultipleTabs=" + preferences.ConfirmClosingMultipleTabs,
            "colorScheme=" + preferences.ColorScheme,
            "accentColor=" + preferences.AccentColor,
            "font=" + preferences.Font,
            "fontFamily=" + preferences.FontFamily,
            "colorVision=" + preferences.ColorVision,
            "textScalePercent=" + preferences.TextScalePercent,
            "pageZoomPercent=" + preferences.PageZoomPercent,
            "highContrast=" + preferences.HighContrast,
            "reducedMotion=" + preferences.ReducedMotion,
            "bionicReading=" + preferences.BionicReading,
            "seniorMode=" + preferences.SeniorMode,
            "simpleReadingView=" + preferences.SimpleReadingView,
            "terminalDensity=" + preferences.TerminalDensity,
            "terminalOutput=" + preferences.TerminalOutput,
            "showOutline=" + preferences.ShowOutline,
            "useUnicode=" + preferences.UseUnicode
        });
        File.Move(temporary, CliPaths.PreferencesFile, true);
    }

    private static void Apply(CliPreferences preferences, String key, String value)
    {
        switch (key)
        {
            case "homePage": preferences.HomePage = value; break;
            case "startupMode" when Enum.TryParse(value, true, out CliStartupMode startup): preferences.StartupMode = startup; break;
            case "newTabMode" when Enum.TryParse(value, true, out CliNewTabMode newTab): preferences.NewTabMode = newTab; break;
            case "newTabPage": preferences.NewTabPage = value; break;
            case "downloadFolder": preferences.DownloadFolder = value; break;
            case "askWhereToSaveDownloads" when Boolean.TryParse(value, out Boolean ask): preferences.AskWhereToSaveDownloads = ask; break;
            case "offerToSavePasswords" when Boolean.TryParse(value, out Boolean offer): preferences.OfferToSavePasswords = offer; break;
            case "autoFillPasswords" when Boolean.TryParse(value, out Boolean fill): preferences.AutoFillPasswords = fill; break;
            case "rememberHistory" when Boolean.TryParse(value, out Boolean history): preferences.RememberHistory = history; break;
            case "clearBrowsingDataOnExit" when Boolean.TryParse(value, out Boolean clear): preferences.ClearBrowsingDataOnExit = clear; break;
            case "sendDoNotTrack" when Boolean.TryParse(value, out Boolean dnt): preferences.SendDoNotTrack = dnt; break;
            case "askBeforeSensitivePermissions" when Boolean.TryParse(value, out Boolean permission): preferences.AskBeforeSensitivePermissions = permission; break;
            case "confirmClosingMultipleTabs" when Boolean.TryParse(value, out Boolean tabs): preferences.ConfirmClosingMultipleTabs = tabs; break;
            case "colorScheme" when Enum.TryParse(value, true, out CliColorScheme scheme): preferences.ColorScheme = scheme; break;
            case "accentColor": preferences.AccentColor = value; break;
            case "font": preferences.Font = value; break;
            case "fontFamily": preferences.FontFamily = value; break;
            case "colorVision": preferences.ColorVision = value; break;
            case "textScalePercent" when Int32.TryParse(value, out Int32 scale): preferences.TextScalePercent = scale; break;
            case "pageZoomPercent" when Int32.TryParse(value, out Int32 zoom): preferences.PageZoomPercent = zoom; break;
            case "highContrast" when Boolean.TryParse(value, out Boolean contrast): preferences.HighContrast = contrast; break;
            case "reducedMotion" when Boolean.TryParse(value, out Boolean motion): preferences.ReducedMotion = motion; break;
            case "bionicReading" when Boolean.TryParse(value, out Boolean bionic): preferences.BionicReading = bionic; break;
            case "seniorMode" when Boolean.TryParse(value, out Boolean senior): preferences.SeniorMode = senior; break;
            case "simpleReadingView" when Boolean.TryParse(value, out Boolean reading): preferences.SimpleReadingView = reading; break;
            case "terminalDensity" when Enum.TryParse(value, true, out CliTerminalDensity density): preferences.TerminalDensity = density; break;
            case "terminalOutput" when Enum.TryParse(value, true, out CliTerminalOutput output): preferences.TerminalOutput = output; break;
            case "showOutline" when Boolean.TryParse(value, out Boolean outline): preferences.ShowOutline = outline; break;
            case "useUnicode" when Boolean.TryParse(value, out Boolean unicode): preferences.UseUnicode = unicode; break;
        }
    }
}
