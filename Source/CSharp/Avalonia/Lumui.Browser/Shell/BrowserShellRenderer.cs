using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Lumui.Browser.Shell;

public sealed class BrowserShellRenderer
{
    private readonly BrowserShellSurface _surface;

    public BrowserShellRenderer(BrowserShellSurface surface)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
    }

    public void ApplyButton(Button button, String componentId, Boolean showLabel)
    {
        String label = _surface.Text(componentId);
        String help = _surface.Help(componentId);
        if (showLabel)
        {
            button.Content = label;
        }
        AutomationProperties.SetName(button, label);
        if (help.Length > 0)
        {
            AutomationProperties.SetHelpText(button, help);
            ToolTip.SetTip(button, help);
        }
    }

    public void ApplyText(TextBlock text, String componentId)
    {
        String value = _surface.Text(componentId);
        text.Text = value;
        AutomationProperties.SetName(text, value);
    }

    public void ApplyControl(Control control, String componentId)
    {
        String label = _surface.Text(componentId);
        String help = _surface.Help(componentId);
        AutomationProperties.SetName(control, label);
        if (help.Length > 0)
        {
            AutomationProperties.SetHelpText(control, help);
            ToolTip.SetTip(control, help);
        }
    }

    public void ApplyToggleButton(
        ToggleButton button,
        String componentId,
        Boolean showLabel)
    {
        String label = _surface.Text(componentId);
        String help = _surface.Help(componentId);
        if (showLabel)
        {
            button.Content = label;
        }
        AutomationProperties.SetName(button, label);
        if (help.Length > 0)
        {
            AutomationProperties.SetHelpText(button, help);
            ToolTip.SetTip(button, help);
        }
    }
}
