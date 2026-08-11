using Lumui.Browser.Configuration;
using Lumui.Browser.Data;

namespace Lumui.Browser.Navigation;

public sealed class BrowserSessionStore
{
    public BrowserSessionState Load()
    {
        if (!File.Exists(BrowserPaths.SessionFile))
        {
            return new BrowserSessionState(Array.Empty<Uri>(), 0);
        }
        try
        {
            String[] lines = File.ReadAllLines(BrowserPaths.SessionFile);
            Int32 activeIndex = lines.Length > 0
                && Int32.TryParse(lines[0], out Int32 parsed)
                    ? parsed
                    : 0;
            List<Uri> addresses = new List<Uri>();
            foreach (String line in lines.Skip(1))
            {
                if (Uri.TryCreate(
                        LocalDataCodec.Decode(line),
                        UriKind.Absolute,
                        out Uri? address))
                {
                    addresses.Add(address);
                }
            }
            return new BrowserSessionState(
                addresses,
                Math.Clamp(activeIndex, 0, Math.Max(0, addresses.Count - 1)));
        }
        catch (IOException)
        {
            return new BrowserSessionState(Array.Empty<Uri>(), 0);
        }
        catch (UnauthorizedAccessException)
        {
            return new BrowserSessionState(Array.Empty<Uri>(), 0);
        }
        catch (FormatException)
        {
            return new BrowserSessionState(Array.Empty<Uri>(), 0);
        }
    }

    public void Save(
        IReadOnlyList<BrowserTabSession> tabs,
        BrowserTabSession? active)
    {
        Directory.CreateDirectory(BrowserPaths.DataFolder);
        BrowserTabSession[] restorable = tabs
            .Where((BrowserTabSession tab) => tab.Address is not null)
            .ToArray();
        Int32 activeIndex = active is null
            ? 0
            : Array.IndexOf(restorable, active);
        if (activeIndex < 0)
        {
            activeIndex = 0;
        }
        String temporary = BrowserPaths.SessionFile + ".tmp";
        File.WriteAllLines(
            temporary,
            new String[] { activeIndex.ToString() }
                .Concat(restorable.Select(
                    (BrowserTabSession tab) =>
                        LocalDataCodec.Encode(tab.Address!.AbsoluteUri))));
        File.Move(temporary, BrowserPaths.SessionFile, true);
    }
}
