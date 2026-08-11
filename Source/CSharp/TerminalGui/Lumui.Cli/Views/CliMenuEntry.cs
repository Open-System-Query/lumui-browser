namespace Lumui.Cli.Views;

public sealed class CliMenuEntry
{
    private Int32 _labelWidth;

    public CliMenuEntry(String key, String label, String shortcut = "")
    {
        Key = key;
        Label = label;
        Shortcut = shortcut;
        _labelWidth = label.Length;
    }

    public String Key { get; }

    public String Label { get; }

    public String Shortcut { get; }

    public static void Align(IEnumerable<CliMenuEntry> entries)
    {
        CliMenuEntry[] values = entries.ToArray();
        Int32 width = values.Length == 0
            ? 0
            : Math.Clamp(values.Max(entry => entry.Label.Length) + 3, 18, 36);
        foreach (CliMenuEntry entry in values)
        {
            entry._labelWidth = width;
        }
    }

    public override String ToString() =>
        Shortcut.Length == 0
            ? Label
            : Label.PadRight(_labelWidth) + Shortcut;
}
