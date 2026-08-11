using Lumui.Browser.Configuration;

namespace Lumui.Browser.Data;

public sealed class HistoryStore
{
    private const Int32 MaximumEntries = 2000;
    private readonly List<HistoryEntry> _entries = new List<HistoryEntry>();
    private readonly SemaphoreSlim _saveGate = new SemaphoreSlim(1, 1);
    private Int64 _saveVersion;

    public HistoryStore()
    {
        Load();
    }

    public event Action? Changed;

    public IReadOnlyList<HistoryEntry> Entries => _entries
        .OrderByDescending((HistoryEntry entry) => entry.VisitedAt)
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

    public void Clear()
    {
        _entries.Clear();
        Save();
        Changed?.Invoke();
    }

    public void Remove(HistoryEntry entry)
    {
        if (!_entries.Remove(entry))
        {
            return;
        }
        Save();
        Changed?.Invoke();
    }

    public void RemoveMany(IEnumerable<HistoryEntry> entries)
    {
        HashSet<HistoryEntry> selected = entries.ToHashSet();
        if (selected.Count == 0
            || _entries.RemoveAll((HistoryEntry entry) => selected.Contains(entry)) == 0)
        {
            return;
        }
        Save();
        Changed?.Invoke();
    }

    private void Load()
    {
        if (!File.Exists(BrowserPaths.HistoryFile))
        {
            return;
        }
        try
        {
            foreach (String line in File.ReadLines(BrowserPaths.HistoryFile))
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
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (FormatException)
        {
        }
    }

    private void Save()
    {
        HistoryEntry[] snapshot = _entries.ToArray();
        Int64 version = Interlocked.Increment(ref _saveVersion);
        _ = Task.Run(() => SaveSnapshot(snapshot, version));
    }

    private void SaveSnapshot(
        IReadOnlyList<HistoryEntry> snapshot,
        Int64 version)
    {
        _saveGate.Wait();
        try
        {
            if (version != Volatile.Read(ref _saveVersion))
            {
                return;
            }
            Directory.CreateDirectory(BrowserPaths.DataFolder);
            String temporary = BrowserPaths.HistoryFile + ".tmp";
            File.WriteAllLines(
                temporary,
                snapshot.Select((HistoryEntry entry) => String.Join(
                    "|",
                    LocalDataCodec.Encode(entry.Address.AbsoluteUri),
                    LocalDataCodec.Encode(entry.Title),
                    entry.VisitedAt.ToUnixTimeSeconds())));
            if (version == Volatile.Read(ref _saveVersion))
            {
                File.Move(temporary, BrowserPaths.HistoryFile, true);
            }
            else if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        finally
        {
            _saveGate.Release();
        }
    }
}
