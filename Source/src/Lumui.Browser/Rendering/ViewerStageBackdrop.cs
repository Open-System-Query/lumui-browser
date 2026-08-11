using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Lumui.Browser.Presentation;

namespace Lumui.Browser.Rendering;

public sealed class ViewerStageBackdrop : Control
{
    private static readonly DrawingBrush Pattern = new DrawingBrush
    {
        Drawing = new GeometryDrawing
        {
            Brush = BrowserBrushCache.Get("#73928C"),
            Geometry = new EllipseGeometry(new Rect(0D, 0D, 2D, 2D))
        },
        DestinationRect = new RelativeRect(
            0D,
            0D,
            20D,
            20D,
            RelativeUnit.Absolute),
        Stretch = Stretch.None,
        TileMode = TileMode.Tile
    };

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Pattern, Bounds);
    }
}
