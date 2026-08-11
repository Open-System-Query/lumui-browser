using Avalonia.Controls;

namespace Lumui.Browser.Rendering;

public sealed class RenderBlockPlan
{
    public RenderBlockPlan(
        String key,
        Double estimatedHeight,
        Func<CancellationToken, Task<Control>> render)
    {
        Key = key;
        EstimatedHeight = Math.Max(1D, estimatedHeight);
        Render = render;
    }

    public String Key { get; }

    public Double EstimatedHeight { get; }

    public Func<CancellationToken, Task<Control>> Render { get; }
}
