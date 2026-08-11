namespace Lumui.Cli.Diagnostics;

public sealed class CliRequestMonitor : ILumuiClientObserver
{
    private const Int32 MaximumTraces = 2000;
    private readonly Object _sync = new Object();
    private readonly List<LumuiRequestTrace> _traces = new List<LumuiRequestTrace>();

    public event Action<LumuiRequestTrace>? Recorded;

    public void Record(LumuiRequestTrace trace)
    {
        lock (_sync)
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
        lock (_sync)
        {
            return _traces.ToArray();
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _traces.Clear();
        }
    }
}

