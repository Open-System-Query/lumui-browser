namespace Lumui.Cli.Rendering;

public sealed record SemanticOption(String Label, Object? Value, String Description)
{
    public override String ToString() => Description.Length == 0 ? Label : Label + "  ·  " + Description;
}

