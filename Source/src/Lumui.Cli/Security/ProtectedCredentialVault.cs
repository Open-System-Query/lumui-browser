using Lumui.Cli.Configuration;
using Lumui.Cli.Data;

namespace Lumui.Cli.Security;

public sealed class ProtectedCredentialVault
{
    private static readonly Byte[] Entropy = Encoding.UTF8.GetBytes("Lumui.Browser.Credentials.v1");

    public ProtectedCredentialVault(Boolean privateMode)
    {
    }

    public Boolean IsAvailable => OperatingSystem.IsWindows();

    public IReadOnlyList<CredentialRecord> GetAll()
    {
        if (!IsAvailable || !File.Exists(CliPaths.CredentialsFile))
        {
            return Array.Empty<CredentialRecord>();
        }
        List<CredentialRecord> credentials = new List<CredentialRecord>();
        try
        {
            foreach (String line in File.ReadLines(CliPaths.CredentialsFile))
            {
                CredentialRecord? credential = Parse(line);
                if (credential is not null)
                {
                    credentials.Add(credential);
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or CryptographicException or FormatException)
        {
        }
        return credentials
            .OrderBy(item => item.Origin.Host)
            .ThenBy(item => item.UserName)
            .ToArray();
    }

    public CredentialRecord? Find(Uri origin, String userName) => GetAll()
        .FirstOrDefault(item => SameOrigin(item.Origin, origin)
            && item.UserName.Equals(userName, StringComparison.Ordinal));

    public CredentialRecord? FindForOrigin(Uri origin) => GetAll()
        .FirstOrDefault(item => SameOrigin(item.Origin, origin));

    public void Save(Uri origin, String userName, String password)
    {
        EnsureAvailable();
        List<CredentialRecord> credentials = GetAll().ToList();
        credentials.RemoveAll(item => SameOrigin(item.Origin, origin)
            && item.UserName.Equals(userName, StringComparison.Ordinal));
        credentials.Add(new CredentialRecord(Origin(origin), userName.Trim(), password, DateTimeOffset.UtcNow));
        Write(credentials);
    }

    public void Remove(Uri origin, String userName)
    {
        EnsureAvailable();
        List<CredentialRecord> credentials = GetAll().ToList();
        credentials.RemoveAll(item => SameOrigin(item.Origin, origin)
            && item.UserName.Equals(userName, StringComparison.Ordinal));
        Write(credentials);
    }

    public void Clear()
    {
        EnsureAvailable();
        Write(Array.Empty<CredentialRecord>());
    }

    private static CredentialRecord? Parse(String line)
    {
        String[] parts = line.Split('|');
        if (parts.Length != 4
            || !Uri.TryCreate(LocalDataCodec.Decode(parts[0]), UriKind.Absolute, out Uri? origin)
            || !Int64.TryParse(parts[3], out Int64 timestamp))
        {
            return null;
        }
        Byte[] clear = WindowsDataProtector.Unprotect(Convert.FromBase64String(parts[2]), Entropy);
        try
        {
            return new CredentialRecord(
                origin,
                LocalDataCodec.Decode(parts[1]),
                Encoding.UTF8.GetString(clear),
                DateTimeOffset.FromUnixTimeSeconds(timestamp));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
        }
    }

    private static void Write(IEnumerable<CredentialRecord> credentials)
    {
        Directory.CreateDirectory(CliPaths.DataFolder);
        String temporary = CliPaths.CredentialsFile + ".tmp";
        File.WriteAllLines(temporary, credentials.Select(Serialize));
        File.Move(temporary, CliPaths.CredentialsFile, true);
    }

    private static String Serialize(CredentialRecord credential)
    {
        Byte[] clear = Encoding.UTF8.GetBytes(credential.Password);
        Byte[] encrypted;
        try
        {
            encrypted = WindowsDataProtector.Protect(clear, Entropy);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
        }
        return String.Join(
            "|",
            LocalDataCodec.Encode(Origin(credential.Origin).AbsoluteUri),
            LocalDataCodec.Encode(credential.UserName),
            Convert.ToBase64String(encrypted),
            credential.UpdatedAt.ToUnixTimeSeconds());
    }

    private static Uri Origin(Uri value) => new Uri(value.GetLeftPart(UriPartial.Authority), UriKind.Absolute);

    private static Boolean SameOrigin(Uri first, Uri second) =>
        first.Scheme.Equals(second.Scheme, StringComparison.OrdinalIgnoreCase)
        && first.Host.Equals(second.Host, StringComparison.OrdinalIgnoreCase)
        && first.Port == second.Port;

    private void EnsureAvailable()
    {
        if (!IsAvailable)
        {
            throw new PlatformNotSupportedException("Protected password storage is unavailable on this system.");
        }
    }
}
