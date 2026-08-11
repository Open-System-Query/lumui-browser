namespace Lumui.Cli.Views;

internal sealed class SettingsPage : View
{
    public SettingsPage(String title)
    {
        Title = title;
        CanFocus = true;
        TabStop = TabBehavior.NoStop;
        ContentSizeTracksViewport = false;
        SetContentHeight(25);
        VerticalScrollBar.Visible = true;
        FrameChanged += (_, _) => SetContentWidth(Math.Max(1, Viewport.Width));
    }

    protected override Boolean OnKeyDown(Key key)
    {
        if (key == Key.PageDown)
        {
            ScrollVertical(Math.Max(1, Viewport.Height - 1));
            return true;
        }
        if (key == Key.PageUp)
        {
            ScrollVertical(-Math.Max(1, Viewport.Height - 1));
            return true;
        }
        return base.OnKeyDown(key);
    }
}
