using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Lumui.Browser.Presentation;

public static class BrowserBrushCache
{
    private static readonly ConcurrentDictionary<String, IBrush> Brushes =
        new ConcurrentDictionary<String, IBrush>(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<String, IBrush> Gradients =
        new ConcurrentDictionary<String, IBrush>(StringComparer.OrdinalIgnoreCase);

    public static IBrush Get(String value) => Brushes.GetOrAdd(
        value,
        static color => new ImmutableSolidColorBrush(Color.Parse(color)));

    public static IBrush Gradient(String start, String end)
    {
        String key = start + "\0" + end;
        return Gradients.GetOrAdd(
            key,
            _ => new ImmutableLinearGradientBrush(
                new ImmutableGradientStop[]
                {
                    new ImmutableGradientStop(0D, Color.Parse(start)),
                    new ImmutableGradientStop(1D, Color.Parse(end))
                },
                startPoint: RelativePoint.TopLeft,
                endPoint: RelativePoint.BottomRight));
    }
}
