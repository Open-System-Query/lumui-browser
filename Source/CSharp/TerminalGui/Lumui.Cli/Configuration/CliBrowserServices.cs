using Lumui.Cli.Data;
using Lumui.Cli.Diagnostics;
using Lumui.Cli.Downloads;
using Lumui.Cli.Security;

namespace Lumui.Cli.Configuration;

public sealed class CliBrowserServices : IDisposable
{
    public CliBrowserServices(Boolean privateMode)
    {
        IsPrivate = privateMode;
        PreferencesStore = new CliPreferencesStore();
        Preferences = PreferencesStore.Load();
        RequestMonitor = new CliRequestMonitor();
        Client = new LumuiClient(observer: RequestMonitor);
        Client.SetDoNotTrack(Preferences.SendDoNotTrack);
        Bookmarks = new BookmarkStore(privateMode);
        History = new HistoryStore(privateMode);
        Downloads = new DownloadManager(privateMode);
        Credentials = new ProtectedCredentialVault(privateMode);
        Session = new SessionStore();
    }

    public Boolean IsPrivate { get; }

    public CliPreferences Preferences { get; }

    public CliPreferencesStore PreferencesStore { get; }

    public LumuiClient Client { get; }

    public CliRequestMonitor RequestMonitor { get; }

    public BookmarkStore Bookmarks { get; }

    public HistoryStore History { get; }

    public DownloadManager Downloads { get; }

    public ProtectedCredentialVault Credentials { get; }

    public SessionStore Session { get; }

    public void SavePreferences()
    {
        Preferences.Normalize();
        if (!IsPrivate)
        {
            PreferencesStore.Save(Preferences);
        }
        Client.SetDoNotTrack(Preferences.SendDoNotTrack);
    }

    public void ClearBrowsingData()
    {
        Client.ClearBrowsingData();
        History.Clear();
        RequestMonitor.Clear();
    }

    public void Dispose()
    {
        if (Preferences.ClearBrowsingDataOnExit)
        {
            ClearBrowsingData();
        }
        Downloads.Dispose();
        Client.Dispose();
    }
}
