namespace Lumui.Cli.Views;

internal sealed class ChoiceButton<T> : CliButton where T : struct, Enum
{
    private readonly T[] _values = Enum.GetValues<T>();
    private T _value;

    public ChoiceButton(T value)
    {
        _value = value;
        UpdateText();
        Accepting += (_, _) =>
        {
            Int32 index = Array.IndexOf(_values, _value);
            _value = _values[(index + 1) % _values.Length];
            UpdateText();
        };
    }

    public T SelectedValue
    {
        get => _value;
        set
        {
            _value = value;
            UpdateText();
        }
    }

    private void UpdateText()
    {
        Text = Humanize(_value.ToString());
    }

    private static String Humanize(String value)
    {
        StringBuilder output = new StringBuilder();
        for (Int32 index = 0; index < value.Length; index++)
        {
            if (index > 0 && Char.IsUpper(value[index]) && Char.IsLower(value[index - 1]))
            {
                output.Append(' ');
            }
            output.Append(value[index]);
        }
        return output.ToString();
    }
}
