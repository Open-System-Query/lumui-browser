using Avalonia.Threading;

namespace Lumui.Browser.Rendering;

public sealed class DeferredRenderScheduler
{
    private readonly PriorityQueue<
        (Func<CancellationToken, Task> Work, CancellationToken Cancellation),
        (Int32 Priority, Int64 Sequence)> _pending =
            new PriorityQueue<
                (Func<CancellationToken, Task> Work, CancellationToken Cancellation),
                (Int32 Priority, Int64 Sequence)>();
    private readonly CancellationToken _cancellationToken;
    private readonly Action<Exception> _failed;
    private Boolean _paused;
    private Boolean _running;
    private Int64 _sequence;
    private TaskCompletionSource<Boolean>? _resumeSignal;

    public DeferredRenderScheduler(
        CancellationToken cancellationToken,
        Action<Exception> failed)
    {
        _cancellationToken = cancellationToken;
        _failed = failed;
    }

    public void Enqueue(
        Func<CancellationToken, Task> work,
        Int32 priority = 1,
        CancellationToken cancellationToken = default)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(
                () => Enqueue(work, priority, cancellationToken));
            return;
        }
        if (_cancellationToken.IsCancellationRequested
            || cancellationToken.IsCancellationRequested)
        {
            return;
        }
        _pending.Enqueue(
            (work, cancellationToken),
            (priority, _sequence++));
        Start();
    }

    public void Pause()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Pause);
            return;
        }
        if (_paused)
        {
            return;
        }
        _paused = true;
        _resumeSignal = new TaskCompletionSource<Boolean>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Resume()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Resume);
            return;
        }
        if (_cancellationToken.IsCancellationRequested)
        {
            return;
        }
        _paused = false;
        TaskCompletionSource<Boolean>? signal = _resumeSignal;
        _resumeSignal = null;
        signal?.TrySetResult(true);
        Start();
    }

    public async Task YieldAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.Yield(DispatcherPriority.Background);
        Task? wait = _paused ? _resumeSignal?.Task : null;
        if (wait is not null)
        {
            await wait.WaitAsync(cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private void Start()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Start);
            return;
        }
        if (_running
            || _paused
            || _pending.Count == 0
            || _cancellationToken.IsCancellationRequested)
        {
            return;
        }
        _running = true;
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            if (_pending.Count == 0
                || _paused
                || _cancellationToken.IsCancellationRequested)
            {
                return;
            }
            _cancellationToken.ThrowIfCancellationRequested();
            Func<CancellationToken, Task>? work = null;
            CancellationToken requestCancellation = default;
            (Int32 Priority, Int64 Sequence) requestPriority = default;
            for (Int32 discarded = 0; discarded < 64; discarded++)
            {
                if (_pending.Count == 0)
                {
                    return;
                }
                _pending.TryDequeue(
                    out (Func<CancellationToken, Task> Work,
                        CancellationToken Cancellation) request,
                    out requestPriority);
                if (!request.Cancellation.IsCancellationRequested)
                {
                    work = request.Work;
                    requestCancellation = request.Cancellation;
                    break;
                }
            }
            if (work is null)
            {
                return;
            }
            await Dispatcher.Yield(
                requestPriority.Priority == 0
                    ? DispatcherPriority.Loaded
                    : DispatcherPriority.Background);
            if (_paused)
            {
                _pending.Enqueue(
                    (work, requestCancellation),
                    requestPriority);
                return;
            }
            try
            {
                using CancellationTokenSource linked =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        _cancellationToken,
                        requestCancellation);
                await work(linked.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _failed(exception);
            }
        }
        catch (OperationCanceledException) when (
            _cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (_cancellationToken.IsCancellationRequested)
            {
                _pending.Clear();
            }
            _running = false;
            Start();
        }
    }
}
