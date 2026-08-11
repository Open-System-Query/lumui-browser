namespace Lumui.Cli.Rendering;

public sealed class TerminalPixelFrame
{
    public TerminalPixelFrame(Int32 width, Int32 height, Byte[] rgb)
    {
        Width = width;
        Height = height;
        Rgb = rgb;
    }

    public Int32 Width { get; }

    public Int32 Height { get; }

    public Byte[] Rgb { get; }
}
