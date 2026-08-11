using Lumui.Client;

namespace Lumui.Browser.DeveloperTools;

public sealed class BrowserRequestMonitor : ILumuiClientObserver
{
    private const Int32 MaximumTraces = 2000;
    private readonly Lock _lock = new Lock();
    private readonly List<LumuiRequestTrace> _traces = new List<LumuiRequestTrace>();

    public event Action<LumuiRequestTrace>? Recorded;

    public void Record(LumuiRequestTrace trace)
    {
        lock (_lock)
        {
            _traces.Add(trace);
            if (_traces.Count > MaximumTraces)
            {
                _traces.RemoveRange(0, _traces.Count - MaximumTraces);
            }
        }
        Recorded?.Invoke(trace);
    }

    public IReadOnlyList<LumuiRequestTrace> Snapshot()
    {
        lock (_lock)
        {
            return _traces.ToArray();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _traces.Clear();
        }
    }
}
