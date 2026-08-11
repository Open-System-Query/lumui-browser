namespace Lumui.Cli.Rendering;

public sealed class SemanticComponent
{
    public SemanticComponent(
        JsonElement element,
        String id,
        String kind,
        String label,
        String text,
        String actionId,
        Uri? target,
        IReadOnlyList<SemanticComponent> children,
        IReadOnlyList<SemanticOption> options,
        IReadOnlyList<MediaSourceDescriptor> mediaSources)
    {
        Element = element.Clone();
        Id = id;
        Kind = kind;
        Label = label;
        Text = text;
        ActionId = actionId;
        Target = target;
        Children = children;
        Options = options;
        MediaSources = mediaSources;
    }

    public JsonElement Element { get; }

    public String Id { get; }

    public String Kind { get; }

    public String Label { get; }

    public String Text { get; }

    public String ActionId { get; }

    public Uri? Target { get; }

    public IReadOnlyList<SemanticComponent> Children { get; }

    public IReadOnlyList<SemanticOption> Options { get; }

    public IReadOnlyList<MediaSourceDescriptor> MediaSources { get; }

    public Uri? OriginSurfaceUri { get; private set; }

    public Boolean Enabled => !Element.TryGetProperty(LumuiProtocol.Fields.Enabled, out JsonElement enabled)
        || enabled.ValueKind != JsonValueKind.False;

    public Boolean Visible => !Element.TryGetProperty(LumuiProtocol.Fields.Visible, out JsonElement visible)
        || visible.ValueKind != JsonValueKind.False;

    public Boolean ReadOnly => Element.TryGetProperty(LumuiProtocol.Fields.Readonly, out JsonElement value)
        && value.ValueKind == JsonValueKind.True;

    internal void SetOrigin(Uri origin)
    {
        OriginSurfaceUri = origin;
        foreach (SemanticComponent child in Children)
        {
            child.SetOrigin(origin);
        }
    }

    public override String ToString() => Label.Length > 0 ? Label : Kind;
}
