namespace Lumui.Cli.Data;

public sealed record HistoryEntry(
    Uri Address,
    String Title,
    DateTimeOffset VisitedAt)
{
    public override String ToString() => VisitedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
        + "  " + Title + "  ·  " + Address.AbsoluteUri;
}
