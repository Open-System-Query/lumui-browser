namespace Lumui.Browser.Navigation;

public sealed class BrowserTabManager : IDisposable
{
    private readonly List<BrowserTabSession> _tabs = new List<BrowserTabSession>();
    private readonly Stack<Uri> _closedAddresses = new Stack<Uri>();

    public event Action? Changed;

    public IReadOnlyList<BrowserTabSession> Tabs => _tabs;

    public BrowserTabSession? Active { get; private set; }

    public BrowserTabSession Create()
    {
        BrowserTabSession tab = new BrowserTabSession();
        _tabs.Add(tab);
        Active = tab;
        Changed?.Invoke();
        return tab;
    }

    public Boolean Activate(BrowserTabSession tab)
    {
        if (!_tabs.Contains(tab))
        {
            return false;
        }
        if (ReferenceEquals(Active, tab))
        {
            return true;
        }
        Active = tab;
        Changed?.Invoke();
        return true;
    }

    public BrowserTabSession Close(BrowserTabSession tab)
    {
        Int32 index = _tabs.IndexOf(tab);
        if (index < 0)
        {
            return Active ?? Create();
        }
        if (tab.Address is not null)
        {
            _closedAddresses.Push(tab.Address);
        }
        _tabs.RemoveAt(index);
        if (_tabs.Count == 0)
        {
            return Create();
        }
        if (ReferenceEquals(Active, tab))
        {
            Active = _tabs[Math.Min(index, _tabs.Count - 1)];
        }
        Changed?.Invoke();
        return Active!;
    }

    public Uri? TakeLastClosedAddress() =>
        _closedAddresses.Count > 0 ? _closedAddresses.Pop() : null;

    public BrowserTabSession? Move(Int32 offset)
    {
        if (Active is null || _tabs.Count == 0)
        {
            return null;
        }
        Int32 current = _tabs.IndexOf(Active);
        Int32 target = (current + offset) % _tabs.Count;
        if (target < 0)
        {
            target += _tabs.Count;
        }
        if (target == current)
        {
            return Active;
        }
        Active = _tabs[target];
        Changed?.Invoke();
        return Active;
    }

    public BrowserTabSession? ActivateAt(Int32 index)
    {
        if (_tabs.Count == 0)
        {
            return null;
        }
        Int32 target = index == -1
            ? _tabs.Count - 1
            : Math.Clamp(index, 0, _tabs.Count - 1);
        if (ReferenceEquals(Active, _tabs[target]))
        {
            return Active;
        }
        Active = _tabs[target];
        Changed?.Invoke();
        return Active;
    }

    public void Dispose()
    {
        foreach (BrowserTabSession tab in _tabs)
        {
            tab.Dispose();
        }
        _tabs.Clear();
    }
}
