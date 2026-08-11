namespace Lumui.Cli.Rendering;

public sealed class TerminalSiteChrome
{
    public TerminalSiteChrome(
        Boolean hasIdentity,
        String name,
        String shortName,
        SemanticComponent? home,
        SemanticComponent? logo,
        SemanticComponent? icon,
        IReadOnlyList<SemanticComponent> routes,
        IReadOnlyList<SemanticComponent> groups,
        String copyright)
    {
        HasIdentity = hasIdentity;
        Name = name;
        ShortName = shortName;
        Home = home;
        Logo = logo;
        Icon = icon;
        Routes = routes;
        Groups = groups;
        Copyright = copyright;
    }

    public Boolean HasIdentity { get; }

    public String Name { get; }

    public String ShortName { get; }

    public SemanticComponent? Home { get; }

    public SemanticComponent? Logo { get; }

    public SemanticComponent? Icon { get; }

    public IReadOnlyList<SemanticComponent> Routes { get; }

    public IReadOnlyList<SemanticComponent> Groups { get; }

    public String Copyright { get; }
}
