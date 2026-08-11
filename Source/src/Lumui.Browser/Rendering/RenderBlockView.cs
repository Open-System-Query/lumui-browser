using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Lumui.Browser.Rendering;

public sealed class RenderBlockView : ContentControl
{
    private readonly RenderBlockPlan _plan;
    private readonly DeferredRenderScheduler _scheduler;
    private readonly CancellationToken _documentCancellation;
    private readonly Double _preloadDistance;
    private readonly Double _retentionDistance;
    private CancellationTokenSource? _request;
    private Int64 _generation;
    private Double _reservedHeight;
    private Double _measuredWidth = Double.NaN;

    public RenderBlockView(
        RenderBlockPlan plan,
        DeferredRenderScheduler scheduler,
        CancellationToken documentCancellation,
        Double preloadDistance)
    {
        _plan = plan;
        _scheduler = scheduler;
        _documentCancellation = documentCancellation;
        _preloadDistance = Math.Max(200D, preloadDistance);
        _retentionDistance = Math.Max(
            _preloadDistance + 720D,
            _preloadDistance * 2.5D);
        _reservedHeight = plan.EstimatedHeight;
        MinHeight = _reservedHeight;
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        AttachedToVisualTree += Attached;
        DetachedFromVisualTree += Detached;
        EffectiveViewportChanged += ViewportChanged;
        SizeChanged += BlockSizeChanged;
    }

    private void Attached(Object? sender, VisualTreeAttachmentEventArgs eventArgs)
    {
        MinHeight = _reservedHeight;
    }

    private void Detached(Object? sender, VisualTreeAttachmentEventArgs eventArgs)
    {
        _generation++;
        _request?.Cancel();
        _request?.Dispose();
        _request = null;
        Content = null;
        MinHeight = _reservedHeight;
    }

    private void ViewportChanged(
        Object? sender,
        EffectiveViewportChangedEventArgs eventArgs)
    {
        Rect viewport = eventArgs.EffectiveViewport;
        if (IsNear(viewport, _preloadDistance))
        {
            Start(IsNear(viewport, 0D) ? 0 : 1);
            return;
        }
        if (!IsNear(viewport, _retentionDistance))
        {
            Suspend();
        }
    }

    private void BlockSizeChanged(
        Object? sender,
        SizeChangedEventArgs eventArgs)
    {
        if (!Double.IsFinite(_measuredWidth)
            || Math.Abs(_measuredWidth - eventArgs.NewSize.Width) > 1D)
        {
            _measuredWidth = eventArgs.NewSize.Width;
            _reservedHeight = _plan.EstimatedHeight;
        }
        Double stableHeight = Math.Max(
            _reservedHeight,
            eventArgs.NewSize.Height);
        if (stableHeight > _reservedHeight + 0.5D)
        {
            _reservedHeight = stableHeight;
            MinHeight = _reservedHeight;
        }
    }

    private void Start(Int32 priority)
    {
        if (Content is not null || _request is not null)
        {
            return;
        }
        Int64 generation = ++_generation;
        _request = CancellationTokenSource.CreateLinkedTokenSource(
            _documentCancellation);
        CancellationToken token = _request.Token;
        _scheduler.Enqueue(
            async schedulerToken =>
            {
                try
                {
                    using CancellationTokenSource linked =
                        CancellationTokenSource.CreateLinkedTokenSource(
                            token,
                            schedulerToken);
                    Control control = await RunOnUiThreadAsync(
                        () => _plan.Render(linked.Token));
                    linked.Token.ThrowIfCancellationRequested();
                    await AttachContentAsync(
                        control,
                        generation,
                        linked.Token);
                }
                finally
                {
                    await CompleteRequestAsync(generation);
                }
            },
            priority,
            token);
    }

    private static Task<T> RunOnUiThreadAsync<T>(Func<Task<T>> work)
    {
        return Dispatcher.UIThread.CheckAccess()
            ? work()
            : Dispatcher.UIThread.InvokeAsync(work);
    }

    private async Task AttachContentAsync(
        Control control,
        Int64 generation,
        CancellationToken cancellationToken)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(
                () => AttachContent(
                    control,
                    generation,
                    cancellationToken));
            return;
        }
        AttachContent(control, generation, cancellationToken);
    }

    private void AttachContent(
        Control control,
        Int64 generation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (generation != _generation
            || !this.IsAttachedToVisualTree())
        {
            return;
        }
        MinHeight = _reservedHeight;
        Content = control;
    }

    private async Task CompleteRequestAsync(Int64 generation)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(
                () => CompleteRequest(generation));
            return;
        }
        CompleteRequest(generation);
    }

    private void CompleteRequest(Int64 generation)
    {
        if (generation != _generation)
        {
            return;
        }
        _request?.Dispose();
        _request = null;
    }

    private void Suspend()
    {
        if (_request is not null)
        {
            _generation++;
            _request.Cancel();
            _request.Dispose();
            _request = null;
        }
        if (Content is not Control)
        {
            return;
        }
        _reservedHeight = Math.Max(
            _reservedHeight,
            Math.Max(Bounds.Height, DesiredSize.Height));
        Content = null;
        MinHeight = _reservedHeight;
    }

    private Boolean IsNear(Rect viewport, Double distance)
    {
        if (viewport.Width <= 0D || viewport.Height <= 0D)
        {
            return false;
        }
        Double width = Math.Max(1D, Math.Max(Bounds.Width, DesiredSize.Width));
        Double height = Math.Max(
            _reservedHeight,
            Math.Max(Bounds.Height, DesiredSize.Height));
        return viewport.Right >= -distance
            && viewport.Bottom >= -distance
            && viewport.X <= width + distance
            && viewport.Y <= height + distance;
    }
}
