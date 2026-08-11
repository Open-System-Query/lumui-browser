namespace Lumui.Browser.Presentation;

public sealed class InteractionModeDefinition
{
    public InteractionModeDefinition(
        String id,
        String label,
        InteractionMode mode)
    {
        Id = id;
        Label = label;
        Mode = mode;
    }

    public String Id { get; }

    public String Label { get; }

    public InteractionMode Mode { get; }

    public override String ToString() => Label;
}
