using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Lumui.Browser.Rendering;

public sealed class SemanticSvgDocument
{
    private readonly DrawingImage _image;

    public SemanticSvgDocument(
        Double width,
        Double height,
        Drawing drawing)
    {
        Width = width;
        Height = height;
        _image = new DrawingImage(drawing)
        {
            Viewbox = new Rect(0D, 0D, width, height)
        };
    }

    public Double Width { get; }

    public Double Height { get; }

    public Control CreateView()
    {
        return new Image
        {
            Source = _image,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false
        };
    }
}
