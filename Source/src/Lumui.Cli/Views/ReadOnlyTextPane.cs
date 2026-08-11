namespace Lumui.Cli.Views;

public sealed class ReadOnlyTextPane : ListView
{
    private ObservableCollection<String> _lines = new ObservableCollection<String>();

    public ReadOnlyTextPane()
    {
        SetSource(_lines);
        HorizontalScrollBar.Visible = true;
        VerticalScrollBar.Visible = true;
    }

    public String Content { get; private set; } = String.Empty;

    public void SetContent(String value)
    {
        Content = value ?? String.Empty;
        _lines = new ObservableCollection<String>(
            Content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
                .Select(ExpandTabs));
        SetSource(_lines);
        Value = _lines.Count > 0 ? 0 : null;
    }

    public Boolean Find(String value)
    {
        if (String.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        Int32 start = Value is Int32 current ? current + 1 : 0;
        for (Int32 offset = 0; offset < _lines.Count; offset++)
        {
            Int32 index = (start + offset) % _lines.Count;
            if (_lines[index].Contains(value, StringComparison.CurrentCultureIgnoreCase))
            {
                Value = index;
                SetFocus();
                return true;
            }
        }
        return false;
    }

    private static String ExpandTabs(String value)
    {
        StringBuilder output = new StringBuilder(value.Length);
        foreach (Char character in value)
        {
            if (character != '\t')
            {
                output.Append(character);
                continue;
            }
            output.Append(' ', 4 - output.Length % 4);
        }
        return output.ToString();
    }
}
