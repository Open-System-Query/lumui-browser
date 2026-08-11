using Lumui.Browser.Configuration;

namespace Lumui.Browser.Data;

public sealed class BookmarkStore
{
    private readonly List<BookmarkEntry> _entries = new List<BookmarkEntry>();

    public BookmarkStore()
    {
        Load();
    }

    public event Action? Changed;

    public IReadOnlyList<BookmarkEntry> Entries => _entries
        .OrderBy((BookmarkEntry entry) => entry.Title, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    public Boolean Contains(Uri address) => _entries.Any(
        (BookmarkEntry entry) => entry.Address == address);

    public void Add(Uri address, String title, String folder = "Bookmarks")
    {
        Remove(address, false);
        _entries.Add(new BookmarkEntry(
            address,
            String.IsNullOrWhiteSpace(title) ? address.Host : title.Trim(),
            DateTimeOffset.UtcNow,
            NormalizeFolder(folder)));
        Save();
        Changed?.Invoke();
    }

    public void Update(
        Uri originalAddress,
        Uri address,
        String title,
        String folder)
    {
        BookmarkEntry? original = _entries.FirstOrDefault(
            (BookmarkEntry entry) => entry.Address == originalAddress);
        Remove(originalAddress, false);
        if (address != originalAddress)
        {
            Remove(address, false);
        }
        _entries.Add(new BookmarkEntry(
            address,
            String.IsNullOrWhiteSpace(title) ? address.Host : title.Trim(),
            original?.CreatedAt ?? DateTimeOffset.UtcNow,
            NormalizeFolder(folder)));
        Save();
        Changed?.Invoke();
    }

    public void Remove(Uri address) => Remove(address, true);

    private void Remove(Uri address, Boolean save)
    {
        _entries.RemoveAll((BookmarkEntry entry) => entry.Address == address);
        if (save)
        {
            Save();
            Changed?.Invoke();
        }
    }

    private void Load()
    {
        if (!File.Exists(BrowserPaths.BookmarksFile))
        {
            return;
        }
        try
        {
            foreach (String line in File.ReadLines(BrowserPaths.BookmarksFile))
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
                    parts.Length == 4
                        ? NormalizeFolder(LocalDataCodec.Decode(parts[3]))
                        : "Bookmarks"));
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
        Directory.CreateDirectory(BrowserPaths.DataFolder);
        String temporary = BrowserPaths.BookmarksFile + ".tmp";
        File.WriteAllLines(
            temporary,
            _entries.Select((BookmarkEntry entry) => String.Join(
                "|",
                LocalDataCodec.Encode(entry.Address.AbsoluteUri),
                LocalDataCodec.Encode(entry.Title),
                entry.CreatedAt.ToUnixTimeSeconds(),
                LocalDataCodec.Encode(entry.Folder))));
        File.Move(temporary, BrowserPaths.BookmarksFile, true);
    }

    private static String NormalizeFolder(String folder) =>
        String.IsNullOrWhiteSpace(folder) ? "Bookmarks" : folder.Trim();
}
