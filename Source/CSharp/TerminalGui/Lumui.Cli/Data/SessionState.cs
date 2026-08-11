namespace Lumui.Cli.Data;

public sealed record SessionState(IReadOnlyList<Uri> Addresses, Int32 ActiveIndex);
