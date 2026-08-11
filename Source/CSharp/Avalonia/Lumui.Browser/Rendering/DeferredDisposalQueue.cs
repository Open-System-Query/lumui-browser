using System.Collections.Concurrent;
using Avalonia.Threading;

namespace Lumui.Browser.Rendering;

public sealed class DeferredDisposalQueue
{
    private readonly ConcurrentQueue<IDisposable> _pending =
        new ConcurrentQueue<IDisposable>();
    private Int32 _scheduled;

    public static DeferredDisposalQueue Shared { get; } =
        new DeferredDisposalQueue();

    public void Enqueue(IDisposable? disposable)
    {
        if (disposable is null)
        {
            return;
        }
        _pending.Enqueue(disposable);
        Schedule();
    }

    private void Schedule()
    {
        if (Interlocked.Exchange(ref _scheduled, 1) != 0)
        {
            return;
        }
        Dispatcher.UIThread.Post(DrainOne, DispatcherPriority.ApplicationIdle);
    }

    private void DrainOne()
    {
        if (_pending.TryDequeue(out IDisposable? disposable))
        {
            try
            {
                disposable.Dispose();
            }
            catch
            {
            }
        }
        Interlocked.Exchange(ref _scheduled, 0);
        if (!_pending.IsEmpty)
        {
            Schedule();
        }
    }
}
