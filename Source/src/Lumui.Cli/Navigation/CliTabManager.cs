namespace Lumui.Cli.Navigation;

public sealed class CliTabManager : IDisposable
{
    private readonly List<CliTabSession> _tabs = new List<CliTabSession>();
    private readonly Stack<Uri> _closedAddresses = new Stack<Uri>();

    public event Action? Changed;

    public IReadOnlyList<CliTabSession> Tabs => _tabs;

    public CliTabSession? Active { get; private set; }

    public CliTabSession Create()
    {
        CliTabSession tab = new CliTabSession();
        _tabs.Add(tab);
        Active = tab;
        Changed?.Invoke();
        return tab;
    }

    public Boolean Activate(CliTabSession tab)
    {
        if (!_tabs.Contains(tab))
        {
            return false;
        }
        Active = tab;
        Changed?.Invoke();
        return true;
    }

    public CliTabSession Close(CliTabSession tab)
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
        tab.Dispose();
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

    public Uri? TakeLastClosedAddress() => _closedAddresses.Count > 0 ? _closedAddresses.Pop() : null;

    public CliTabSession? Move(Int32 offset)
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
        Active = _tabs[target];
        Changed?.Invoke();
        return Active;
    }

    public CliTabSession? ActivateAt(Int32 index)
    {
        if (_tabs.Count == 0)
        {
            return null;
        }
        Int32 target = index == -1 ? _tabs.Count - 1 : Math.Clamp(index, 0, _tabs.Count - 1);
        Active = _tabs[target];
        Changed?.Invoke();
        return Active;
    }

    public void Dispose()
    {
        foreach (CliTabSession tab in _tabs)
        {
            tab.Dispose();
        }
        _tabs.Clear();
    }
}
