namespace Lumui.Cli.Rendering;

public sealed class TerminalPage
{
    public TerminalPage(
        String id,
        String title,
        String description,
        String role,
        IReadOnlyList<SemanticComponent> components)
    {
        Id = id;
        Title = title;
        Description = description;
        Role = role;
        Components = components;
    }

    public String Id { get; }

    public String Title { get; }

    public String Description { get; }

    public String Role { get; }

    public IReadOnlyList<SemanticComponent> Components { get; }

    public override String ToString() => Title;
}
