namespace Lumui.Cli.Views;

public sealed record MediaLink(String Label, Uri Address)
{
    public override String ToString() => Label + "  ·  " + Address.AbsoluteUri;
}
