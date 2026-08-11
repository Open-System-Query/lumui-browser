using System.Text;

namespace Lumui.Browser.Data;

public static class LocalDataCodec
{
    public static String Encode(String value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    public static String Decode(String value)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch (FormatException)
        {
            return String.Empty;
        }
    }
}
