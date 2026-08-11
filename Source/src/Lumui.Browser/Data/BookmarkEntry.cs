namespace Lumui.Browser.Data;

public sealed record BookmarkEntry(
    Uri Address,
    String Title,
    DateTimeOffset CreatedAt,
    String Folder = "Bookmarks");
