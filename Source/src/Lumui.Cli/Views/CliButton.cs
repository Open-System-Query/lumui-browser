namespace Lumui.Cli.Views;

internal class CliButton : Button
{
    public CliButton()
    {
        ShadowStyle = Terminal.Gui.ViewBase.ShadowStyles.None;
        CanFocus = true;
        TabStop = TabBehavior.TabStop;
    }
}
