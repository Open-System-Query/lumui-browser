namespace Lumui.Browser.Presentation;

public sealed class OutputModeDefinition
{
    public OutputModeDefinition(
        String id,
        String label,
        OutputMode mode)
    {
        Id = id;
        Label = label;
        Mode = mode;
    }

    public String Id { get; }

    public String Label { get; }

    public OutputMode Mode { get; }

    public override String ToString() => Label;
}
