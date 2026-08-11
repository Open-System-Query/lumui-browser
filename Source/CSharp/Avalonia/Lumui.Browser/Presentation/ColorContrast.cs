using Avalonia.Media;

namespace Lumui.Browser.Presentation;

public static class ColorContrast
{
    private const Double MinimumTextRatio = 4.5D;

    public static Boolean IsReadable(String foreground, String background)
    {
        Color foregroundColor = Color.Parse(foreground);
        Color backgroundColor = Color.Parse(background);
        Double lighter = Math.Max(
            Luminance(foregroundColor),
            Luminance(backgroundColor));
        Double darker = Math.Min(
            Luminance(foregroundColor),
            Luminance(backgroundColor));
        return (lighter + 0.05D) / (darker + 0.05D)
            >= MinimumTextRatio;
    }

    private static Double Luminance(Color color) =>
        (0.2126D * Channel(color.R))
        + (0.7152D * Channel(color.G))
        + (0.0722D * Channel(color.B));

    private static Double Channel(Byte value)
    {
        Double channel = value / 255D;
        return channel <= 0.04045D
            ? channel / 12.92D
            : Math.Pow((channel + 0.055D) / 1.055D, 2.4D);
    }
}
