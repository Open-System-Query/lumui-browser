using Lumui.Browser.Presentation;

namespace Lumui.Browser.Configuration;

public sealed class BrowserPreferencesStore
{
    private readonly String _path;
    private readonly SemaphoreSlim _saveGate = new SemaphoreSlim(1, 1);
    private Int64 _saveVersion;

    public BrowserPreferencesStore()
    {
        _path = BrowserPaths.PreferencesFile;
    }

    public BrowserPreferences Load()
    {
        BrowserPreferences preferences = new BrowserPreferences();
        if (!File.Exists(_path))
        {
            return preferences;
        }

        try
        {
            foreach (String line in File.ReadLines(_path))
            {
                Int32 separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }
                Apply(
                    preferences,
                    line[..separator].Trim(),
                    line[(separator + 1)..].Trim());
            }
        }
        catch (IOException)
        {
            return new BrowserPreferences();
        }
        catch (UnauthorizedAccessException)
        {
            return new BrowserPreferences();
        }

        preferences.Normalize();
        return preferences;
    }

    public Task SaveAsync(BrowserPreferences preferences)
    {
        preferences.Normalize();
        String? directory = Path.GetDirectoryName(_path);
        if (String.IsNullOrWhiteSpace(directory))
        {
            return Task.CompletedTask;
        }
        String[] snapshot = new String[]
        {
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
            "simpleReadingView=" + preferences.SimpleReadingView
        };
        Int64 version = Interlocked.Increment(ref _saveVersion);
        return Task.Run(() => SaveSnapshot(directory, snapshot, version));
    }

    private void SaveSnapshot(
        String directory,
        IReadOnlyList<String> snapshot,
        Int64 version)
    {
        _saveGate.Wait();
        try
        {
            if (version != Volatile.Read(ref _saveVersion))
            {
                return;
            }
            Directory.CreateDirectory(directory);
            String temporaryPath = _path + ".tmp";
            File.WriteAllLines(temporaryPath, snapshot);
            if (version == Volatile.Read(ref _saveVersion))
            {
                File.Move(temporaryPath, _path, true);
            }
            else if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private static void Apply(
        BrowserPreferences preferences,
        String key,
        String value)
    {
        switch (key)
        {
            case "homePage":
                preferences.HomePage = value;
                break;
            case "startupMode" when Enum.TryParse(
                value,
                true,
                out BrowserStartupMode startupMode):
                preferences.StartupMode = startupMode;
                break;
            case "newTabMode" when Enum.TryParse(
                value,
                true,
                out BrowserNewTabMode newTabMode):
                preferences.NewTabMode = newTabMode;
                break;
            case "newTabPage":
                preferences.NewTabPage = value;
                break;
            case "downloadFolder":
                preferences.DownloadFolder = value;
                break;
            case "askWhereToSaveDownloads" when Boolean.TryParse(value, out Boolean askDownloads):
                preferences.AskWhereToSaveDownloads = askDownloads;
                break;
            case "offerToSavePasswords" when Boolean.TryParse(value, out Boolean savePasswords):
                preferences.OfferToSavePasswords = savePasswords;
                break;
            case "autoFillPasswords" when Boolean.TryParse(value, out Boolean autoFill):
                preferences.AutoFillPasswords = autoFill;
                break;
            case "rememberHistory" when Boolean.TryParse(value, out Boolean rememberHistory):
                preferences.RememberHistory = rememberHistory;
                break;
            case "clearBrowsingDataOnExit" when Boolean.TryParse(value, out Boolean clearOnExit):
                preferences.ClearBrowsingDataOnExit = clearOnExit;
                break;
            case "sendDoNotTrack" when Boolean.TryParse(value, out Boolean doNotTrack):
                preferences.SendDoNotTrack = doNotTrack;
                break;
            case "askBeforeSensitivePermissions" when Boolean.TryParse(value, out Boolean askPermissions):
                preferences.AskBeforeSensitivePermissions = askPermissions;
                break;
            case "confirmClosingMultipleTabs" when Boolean.TryParse(value, out Boolean confirmTabs):
                preferences.ConfirmClosingMultipleTabs = confirmTabs;
                break;
            case "colorScheme" when Enum.TryParse(
                value,
                true,
                out BrowserColorScheme colorScheme):
                preferences.ColorScheme = colorScheme;
                break;
            case "accentColor":
                preferences.AccentColor = value;
                break;
            case "font" when Enum.TryParse(
                value,
                true,
                out FontPreference font):
                preferences.Font = font;
                break;
            case "fontFamily":
                preferences.FontFamily = value;
                break;
            case "colorVision" when Enum.TryParse(
                value,
                true,
                out ColorVisionMode colorVision):
                preferences.ColorVision = colorVision;
                break;
            case "textScalePercent" when Int32.TryParse(
                value,
                out Int32 textScalePercent):
                preferences.TextScalePercent = textScalePercent;
                break;
            case "pageZoomPercent" when Int32.TryParse(
                value,
                out Int32 pageZoomPercent):
                preferences.PageZoomPercent = pageZoomPercent;
                break;
            case "highContrast" when Boolean.TryParse(value, out Boolean highContrast):
                preferences.HighContrast = highContrast;
                break;
            case "reducedMotion" when Boolean.TryParse(value, out Boolean reducedMotion):
                preferences.ReducedMotion = reducedMotion;
                break;
            case "bionicReading" when Boolean.TryParse(value, out Boolean bionicReading):
                preferences.BionicReading = bionicReading;
                break;
            case "seniorMode" when Boolean.TryParse(value, out Boolean seniorMode):
                preferences.SeniorMode = seniorMode;
                break;
            case "simpleReadingView" when Boolean.TryParse(value, out Boolean simpleReadingView):
                preferences.SimpleReadingView = simpleReadingView;
                break;
        }
    }
}
