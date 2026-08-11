using System.Net;
using Lumui.Browser.Data;
using Lumui.Browser.Downloads;
using Lumui.Browser.Security;
using Lumui.Client;

namespace Lumui.Browser.Configuration;

public sealed class BrowserApplicationServices : IDisposable
{
    private static readonly Lazy<BrowserApplicationServices> Shared =
        new Lazy<BrowserApplicationServices>(
            () => new BrowserApplicationServices(),
            LazyThreadSafetyMode.ExecutionAndPublication);
    private Boolean _disposed;
    private readonly HashSet<LumuiClient> _clients = new HashSet<LumuiClient>();

    private BrowserApplicationServices()
    {
        PreferencesStore = new BrowserPreferencesStore();
        Preferences = PreferencesStore.Load();
        Bookmarks = new BookmarkStore();
        History = new HistoryStore();
        Downloads = new DownloadManager();
        Credentials = new ProtectedCredentialVault();
    }

    public static BrowserApplicationServices Current => Shared.Value;

    public BrowserPreferencesStore PreferencesStore { get; }

    public BrowserPreferences Preferences { get; }

    public BookmarkStore Bookmarks { get; }

    public HistoryStore History { get; }

    public DownloadManager Downloads { get; }

    public ICredentialVault Credentials { get; }

    public CookieContainer Cookies { get; } = new CookieContainer();

    public event Action<Object>? PreferencesChanged;

    public void NotifyPreferencesChanged(Object source) =>
        PreferencesChanged?.Invoke(source);

    public void RegisterClient(LumuiClient client) => _clients.Add(client);

    public void UnregisterClient(LumuiClient client) => _clients.Remove(client);

    public void ClearBrowsingData()
    {
        foreach (LumuiClient client in _clients.ToArray())
        {
            client.ClearBrowsingData();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _clients.Clear();
        Downloads.Dispose();
    }
}
