using Lumui.Cli.Configuration;

namespace Lumui.Cli.Data;

public sealed class HistoryStore
{
    private const Int32 MaximumEntries = 2000;
    private readonly List<HistoryEntry> _entries = new List<HistoryEntry>();
    private readonly Boolean _persistent;

    public HistoryStore(Boolean privateMode)
    {
        _persistent = !privateMode;
        if (_persistent)
        {
            Load();
        }
    }

    public event Action? Changed;

    public IReadOnlyList<HistoryEntry> Entries => _entries
        .OrderByDescending(entry => entry.VisitedAt)
        .ToArray();

    public void Record(Uri address, String title)
    {
        _entries.Add(new HistoryEntry(
            address,
            String.IsNullOrWhiteSpace(title) ? address.Host : title.Trim(),
            DateTimeOffset.UtcNow));
        if (_entries.Count > MaximumEntries)
        {
            _entries.RemoveRange(0, _entries.Count - MaximumEntries);
        }
        Save();
        Changed?.Invoke();
    }

    public void Remove(HistoryEntry entry)
    {
        if (_entries.Remove(entry))
        {
            Save();
            Changed?.Invoke();
        }
    }

    public void Clear()
    {
        _entries.Clear();
        Save();
        Changed?.Invoke();
    }

    private void Load()
    {
        if (!File.Exists(CliPaths.HistoryFile))
        {
            return;
        }
        try
        {
            foreach (String line in File.ReadLines(CliPaths.HistoryFile))
            {
                String[] parts = line.Split('|');
                if (parts.Length != 3
                    || !Uri.TryCreate(LocalDataCodec.Decode(parts[0]), UriKind.Absolute, out Uri? address)
                    || !Int64.TryParse(parts[2], out Int64 timestamp))
                {
                    continue;
                }
                _entries.Add(new HistoryEntry(
                    address,
                    LocalDataCodec.Decode(parts[1]),
                    DateTimeOffset.FromUnixTimeSeconds(timestamp)));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
        {
        }
    }

    private void Save()
    {
        if (!_persistent)
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(CliPaths.DataFolder);
            String temporary = CliPaths.HistoryFile + ".tmp";
            File.WriteAllLines(temporary, _entries.Select(entry => String.Join(
                "|",
                LocalDataCodec.Encode(entry.Address.AbsoluteUri),
                LocalDataCodec.Encode(entry.Title),
                entry.VisitedAt.ToUnixTimeSeconds())));
            File.Move(temporary, CliPaths.HistoryFile, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
