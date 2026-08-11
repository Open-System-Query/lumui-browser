namespace Lumui.Cli.Navigation;

public sealed class CliBrowserHistory
{
    private readonly List<Uri> _entries = new List<Uri>();
    private Int32 _index = -1;

    public Boolean CanMoveBack => _index > 0;

    public Boolean CanMoveForward => _index >= 0 && _index < _entries.Count - 1;

    public Uri? Current => _index >= 0 && _index < _entries.Count ? _entries[_index] : null;

    public void Push(Uri address)
    {
        if (_index + 1 < _entries.Count)
        {
            _entries.RemoveRange(_index + 1, _entries.Count - _index - 1);
        }
        if (_entries.Count == 0 || _entries[^1] != address)
        {
            _entries.Add(address);
        }
        _index = _entries.Count - 1;
    }

    public Boolean TryPeek(Int32 offset, out Uri? address)
    {
        Int32 target = _index + offset;
        if (target < 0 || target >= _entries.Count)
        {
            address = null;
            return false;
        }
        address = _entries[target];
        return true;
    }

    public Boolean TryMove(Int32 offset)
    {
        if (!TryPeek(offset, out Uri? address) || address is null)
        {
            return false;
        }
        _index += offset;
        return true;
    }
}
