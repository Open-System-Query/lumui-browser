using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Lumui.Browser.Navigation;
using Lumui.Browser.Rendering;

namespace Lumui.Browser.Views;

public sealed class BrowserTabHeader
{
    private readonly TextBlock _title;
    private readonly ToggleButton _select;
    private readonly Button _close;
    private String _displayedTitle = String.Empty;
    private Boolean _bionicReading;

    public BrowserTabHeader(
        BrowserTabSession tab,
        Action<BrowserTabSession> activate,
        Action<BrowserTabSession> close)
    {
        Tab = tab ?? throw new ArgumentNullException(nameof(tab));
        _title = new TextBlock
        {
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        _select = new ToggleButton
        {
            Content = _title
        };
        _select.Classes.Add("tab-title");
        _select.Click += (_, _) => activate(Tab);
        _close = new Button
        {
            Content = "×"
        };
        _close.Classes.Add("tab-close");
        _close.Click += (_, _) => close(Tab);
        Grid.SetColumn(_close, 1);
        Grid grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };
        grid.Children.Add(_select);
        grid.Children.Add(_close);
        Root = new Border
        {
            Child = grid
        };
        Root.Classes.Add("browser-tab");
    }

    public BrowserTabSession Tab { get; }

    public Border Root { get; }

    public void Update(Boolean active, Boolean bionicReading)
    {
        String title = String.IsNullOrWhiteSpace(Tab.Title)
            ? "New tab"
            : Tab.Title;
        if (!_displayedTitle.Equals(title, StringComparison.Ordinal)
            || _bionicReading != bionicReading)
        {
            ReadingTextFormatter.Apply(_title, title, bionicReading);
            _displayedTitle = title;
            _bionicReading = bionicReading;
        }
        _select.IsChecked = active;
        SetClass(Root, "selected", active);
        AutomationProperties.SetName(_select, "Open tab " + title);
        AutomationProperties.SetName(_close, "Close tab " + title);
    }

    private static void SetClass(Control control, String name, Boolean enabled)
    {
        if (enabled && !control.Classes.Contains(name))
        {
            control.Classes.Add(name);
        }
        else if (!enabled)
        {
            control.Classes.Remove(name);
        }
    }
}
