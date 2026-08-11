using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Lumui.Browser.Rendering;
using Lumui.Browser.Security;

namespace Lumui.Browser.Views;

internal sealed class PasswordsManagerPage : IBrowserLibraryPage
{
    private readonly ICredentialVault _credentials;
    private String? _selectedKey;
    private String? _notice;
    private Boolean _passwordRevealed;

    public PasswordsManagerPage(ICredentialVault credentials)
    {
        _credentials = credentials;
    }

    public String Title => "Passwords";

    public String Description => "Saved sign-ins on this device.";

    public String SearchPlaceholder => "Search passwords";

    public String Summary { get; private set; } = String.Empty;

    public String? PrimaryActionText => "Add password";

    public String? SecondaryActionText => null;

    public Boolean PrimaryActionEnabled => _credentials.IsAvailable;

    public Boolean SecondaryActionEnabled => false;

    public event Action? Changed;

    public Control Build(String query)
    {
        if (!_credentials.IsAvailable)
        {
            Summary = "Password storage is unavailable.";
            StackPanel unavailable = BrowserManagerControls.List();
            unavailable.Children.Add(BrowserManagerControls.EmptyState(
                "Passwords unavailable",
                "Saved passwords cannot be opened on this device."));
            return BrowserManagerControls.ScrollPage(unavailable);
        }
        CredentialRecord[] records = _credentials.GetAll()
            .Where((CredentialRecord item) => Matches(
                query,
                item.Origin.Host,
                item.UserName))
            .OrderBy((CredentialRecord item) =>
                item.Origin.Host,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy((CredentialRecord item) =>
                item.UserName,
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        if (_selectedKey is null
            || !records.Any((CredentialRecord item) => CredentialKey(item) == _selectedKey))
        {
            _selectedKey = records.FirstOrDefault() is CredentialRecord first
                ? CredentialKey(first)
                : null;
            _passwordRevealed = false;
        }
        Summary = _notice ?? (records.Length == 1
            ? "1 saved sign-in"
            : records.Length + " saved sign-ins");
        _notice = null;
        if (records.Length == 0)
        {
            StackPanel emptyHost = BrowserManagerControls.List();
            emptyHost.Children.Add(BrowserManagerControls.EmptyState(
                query.Length == 0 ? "No saved passwords" : "No passwords found",
                query.Length == 0
                    ? "Add a password when you are ready."
                    : "Try another website or username."));
            return BrowserManagerControls.ScrollPage(emptyHost);
        }
        List<ManagerListItem> rows = new List<ManagerListItem>();
        foreach (CredentialRecord credential in records)
        {
            CredentialRecord value = credential;
            rows.Add(new ManagerListItem(() => CredentialRow(value)));
        }
        ItemsControl list = new ItemsControl
        {
            ItemsSource = rows,
            ItemTemplate = new FuncDataTemplate<ManagerListItem>(
                (item, _) => item?.Create(),
                false),
            ItemsPanel = new FuncTemplate<Panel?>(() =>
                new VirtualizingStackPanel { CacheLength = 1D }),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        ScrollViewer listScroll = new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        CredentialRecord selected = records.First((CredentialRecord item) =>
            CredentialKey(item) == _selectedKey);
        Grid layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("360,*"),
            ColumnSpacing = 28D,
            Margin = new Thickness(32D),
            MaxWidth = 1180D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        layout.Children.Add(listScroll);
        Border detail = CredentialDetail(selected);
        Grid.SetColumn(detail, 1);
        layout.Children.Add(detail);
        return layout;
    }

    public async Task PrimaryActionAsync(Window owner)
    {
        await ShowEditorAsync(owner, null);
    }

    public Task SecondaryActionAsync(Window owner) => Task.CompletedTask;

    public Boolean HandleKeyDown(Window owner, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape && _passwordRevealed)
        {
            _passwordRevealed = false;
            Changed?.Invoke();
            return true;
        }
        return false;
    }

    public void Dispose()
    {
    }

    private Border CredentialRow(CredentialRecord credential)
    {
        Button button = BrowserManagerControls.ItemLink(
            credential.Origin.Host,
            credential.UserName,
            BrowserManagerControls.Initial(credential.Origin.Host),
            "password",
            () =>
            {
                _selectedKey = CredentialKey(credential);
                _passwordRevealed = false;
                Changed?.Invoke();
            });
        AutomationProperties.SetName(
            button,
            "View saved sign-in for " + credential.Origin.Host);
        if (CredentialKey(credential) == _selectedKey)
        {
            button.Classes.Add("selected");
        }
        Border row = new Border { Child = button };
        row.Classes.Add("manager-row");
        return row;
    }

    private Border CredentialDetail(CredentialRecord credential)
    {
        StackPanel content = new StackPanel { Spacing = 18D };
        content.Children.Add(new TextBlock
        {
            Text = credential.Origin.Host,
            FontSize = 24D,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = "Updated " + credential.UpdatedAt.ToLocalTime().ToString("D"),
            Classes = { "subtle" }
        });
        content.Children.Add(DetailField("Website", credential.Origin.AbsoluteUri, null));
        content.Children.Add(DetailField(
            "Username",
            credential.UserName,
            (Window owner) => CopyAsync(owner, credential.UserName, "Username")));
        String password = _passwordRevealed
            ? credential.Password
            : new String('●', Math.Clamp(credential.Password.Length, 8, 16));
        content.Children.Add(DetailField(
            "Password",
            password,
            (Window owner) => CopyPasswordAsync(owner, credential)));
        StackPanel actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8D
        };
        Button reveal = BrowserManagerControls.TextButton(
            _passwordRevealed ? "Hide password" : "Show password");
        reveal.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(reveal) is Window owner)
            {
                await ToggleVisibilityAsync(owner);
            }
        };
        Button edit = BrowserManagerControls.TextButton("Edit");
        edit.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(edit) is Window owner)
            {
                await ShowEditorAsync(owner, credential);
            }
        };
        Button remove = BrowserManagerControls.TextButton("Delete", true);
        remove.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(remove) is Window owner)
            {
                await DeleteAsync(owner, credential);
            }
        };
        actions.Children.Add(reveal);
        actions.Children.Add(edit);
        actions.Children.Add(remove);
        content.Children.Add(actions);
        Border detail = new Border { Child = content };
        detail.Classes.Add("detail-surface");
        return detail;
    }

    private Border DetailField(
        String label,
        String value,
        Func<Window, Task>? copy)
    {
        StackPanel text = new StackPanel { Spacing = 4D };
        text.Children.Add(new TextBlock
        {
            Text = label.ToUpperInvariant(),
            FontSize = 11D,
            FontWeight = FontWeight.Bold,
            Classes = { "subtle" },
            LetterSpacing = 0.7D
        });
        text.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 15D,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2
        });
        Grid grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8D
        };
        grid.Children.Add(text);
        if (copy is not null)
        {
            Button button = BrowserManagerControls.IconButton(
                BrowserIcons.Copy,
                "Copy " + label.ToLowerInvariant());
            button.Click += async (_, _) =>
            {
                if (TopLevel.GetTopLevel(button) is Window owner)
                {
                    await copy(owner);
                }
            };
            Grid.SetColumn(button, 1);
            grid.Children.Add(button);
        }
        Border field = new Border { Child = grid };
        field.Classes.Add("detail-field");
        return field;
    }

    private async Task ToggleVisibilityAsync(Window owner)
    {
        if (!_passwordRevealed
            && !await BrowserConfirmationDialog.ShowAsync(
                owner,
                "Show password",
                "Make this password visible on screen?",
                "Show password",
                false))
        {
            return;
        }
        _passwordRevealed = !_passwordRevealed;
        Changed?.Invoke();
    }

    private async Task CopyPasswordAsync(
        Window owner,
        CredentialRecord credential)
    {
        if (!await BrowserConfirmationDialog.ShowAsync(
                owner,
                "Copy password",
                "Copy this password to the clipboard?",
                "Copy password",
                false))
        {
            return;
        }
        await CopyAsync(owner, credential.Password, "Password");
    }

    private async Task CopyAsync(
        Window owner,
        String value,
        String label)
    {
        IClipboard? clipboard = owner.Clipboard;
        if (clipboard is null)
        {
            _notice = "The clipboard is unavailable.";
            Changed?.Invoke();
            return;
        }
        await ClipboardExtensions.SetTextAsync(clipboard, value);
        _notice = label + " copied.";
        Changed?.Invoke();
    }

    private async Task ShowEditorAsync(
        Window owner,
        CredentialRecord? credential)
    {
        CredentialEditorDialog dialog = new CredentialEditorDialog(
            _credentials,
            credential);
        BrowserManagerControls.PrepareDialog(owner, dialog);
        if (await dialog.ShowDialog<Boolean>(owner))
        {
            _selectedKey = dialog.SavedCredentialKey;
            _passwordRevealed = false;
            Changed?.Invoke();
        }
    }

    private async Task DeleteAsync(Window owner, CredentialRecord credential)
    {
        if (!await BrowserConfirmationDialog.ShowAsync(
                owner,
                "Delete password",
                "Remove the saved sign-in for " + credential.Origin.Host + "?",
                "Delete",
                true))
        {
            return;
        }
        _credentials.Remove(credential.Origin, credential.UserName);
        _selectedKey = null;
        _passwordRevealed = false;
        Changed?.Invoke();
    }

    private static String CredentialKey(CredentialRecord credential) =>
        credential.Origin.AbsoluteUri + "\n" + credential.UserName;

    private static Boolean Matches(
        String query,
        String first,
        String second) => query.Length == 0
        || first.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || second.Contains(query, StringComparison.CurrentCultureIgnoreCase);
}
