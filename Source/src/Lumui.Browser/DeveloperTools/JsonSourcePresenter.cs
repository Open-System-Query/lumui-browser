using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Styling;

namespace Lumui.Browser.DeveloperTools;

public sealed class JsonSourcePresenter : UserControl
{
    private const String MonospaceFont = "Cascadia Mono, Consolas";
    private readonly ListBox _lines;
    private String _source = String.Empty;
    private Int32 _matchLine = -1;
    private Int32 _matchOffset = -1;

    public JsonSourcePresenter()
    {
        _lines = new ListBox
        {
            Background = Brush("#0C201D"),
            Foreground = Brush("#D8FFF6"),
            FontFamily = new FontFamily(MonospaceFont),
            FontSize = 12D,
            ItemsPanel = new FuncTemplate<Panel?>(() =>
                new VirtualizingStackPanel { CacheLength = 2D }),
            ItemTemplate = new FuncDataTemplate<String>(
                (line, _) => new TextBlock
                {
                    Text = line ?? String.Empty,
                    Height = 23D,
                    Padding = new Thickness(10D, 2D, 18D, 2D),
                    FontFamily = new FontFamily(MonospaceFont),
                    FontSize = 12D,
                    Foreground = Brush("#D8FFF6")
                },
                false)
        };
        _lines.Styles.Add(new Style(selector =>
            selector.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(ListBoxItem.HeightProperty, 23D),
                new Setter(ListBoxItem.MinHeightProperty, 0D),
                new Setter(ListBoxItem.MarginProperty, new Thickness(0D)),
                new Setter(ListBoxItem.PaddingProperty, new Thickness(0D))
            }
        });
        ScrollViewer.SetHorizontalScrollBarVisibility(
            _lines,
            ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(
            _lines,
            ScrollBarVisibility.Auto);
        Content = _lines;
    }

    public void SetText(String source)
    {
        _source = source;
        _matchLine = -1;
        _matchOffset = -1;
        _lines.SelectedIndex = -1;
        String[] sourceLines = source.Replace(
            "\r",
            String.Empty,
            StringComparison.Ordinal).Split('\n');
        Int32 digits = Math.Max(2, sourceLines.Length.ToString().Length);
        String[] displayLines = new String[sourceLines.Length];
        for (Int32 index = 0; index < sourceLines.Length; index++)
        {
            displayLines[index] = (index + 1).ToString().PadLeft(digits)
                + "  "
                + sourceLines[index];
        }
        _lines.ItemsSource = displayLines;
        if (displayLines.Length > 0)
        {
            _lines.ScrollIntoView(0);
        }
    }

    public Boolean Find(String query)
    {
        if (query.Length == 0 || _source.Length == 0)
        {
            return false;
        }
        Int32 start = _matchOffset >= 0
            ? Math.Min(_source.Length, _matchOffset + 1)
            : 0;
        Int32 index = _source.IndexOf(
            query,
            start,
            StringComparison.OrdinalIgnoreCase);
        if (index < 0 && start > 0)
        {
            index = _source.IndexOf(
                query,
                StringComparison.OrdinalIgnoreCase);
        }
        if (index < 0)
        {
            _matchLine = -1;
            _matchOffset = -1;
            _lines.SelectedIndex = -1;
            return false;
        }
        _matchOffset = index;
        _matchLine = 0;
        for (Int32 character = 0; character < index; character++)
        {
            if (_source[character] == '\n')
            {
                _matchLine++;
            }
        }
        _lines.SelectedIndex = _matchLine;
        _lines.ScrollIntoView(_matchLine);
        return true;
    }

    private static IBrush Brush(String value) =>
        new SolidColorBrush(Color.Parse(value));
}
