using Avalonia.Controls;
using Avalonia.Input;
using Lumui.Browser.Data;
using Lumui.Browser.Rendering;

namespace Lumui.Browser.Views;

internal sealed class BookmarksManagerPage : IBrowserLibraryPage
{
    private readonly BookmarkStore _bookmarks;

    public BookmarksManagerPage(BookmarkStore bookmarks)
    {
        _bookmarks = bookmarks;
        _bookmarks.Changed += BookmarksChanged;
    }

    public String Title => "Bookmarks";

    public String Description => "Pages you want to find again.";

    public String SearchPlaceholder => "Search bookmarks";

    public String Summary { get; private set; } = String.Empty;

    public String? PrimaryActionText => "Add bookmark";

    public String? SecondaryActionText => null;

    public Boolean PrimaryActionEnabled => true;

    public Boolean SecondaryActionEnabled => false;

    public event Action? Changed;

    public event Action<Uri>? OpenRequested;

    public Control Build(String query)
    {
        BookmarkEntry[] entries = _bookmarks.Entries
            .Where((BookmarkEntry item) => Matches(query, item.Title, item.Address.AbsoluteUri))
            .ToArray();
        Summary = entries.Length == 1
            ? "1 saved page"
            : entries.Length + " saved pages";
        List<ManagerListItem> rows = new List<ManagerListItem>();
        if (entries.Length == 0)
        {
            String title = query.Length == 0
                ? "No bookmarks yet"
                : "No bookmarks found";
            String message = query.Length == 0
                ? "Save a page with the star in the address bar."
                : "Try another title or address.";
            rows.Add(new ManagerListItem(
                () => BrowserManagerControls.EmptyState(title, message)));
            return BrowserManagerControls.VirtualizedPage(rows);
        }
        foreach (IGrouping<String, BookmarkEntry> group in entries
                     .GroupBy((BookmarkEntry item) => item.Folder)
                     .OrderBy((IGrouping<String, BookmarkEntry> item) =>
                         item.Key,
                         StringComparer.CurrentCultureIgnoreCase))
        {
            String groupLabel = group.Key.ToUpperInvariant();
            rows.Add(new ManagerListItem(
                () => BrowserManagerControls.SectionLabel(groupLabel)));
            foreach (BookmarkEntry bookmark in group)
            {
                BookmarkEntry value = bookmark;
                rows.Add(new ManagerListItem(() => BookmarkRow(value)));
            }
        }
        return BrowserManagerControls.VirtualizedPage(rows);
    }

    public async Task PrimaryActionAsync(Window owner)
    {
        BookmarkEditorDialog dialog = new BookmarkEditorDialog(_bookmarks);
        BrowserManagerControls.PrepareDialog(owner, dialog);
        if (await dialog.ShowDialog<Boolean>(owner))
        {
            Changed?.Invoke();
        }
    }

    public Task SecondaryActionAsync(Window owner) => Task.CompletedTask;

    public Boolean HandleKeyDown(Window owner, KeyEventArgs eventArgs) => false;

    public void Dispose()
    {
        _bookmarks.Changed -= BookmarksChanged;
    }

    private Border BookmarkRow(BookmarkEntry bookmark)
    {
        Button open = BrowserManagerControls.ItemLink(
            bookmark.Title,
            BrowserManagerControls.AddressLabel(bookmark.Address),
            BrowserManagerControls.Initial(bookmark.Title),
            String.Empty,
            () => OpenRequested?.Invoke(bookmark.Address));
        Button edit = BrowserManagerControls.IconButton(
            BrowserIcons.Edit,
            "Edit bookmark");
        edit.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(edit) is Window owner)
            {
                await EditAsync(owner, bookmark);
            }
        };
        Button remove = BrowserManagerControls.IconButton(
            BrowserIcons.Clear,
            "Delete bookmark",
            true);
        remove.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(remove) is Window owner)
            {
                await DeleteAsync(owner, bookmark);
            }
        };
        return BrowserManagerControls.ItemRow(
            open,
            BrowserManagerControls.Actions(edit, remove));
    }

    private async Task EditAsync(Window owner, BookmarkEntry bookmark)
    {
        BookmarkEditorDialog dialog = new BookmarkEditorDialog(_bookmarks, bookmark);
        BrowserManagerControls.PrepareDialog(owner, dialog);
        if (await dialog.ShowDialog<Boolean>(owner))
        {
            Changed?.Invoke();
        }
    }

    private async Task DeleteAsync(Window owner, BookmarkEntry bookmark)
    {
        if (await BrowserConfirmationDialog.ShowAsync(
                owner,
                "Delete bookmark",
                "Remove “" + bookmark.Title + "” from your bookmarks?",
                "Delete",
                true))
        {
            _bookmarks.Remove(bookmark.Address);
        }
    }

    private void BookmarksChanged() => Changed?.Invoke();

    private static Boolean Matches(
        String query,
        String first,
        String second) => query.Length == 0
        || first.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || second.Contains(query, StringComparison.CurrentCultureIgnoreCase);
}
