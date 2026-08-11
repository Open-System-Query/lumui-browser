namespace Lumui.Cli.Rendering;

internal sealed class StackBuilder
{
    private Int32 _height;

    public StackBuilder(Int32 width)
    {
        Width = width;
        View = new View
        {
            Width = width,
            CanFocus = true,
            TabStop = TabBehavior.NoStop
        };
    }

    public View View { get; }

    public Int32 Width { get; }

    public Int32 Height => _height;

    public void Add(View child, Int32 height)
    {
        if (height <= 0 || !child.Visible)
        {
            child.Dispose();
            return;
        }
        Int32 resolvedHeight = height;
        child.Y = _height;
        child.Height = resolvedHeight;
        View.Add(child);
        _height += resolvedHeight;
        View.Height = Math.Max(1, _height);
    }

    public void Space()
    {
        if (_height > 0)
        {
            _height++;
            View.Height = _height;
        }
    }
}
