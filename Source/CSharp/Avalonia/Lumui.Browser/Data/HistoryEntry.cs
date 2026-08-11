namespace Lumui.Browser.Data;

public sealed record HistoryEntry(
    Uri Address,
    String Title,
    DateTimeOffset VisitedAt);
