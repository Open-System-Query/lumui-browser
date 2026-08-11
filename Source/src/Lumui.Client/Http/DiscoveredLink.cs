namespace Lumui.Client;

internal sealed record DiscoveredLink(
    Uri Uri,
    String Relation,
    String Type);
