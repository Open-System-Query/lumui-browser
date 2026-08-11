namespace Lumui.Cli.Data;

public sealed record BookmarkEntry(
    Uri Address,
    String Title,
    DateTimeOffset CreatedAt,
    String Folder = "Bookmarks")
{
    public override String ToString() => Folder + "  ·  " + Title + "  ·  " + Address.AbsoluteUri;
}
