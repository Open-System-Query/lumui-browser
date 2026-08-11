using Avalonia.Automation;
using Avalonia.Controls;
using Lumui.Browser.Commands;
using Lumui.Browser.Shell;

namespace Lumui.Browser.Views;

public sealed partial class BrowserMenuPanel : UserControl
{
    private readonly BrowserShellRenderer? _renderer;

    public BrowserMenuPanel()
        : this(null)
    {
    }

    public BrowserMenuPanel(BrowserShellRenderer? renderer)
    {
        _renderer = renderer;
        InitializeComponent();
        ApplyShellText();
        Bind(NewTabButton, BrowserCommand.NewTab);
        Bind(NewWindowButton, BrowserCommand.NewWindow);
        Bind(NewPrivateWindowButton, BrowserCommand.NewPrivateWindow);
        Bind(ZoomOutButton, BrowserCommand.ZoomOut);
        Bind(ZoomResetButton, BrowserCommand.ZoomReset);
        Bind(ZoomInButton, BrowserCommand.ZoomIn);
        Bind(FullScreenButton, BrowserCommand.FullScreen);
        Bind(BookmarksButton, BrowserCommand.Bookmarks);
        Bind(HistoryButton, BrowserCommand.History);
        Bind(DownloadsButton, BrowserCommand.Downloads);
        Bind(PasswordsButton, BrowserCommand.Passwords);
        Bind(SettingsButton, BrowserCommand.Settings);
        Bind(ToolsButton, BrowserCommand.DeveloperTools);
    }

    public event Action<BrowserCommand>? CommandRequested;

    public void SetZoom(Int32 percent)
    {
        ZoomValueText.Text = percent + "%";
        AutomationProperties.SetName(
            ZoomResetButton,
            percent == 100
                ? "Page zoom 100 percent"
                : $"Page zoom {percent} percent. Reset to 100 percent");
    }

    public void SetDarkMode(Boolean dark, Boolean highContrast)
    {
        SetClass(MenuRoot, "dark", dark && !highContrast);
        SetClass(MenuRoot, "high-contrast", highContrast);
    }

    private void ApplyShellText()
    {
        if (_renderer is null)
        {
            NewTabText.Text = "New tab";
            NewWindowText.Text = "New window";
            NewPrivateWindowText.Text = "New private window";
            FullScreenText.Text = "Full screen";
            BookmarksText.Text = "Bookmarks";
            HistoryText.Text = "History";
            DownloadsText.Text = "Downloads";
            PasswordsText.Text = "Passwords";
            SettingsText.Text = "Settings";
            ToolsText.Text = "Developer tools";
            return;
        }
        _renderer.ApplyText(NewTabText, "menu.newTab");
        _renderer.ApplyText(NewWindowText, "menu.newWindow");
        _renderer.ApplyText(NewPrivateWindowText, "menu.newPrivateWindow");
        _renderer.ApplyText(FullScreenText, "menu.fullScreen");
        _renderer.ApplyText(BookmarksText, "menu.bookmarks");
        _renderer.ApplyText(HistoryText, "menu.history");
        _renderer.ApplyText(DownloadsText, "menu.downloads");
        _renderer.ApplyText(PasswordsText, "menu.passwords");
        _renderer.ApplyText(SettingsText, "menu.settings");
        _renderer.ApplyText(ToolsText, "menu.tools");
    }

    private void Bind(Button button, BrowserCommand command)
    {
        button.Click += (_, _) => CommandRequested?.Invoke(command);
    }

    private static void SetClass(Control control, String name, Boolean enabled)
    {
        if (enabled && !control.Classes.Contains(name))
        {
            control.Classes.Add(name);
        }
        else if (!enabled)
        {
            control.Classes.Remove(name);
        }
    }
}
