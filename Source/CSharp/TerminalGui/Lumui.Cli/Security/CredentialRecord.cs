namespace Lumui.Cli.Security;

public sealed record CredentialRecord(
    Uri Origin,
    String UserName,
    String Password,
    DateTimeOffset UpdatedAt)
{
    public override String ToString() => Origin.Host + "  ·  " + UserName;
}
