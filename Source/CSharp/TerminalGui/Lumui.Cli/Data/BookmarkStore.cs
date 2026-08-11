using Lumui.Cli.Configuration;

namespace Lumui.Cli.Data;

public sealed class BookmarkStore
{
    private readonly List<BookmarkEntry> _entries = new List<BookmarkEntry>();

    public BookmarkStore(Boolean privateMode)
    {
        Load();
    }

    public event Action? Changed;

    public IReadOnlyList<BookmarkEntry> Entries => _entries
        .OrderBy(entry => entry.Folder, StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(entry => entry.Title, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    public Boolean Contains(Uri address) => _entries.Any(entry => entry.Address == address);

    public void Add(Uri address, String title, String folder = "Bookmarks")
    {
        _entries.RemoveAll(entry => entry.Address == address);
        _entries.Add(new BookmarkEntry(
            address,
            String.IsNullOrWhiteSpace(title) ? address.Host : title.Trim(),
            DateTimeOffset.UtcNow,
            NormalizeFolder(folder)));
        Save();
        Changed?.Invoke();
    }

    public void Update(Uri original, Uri address, String title, String folder)
    {
        BookmarkEntry? entry = _entries.FirstOrDefault(item => item.Address == original);
        _entries.RemoveAll(item => item.Address == original || item.Address == address);
        _entries.Add(new BookmarkEntry(
            address,
            String.IsNullOrWhiteSpace(title) ? address.Host : title.Trim(),
            entry?.CreatedAt ?? DateTimeOffset.UtcNow,
            NormalizeFolder(folder)));
        Save();
        Changed?.Invoke();
    }

    public void Remove(Uri address)
    {
        if (_entries.RemoveAll(entry => entry.Address == address) == 0)
        {
            return;
        }
        Save();
        Changed?.Invoke();
    }

    private void Load()
    {
        if (!File.Exists(CliPaths.BookmarksFile))
        {
            return;
        }
        try
        {
            foreach (String line in File.ReadLines(CliPaths.BookmarksFile))
            {
                String[] parts = line.Split('|');
                if (parts.Length is not (3 or 4)
                    || !Uri.TryCreate(LocalDataCodec.Decode(parts[0]), UriKind.Absolute, out Uri? address)
                    || !Int64.TryParse(parts[2], out Int64 timestamp))
                {
                    continue;
                }
                _entries.Add(new BookmarkEntry(
                    address,
                    LocalDataCodec.Decode(parts[1]),
                    DateTimeOffset.FromUnixTimeSeconds(timestamp),
                    parts.Length == 4 ? NormalizeFolder(LocalDataCodec.Decode(parts[3])) : "Bookmarks"));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
        {
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(CliPaths.DataFolder);
        String temporary = CliPaths.BookmarksFile + ".tmp";
        File.WriteAllLines(temporary, _entries.Select(entry => String.Join(
            "|",
            LocalDataCodec.Encode(entry.Address.AbsoluteUri),
            LocalDataCodec.Encode(entry.Title),
            entry.CreatedAt.ToUnixTimeSeconds(),
            LocalDataCodec.Encode(entry.Folder))));
        File.Move(temporary, CliPaths.BookmarksFile, true);
    }

    private static String NormalizeFolder(String folder) =>
        String.IsNullOrWhiteSpace(folder) ? "Bookmarks" : folder.Trim();
}
