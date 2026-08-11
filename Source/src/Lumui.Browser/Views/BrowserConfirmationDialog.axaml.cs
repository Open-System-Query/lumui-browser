using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Lumui.Browser.Presentation;

namespace Lumui.Browser.Views;

public sealed partial class BrowserConfirmationDialog : Window
{
    public BrowserConfirmationDialog()
        : this("Confirm", String.Empty, "Continue", false)
    {
    }

    public BrowserConfirmationDialog(
        String title,
        String message,
        String confirmLabel,
        Boolean dangerous)
    {
        InitializeComponent();
        Title = title + " | Lumi";
        MessageText.Text = message;
        ConfirmButton.Content = confirmLabel;
        if (dangerous)
        {
            ConfirmButton.Classes.Remove("primary-action");
            ConfirmButton.Classes.Add("danger-action");
        }
        AutomationProperties.SetName(this, title + ". " + message);
        CancelButton.Click += (_, _) => Close(false);
        ConfirmButton.Click += (_, _) => Close(true);
        KeyDown += WindowKeyDown;
    }

    public static async Task<Boolean> ShowAsync(
        Window owner,
        String title,
        String message,
        String confirmLabel,
        Boolean dangerous = false)
    {
        BrowserConfirmationDialog dialog = new BrowserConfirmationDialog(
            title,
            message,
            confirmLabel,
            dangerous);
        BrowserWindowAppearance.Inherit(owner, dialog);
        return await dialog.ShowDialog<Boolean>(owner);
    }

    private void WindowKeyDown(Object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape)
        {
            eventArgs.Handled = true;
            Close(false);
        }
        else if (eventArgs.Key == Key.Enter)
        {
            eventArgs.Handled = true;
            Close(true);
        }
    }
}
