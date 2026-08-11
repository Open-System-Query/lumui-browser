namespace Lumui.Browser.Configuration;

public static class BrowserPaths
{
    private const String ApplicationFolder = "LUMUI Browser";

    public static String DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ApplicationFolder);

    public static String PreferencesFile { get; } =
        Path.Combine(DataFolder, "preferences.conf");

    public static String BookmarksFile { get; } =
        Path.Combine(DataFolder, "bookmarks.data");

    public static String HistoryFile { get; } =
        Path.Combine(DataFolder, "history.data");

    public static String CredentialsFile { get; } =
        Path.Combine(DataFolder, "credentials.data");

    public static String SessionFile { get; } =
        Path.Combine(DataFolder, "session.data");

    public static String DownloadsFile { get; } =
        Path.Combine(DataFolder, "downloads.data");

    public static String MediaCacheFolder { get; } =
        Path.Combine(DataFolder, "media");

    public static String PreparedMediaCacheFolder { get; } =
        Path.Combine(MediaCacheFolder, "prepared");

    public static String WindowPlacementFile { get; } =
        Path.Combine(DataFolder, "windows.conf");

    public static String DefaultDownloadFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads");
}
