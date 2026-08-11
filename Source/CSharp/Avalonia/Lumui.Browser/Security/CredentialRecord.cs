namespace Lumui.Browser.Security;

public sealed record CredentialRecord(
    Uri Origin,
    String UserName,
    String Password,
    DateTimeOffset UpdatedAt);
