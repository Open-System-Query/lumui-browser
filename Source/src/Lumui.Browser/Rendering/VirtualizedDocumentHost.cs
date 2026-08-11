using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace Lumui.Browser.Rendering;

public sealed class VirtualizedDocumentHost : ContentControl
{
    public VirtualizedDocumentHost(
        IReadOnlyList<RenderBlockPlan> blocks,
        DeferredRenderScheduler scheduler,
        CancellationToken cancellationToken,
        Double cacheLength = 1.25D)
    {
        StackPanel items = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Double preloadDistance = 720D
            + Math.Clamp(cacheLength, 0D, 2D) * 960D;
        foreach (RenderBlockPlan block in blocks)
        {
            items.Children.Add(new RenderBlockView(
                block,
                scheduler,
                cancellationToken,
                preloadDistance));
        }
        Viewport = new ScrollViewer
        {
            Content = items,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top
        };
        Content = Viewport;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
    }

    public ScrollViewer Viewport { get; }
}
