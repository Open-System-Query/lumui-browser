using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Lumui.Browser.Data;
using Lumui.Browser.Rendering;

namespace Lumui.Browser.Views;

internal sealed class HistoryManagerPage : IBrowserLibraryPage
{
    private const Int32 PageSize = 200;
    private readonly HistoryStore _history;
    private readonly HashSet<HistoryEntry> _selected = new HashSet<HistoryEntry>();
    private String _query = String.Empty;
    private Int32 _visibleCount = PageSize;

    public HistoryManagerPage(HistoryStore history)
    {
        _history = history;
        _history.Changed += HistoryChanged;
    }

    public String Title => "History";

    public String Description => "Pages opened in Lumi.";

    public String SearchPlaceholder => "Search history";

    public String Summary { get; private set; } = String.Empty;

    public String? PrimaryActionText => null;

    public String? SecondaryActionText => "Clear history";

    public Boolean PrimaryActionEnabled => false;

    public Boolean SecondaryActionEnabled => _history.Entries.Count > 0;

    public event Action? Changed;

    public event Action<Uri>? OpenRequested;

    public Control Build(String query)
    {
        if (!String.Equals(_query, query, StringComparison.Ordinal))
        {
            _visibleCount = PageSize;
        }
        _query = query;
        HistoryEntry[] allEntries = Filtered().ToArray();
        HistoryEntry[] entries = allEntries.Take(_visibleCount).ToArray();
        _selected.RemoveWhere((HistoryEntry item) => !_history.Entries.Contains(item));
        Summary = allEntries.Length == 1
            ? "1 page in history"
            : allEntries.Length + " pages in history";
        List<ManagerListItem> items = new List<ManagerListItem>();
        IGrouping<DateTime, HistoryEntry>[] groups = entries
            .GroupBy((HistoryEntry item) => item.VisitedAt.ToLocalTime().Date)
            .OrderByDescending((IGrouping<DateTime, HistoryEntry> group) => group.Key)
            .ToArray();
        foreach (IGrouping<DateTime, HistoryEntry> group in groups)
        {
            String groupLabel = DateLabel(group.Key).ToUpperInvariant();
            items.Add(new ManagerListItem(
                () => BrowserManagerControls.SectionLabel(groupLabel)));
            foreach (HistoryEntry entry in group)
            {
                HistoryEntry value = entry;
                items.Add(new ManagerListItem(() => HistoryRow(value)));
            }
        }
        if (entries.Length == 0)
        {
            String title = query.Length == 0
                ? "No history yet"
                : "No history found";
            String message = query.Length == 0
                ? "Pages you open will appear here."
                : "Try another title or address.";
            items.Add(new ManagerListItem(
                () => BrowserManagerControls.EmptyState(title, message)));
        }
        else if (entries.Length < allEntries.Length)
        {
            Int32 nextCount = Math.Min(
                PageSize,
                allEntries.Length - entries.Length);
            items.Add(new ManagerListItem(() =>
            {
                Button more = BrowserManagerControls.TextButton(
                    "Show " + nextCount + " more");
                more.Margin = new Avalonia.Thickness(0D, 18D, 0D, 0D);
                more.HorizontalAlignment = HorizontalAlignment.Center;
                more.Click += (_, _) =>
                {
                    _visibleCount += PageSize;
                    Changed?.Invoke();
                };
                return more;
            }));
        }
        Grid layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        if (_selected.Count > 0)
        {
            layout.Children.Add(SelectionBar());
        }
        Control page = BrowserManagerControls.VirtualizedPage(items);
        Grid.SetRow(page, 1);
        layout.Children.Add(page);
        return layout;
    }

    public Task PrimaryActionAsync(Window owner) => Task.CompletedTask;

    public async Task SecondaryActionAsync(Window owner)
    {
        if (!await BrowserConfirmationDialog.ShowAsync(
                owner,
                "Clear history",
                "Remove every page from your browsing history?",
                "Delete history",
                true))
        {
            return;
        }
        _selected.Clear();
        _history.Clear();
    }

    public Boolean HandleKeyDown(Window owner, KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyModifiers == KeyModifiers.Control && eventArgs.Key == Key.A)
        {
            _selected.Clear();
            foreach (HistoryEntry entry in Filtered())
            {
                _selected.Add(entry);
            }
            Changed?.Invoke();
            return true;
        }
        if (eventArgs.Key == Key.Delete && _selected.Count > 0)
        {
            _ = DeleteSelectedAsync(owner);
            return true;
        }
        if (eventArgs.Key == Key.Escape && _selected.Count > 0)
        {
            _selected.Clear();
            Changed?.Invoke();
            return true;
        }
        return false;
    }

    public void Dispose()
    {
        _history.Changed -= HistoryChanged;
    }

    private Border HistoryRow(HistoryEntry entry)
    {
        CheckBox selection = new CheckBox
        {
            IsChecked = _selected.Contains(entry),
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(selection, "Select " + entry.Title);
        selection.IsCheckedChanged += (_, _) =>
        {
            if (selection.IsChecked == true)
            {
                _selected.Add(entry);
            }
            else
            {
                _selected.Remove(entry);
            }
            Changed?.Invoke();
        };
        Button open = BrowserManagerControls.ItemLink(
            entry.Title,
            entry.Address.Host + "   " + entry.VisitedAt.ToLocalTime().ToString("t"),
            BrowserManagerControls.Initial(entry.Title),
            "history",
            () => OpenRequested?.Invoke(entry.Address));
        Button remove = BrowserManagerControls.IconButton(
            BrowserIcons.Clear,
            "Delete from history",
            true);
        remove.Click += (_, _) => _history.Remove(entry);
        Grid content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 10D
        };
        content.Children.Add(selection);
        Grid.SetColumn(open, 1);
        content.Children.Add(open);
        Grid.SetColumn(remove, 2);
        content.Children.Add(remove);
        Border row = new Border { Child = content };
        row.Classes.Add("manager-row");
        return row;
    }

    private Border SelectionBar()
    {
        TextBlock count = new TextBlock
        {
            Text = _selected.Count == 1
                ? "1 page selected"
                : _selected.Count + " pages selected",
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Button cancel = BrowserManagerControls.TextButton("Cancel");
        cancel.Click += (_, _) =>
        {
            _selected.Clear();
            Changed?.Invoke();
        };
        Button delete = BrowserManagerControls.TextButton("Delete selected", true);
        delete.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(delete) is Window owner)
            {
                await DeleteSelectedAsync(owner);
            }
        };
        Grid content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8D,
            MaxWidth = 1120D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        content.Children.Add(count);
        Grid.SetColumn(cancel, 1);
        content.Children.Add(cancel);
        Grid.SetColumn(delete, 2);
        content.Children.Add(delete);
        Border bar = new Border { Child = content };
        bar.Classes.Add("selection-bar");
        return bar;
    }

    private async Task DeleteSelectedAsync(Window owner)
    {
        Int32 count = _selected.Count;
        if (count == 0
            || !await BrowserConfirmationDialog.ShowAsync(
                owner,
                "Delete selected history",
                "Remove " + count + (count == 1 ? " selected page?" : " selected pages?"),
                "Delete",
                true))
        {
            return;
        }
        _history.RemoveMany(_selected);
        _selected.Clear();
    }

    private IEnumerable<HistoryEntry> Filtered() => _history.Entries.Where(
        (HistoryEntry item) => _query.Length == 0
            || item.Title.Contains(_query, StringComparison.CurrentCultureIgnoreCase)
            || item.Address.AbsoluteUri.Contains(
                _query,
                StringComparison.CurrentCultureIgnoreCase));

    private void HistoryChanged() => Changed?.Invoke();

    private static String DateLabel(DateTime date)
    {
        DateTime today = DateTime.Today;
        if (date == today)
        {
            return "Today";
        }
        if (date == today.AddDays(-1D))
        {
            return "Yesterday";
        }
        return date.ToString("D");
    }
}
