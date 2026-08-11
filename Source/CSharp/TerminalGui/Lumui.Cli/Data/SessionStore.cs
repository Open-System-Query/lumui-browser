using Lumui.Cli.Configuration;
using Lumui.Cli.Navigation;

namespace Lumui.Cli.Data;

public sealed class SessionStore
{
    public SessionState Load()
    {
        if (!File.Exists(CliPaths.SessionFile))
        {
            return new SessionState(Array.Empty<Uri>(), 0);
        }
        try
        {
            String[] lines = File.ReadAllLines(CliPaths.SessionFile);
            Int32 active = lines.Length > 0 && Int32.TryParse(lines[0], out Int32 parsed) ? parsed : 0;
            Uri[] addresses = lines.Skip(1)
                .Select(LocalDataCodec.Decode)
                .Select(value => Uri.TryCreate(value, UriKind.Absolute, out Uri? address) ? address : null)
                .Where(address => address is not null)
                .Cast<Uri>()
                .ToArray();
            return new SessionState(addresses, Math.Clamp(active, 0, Math.Max(0, addresses.Length - 1)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
        {
            return new SessionState(Array.Empty<Uri>(), 0);
        }
    }

    public void Save(IReadOnlyList<CliTabSession> tabs, CliTabSession? active)
    {
        Directory.CreateDirectory(CliPaths.DataFolder);
        CliTabSession[] restorable = tabs.Where(tab => tab.Address is not null).ToArray();
        Int32 activeIndex = active is null ? 0 : Array.IndexOf(restorable, active);
        String temporary = CliPaths.SessionFile + ".tmp";
        File.WriteAllLines(
            temporary,
            new String[] { Math.Max(0, activeIndex).ToString(CultureInfo.InvariantCulture) }
                .Concat(restorable.Select(tab => LocalDataCodec.Encode(tab.Address!.AbsoluteUri))));
        File.Move(temporary, CliPaths.SessionFile, true);
    }
}

