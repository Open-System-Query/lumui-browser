namespace Lumui.Browser.Navigation;

public sealed record BrowserSessionState(
    IReadOnlyList<Uri> Addresses,
    Int32 ActiveIndex);
