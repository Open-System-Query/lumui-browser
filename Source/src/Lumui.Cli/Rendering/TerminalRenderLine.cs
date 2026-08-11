namespace Lumui.Cli.Rendering;

public sealed record TerminalRenderLine(
    String Text,
    TerminalLineRole Role,
    SemanticComponent? Component = null,
    TerminalInteraction Interaction = TerminalInteraction.None)
{
    public Boolean IsInteractive => Component is not null && Interaction != TerminalInteraction.None;

    public override String ToString() => Text;
}

