namespace Lumui.Cli.Rendering;

public sealed class TerminalViewPage
{
    public TerminalViewPage(
        View content,
        Int32 height,
        IReadOnlyList<String> outline,
        Int32 guidedStepCount)
    {
        Content = content;
        Height = height;
        Outline = outline;
        GuidedStepCount = guidedStepCount;
    }

    public View Content { get; }

    public Int32 Height { get; }

    public IReadOnlyList<String> Outline { get; }

    public Int32 GuidedStepCount { get; }
}
