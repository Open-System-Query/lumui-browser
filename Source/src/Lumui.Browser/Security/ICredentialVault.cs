namespace Lumui.Browser.Security;

public interface ICredentialVault
{
    Boolean IsAvailable { get; }

    IReadOnlyList<CredentialRecord> GetAll();

    CredentialRecord? Find(Uri origin, String userName);

    CredentialRecord? FindForOrigin(Uri origin);

    void Save(Uri origin, String userName, String password);

    void Remove(Uri origin, String userName);

    void Clear();
}
