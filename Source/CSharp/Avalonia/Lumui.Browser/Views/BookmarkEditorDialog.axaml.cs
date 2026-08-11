using Avalonia.Controls;
using Avalonia.Input;
using Lumui.Browser.Configuration;
using Lumui.Browser.Data;
using Lumui.Browser.Rendering;
using Lumui.Client;

namespace Lumui.Browser.Views;

public sealed partial class BookmarkEditorDialog : Window
{
    private readonly BookmarkStore _bookmarks;
    private readonly BookmarkEntry? _original;

    public BookmarkEditorDialog()
        : this(BrowserApplicationServices.Current.Bookmarks)
    {
    }

    public BookmarkEditorDialog(
        BookmarkStore bookmarks,
        BookmarkEntry? original = null)
    {
        _bookmarks = bookmarks ?? throw new ArgumentNullException(nameof(bookmarks));
        _original = original;
        InitializeComponent();
        Boolean editing = original is not null;
        Title = (editing ? "Edit bookmark" : "Add bookmark") + " | LUMUI Browser";
        DialogTitleText.Text = editing ? "Edit bookmark" : "Add bookmark";
        DialogDescriptionText.Text = editing
            ? "Change the name, address or folder."
            : "Save a page so it is easy to find again.";
        if (original is not null)
        {
            TitleBox.Text = original.Title;
            AddressBox.Text = original.Address.AbsoluteUri;
            FolderBox.Text = original.Folder;
        }
        else
        {
            FolderBox.Text = "Bookmarks";
        }
        CancelButton.Click += (_, _) => Close(false);
        SaveButton.Click += SaveClicked;
        Opened += (_, _) => TitleBox.Focus();
        KeyDown += WindowKeyDown;
    }

    public Uri? SavedAddress { get; private set; }

    private void SaveClicked(Object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs) =>
        SaveBookmark();

    private void WindowKeyDown(Object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape)
        {
            eventArgs.Handled = true;
            Close(false);
        }
        else if (eventArgs.Key == Key.Enter)
        {
            eventArgs.Handled = true;
            SaveBookmark();
        }
    }

    private void SaveBookmark()
    {
        try
        {
            Uri address = LumuiClient.NormalizeAddress(AddressBox.Text ?? String.Empty);
            String title = (TitleBox.Text ?? String.Empty).Trim();
            String folder = (FolderBox.Text ?? String.Empty).Trim();
            if (title.Length == 0)
            {
                StatusText.Text = "Enter a name for this bookmark.";
                ApplyReadingStyle();
                return;
            }
            if (_original is null)
            {
                _bookmarks.Add(address, title, folder);
            }
            else
            {
                _bookmarks.Update(_original.Address, address, title, folder);
            }
            SavedAddress = address;
            Close(true);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or LumuiProtocolException
                or UriFormatException)
        {
            StatusText.Text = exception.Message;
            ApplyReadingStyle();
        }
    }

    private void ApplyReadingStyle() => ReadingTextFormatter.ApplyTree(
        this,
        Classes.Contains("bionic"));
}
