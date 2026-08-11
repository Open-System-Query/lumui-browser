namespace Lumui.Cli.Views;

public sealed record CliOutlineItem(String Label, Int32 PageIndex, Int32 LineIndex)
{
    public override String ToString() => Label;
}
