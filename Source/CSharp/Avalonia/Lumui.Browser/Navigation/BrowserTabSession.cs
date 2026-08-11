using Avalonia;
using Avalonia.Controls;
using Lumui.Browser.Configuration;
using Lumui.Browser.Rendering;
using Lumui.Client;

namespace Lumui.Browser.Navigation;

public sealed class BrowserTabSession : IDisposable
{
    private CancellationTokenSource? _navigationCancellation;
    private CancellationTokenSource? _actionCancellation;

    public BrowserTabSession()
    {
        Id = Guid.NewGuid();
        History = new BrowserHistory();
    }

    public Guid Id { get; }

    public BrowserHistory History { get; }

    public Uri? Address { get; set; }

    public String Title { get; set; } = "New tab";

    public String Status { get; set; } = BrowserText.Ready;

    public String DocumentInfo { get; set; } = String.Empty;

    public TimeSpan LoadDuration { get; set; }

    public Boolean IsBusy { get; set; }

    public LoadedSurface? Loaded { get; private set; }

    public LumuiRenderer? Renderer { get; private set; }

    public Control? Content { get; private set; }

    public Vector ViewportOffset { get; set; }

    public CancellationToken BeginNavigation()
    {
        Renderer?.PauseDeferredWork();
        _actionCancellation?.Cancel();
        _navigationCancellation?.Cancel();
        _navigationCancellation?.Dispose();
        _navigationCancellation = new CancellationTokenSource();
        return _navigationCancellation.Token;
    }

    public Boolean IsCurrentNavigation(CancellationToken cancellationToken) =>
        _navigationCancellation?.Token == cancellationToken;

    public CancellationToken BeginAction()
    {
        _actionCancellation?.Cancel();
        _actionCancellation?.Dispose();
        _actionCancellation = new CancellationTokenSource();
        return _actionCancellation.Token;
    }

    public void SetDocument(
        LoadedSurface loaded,
        LumuiRenderer renderer,
        Control content)
    {
        LoadedSurface? previousLoaded = Loaded;
        LumuiRenderer? previousRenderer = Renderer;
        Loaded = loaded;
        Renderer = renderer;
        Content = content;
        DeferredDisposalQueue.Shared.Enqueue(previousRenderer);
        DeferredDisposalQueue.Shared.Enqueue(previousLoaded);
    }

    public void ReplaceRendering(LumuiRenderer renderer, Control content)
    {
        LumuiRenderer? previousRenderer = Renderer;
        Renderer = renderer;
        Content = content;
        DeferredDisposalQueue.Shared.Enqueue(previousRenderer);
    }

    public void ReleaseRendering()
    {
        DeferredDisposalQueue.Shared.Enqueue(Renderer);
        Renderer = null;
        Content = null;
    }

    public void SetError(Control content)
    {
        Content = content;
        DeferredDisposalQueue.Shared.Enqueue(Renderer);
        Renderer = null;
    }

    public void SetBlank()
    {
        _navigationCancellation?.Cancel();
        _actionCancellation?.Cancel();
        DeferredDisposalQueue.Shared.Enqueue(Renderer);
        DeferredDisposalQueue.Shared.Enqueue(Loaded);
        Renderer = null;
        Loaded = null;
        Content = null;
        Address = null;
        Title = "New tab";
        Status = BrowserText.Ready;
        DocumentInfo = String.Empty;
        LoadDuration = TimeSpan.Zero;
        IsBusy = false;
    }

    public void RestoreAddress(Uri address)
    {
        SetBlank();
        Address = address;
        Title = address.Host;
        DocumentInfo = address.Host;
        History.Push(address);
    }

    public void CancelNavigation() => _navigationCancellation?.Cancel();

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
        DeferredDisposalQueue.Shared.Enqueue(Renderer);
        DeferredDisposalQueue.Shared.Enqueue(Loaded);
    }
}
