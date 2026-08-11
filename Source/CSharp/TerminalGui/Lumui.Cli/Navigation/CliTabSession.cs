using Lumui.Cli.Rendering;

namespace Lumui.Cli.Navigation;

public sealed class CliTabSession : IDisposable
{
    private CancellationTokenSource? _navigationCancellation;
    private CancellationTokenSource? _actionCancellation;

    public CliTabSession()
    {
        Id = Guid.NewGuid();
        History = new CliBrowserHistory();
    }

    public Guid Id { get; }

    public CliBrowserHistory History { get; }

    public Uri? Address { get; set; }

    public String Title { get; set; } = "New tab";

    public String Status { get; set; } = "Ready";

    public String DocumentInfo { get; set; } = String.Empty;

    public TimeSpan LoadDuration { get; set; }

    public Boolean IsBusy { get; set; }

    public Int32 PageIndex { get; set; }

    public Int32 GuidedStep { get; set; }

    public LoadedSurface? Loaded { get; private set; }

    public TerminalSurfaceDocument? Document { get; private set; }

    public Dictionary<String, Object?> Input { get; } = new Dictionary<String, Object?>(StringComparer.Ordinal);

    public CancellationToken BeginNavigation()
    {
        _actionCancellation?.Cancel();
        _navigationCancellation?.Cancel();
        _navigationCancellation?.Dispose();
        _navigationCancellation = new CancellationTokenSource();
        return _navigationCancellation.Token;
    }

    public Boolean IsCurrentNavigation(CancellationToken cancellationToken) =>
        _navigationCancellation?.Token == cancellationToken
        && !cancellationToken.IsCancellationRequested;

    public CancellationToken BeginAction()
    {
        _actionCancellation?.Cancel();
        _actionCancellation?.Dispose();
        _actionCancellation = new CancellationTokenSource();
        return _actionCancellation.Token;
    }

    public Boolean IsCurrentAction(CancellationToken cancellationToken) =>
        _actionCancellation?.Token == cancellationToken
        && !cancellationToken.IsCancellationRequested;

    public void SetDocument(LoadedSurface loaded, TerminalSurfaceDocument document)
    {
        LoadedSurface? previous = Loaded;
        Loaded = loaded;
        Document = document;
        Input.Clear();
        foreach (KeyValuePair<String, Object?> value in document.InitialInput)
        {
            Input[value.Key] = value.Value;
        }
        PageIndex = document.RequestedPageIndex;
        GuidedStep = 0;
        previous?.Dispose();
    }

    public void SetBlank()
    {
        _navigationCancellation?.Cancel();
        _actionCancellation?.Cancel();
        Loaded?.Dispose();
        Loaded = null;
        Document = null;
        Input.Clear();
        Address = null;
        Title = "New tab";
        Status = "Ready";
        DocumentInfo = String.Empty;
        LoadDuration = TimeSpan.Zero;
        IsBusy = false;
        PageIndex = 0;
        GuidedStep = 0;
    }

    public void RestoreAddress(Uri address)
    {
        SetBlank();
        Address = address;
        Title = address.Host;
        DocumentInfo = address.Host;
        History.Push(address);
    }

    public void CancelPendingWork()
    {
        _navigationCancellation?.Cancel();
        _actionCancellation?.Cancel();
    }

    public void Dispose()
    {
        _navigationCancellation?.Cancel();
        _navigationCancellation?.Dispose();
        _actionCancellation?.Cancel();
        _actionCancellation?.Dispose();
        Loaded?.Dispose();
    }
}

