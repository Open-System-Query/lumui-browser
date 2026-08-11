namespace Lumui.Cli.Rendering;

public sealed class RenderedTerminalPage
{
    public RenderedTerminalPage(
        IReadOnlyList<TerminalRenderLine> lines,
        IReadOnlyList<String> outline,
        Int32 guidedStepCount)
    {
        Lines = lines;
        Outline = outline;
        GuidedStepCount = guidedStepCount;
    }

    public IReadOnlyList<TerminalRenderLine> Lines { get; }

    public IReadOnlyList<String> Outline { get; }

    public Int32 GuidedStepCount { get; }
}

