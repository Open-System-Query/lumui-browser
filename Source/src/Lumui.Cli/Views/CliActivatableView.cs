namespace Lumui.Cli.Views;

internal sealed class CliActivatableFrame : FrameView
{
    private readonly Action _activate;

    public CliActivatableFrame(Action activate)
    {
        _activate = activate;
        AddCommand(Command.Accept, Activate);
        KeyBindings.ReplaceCommands(Key.Enter, Command.Accept);
        KeyBindings.ReplaceCommands(Key.Space, Command.Accept);
        MouseBindings.Add(MouseFlags.LeftButtonClicked, Command.Accept);
    }

    private Boolean? Activate()
    {
        _activate();
        return true;
    }
}

internal sealed class CliActivatableLabel : Label
{
    private readonly Action _activate;

    public CliActivatableLabel(Action activate)
    {
        _activate = activate;
        AddCommand(Command.Accept, Activate);
        KeyBindings.ReplaceCommands(Key.Enter, Command.Accept);
        KeyBindings.ReplaceCommands(Key.Space, Command.Accept);
        MouseBindings.Add(MouseFlags.LeftButtonClicked, Command.Accept);
    }

    private Boolean? Activate()
    {
        _activate();
        return true;
    }
}
