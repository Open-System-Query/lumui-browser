using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Input;
using Lumui.Browser.Configuration;
using Lumui.Browser.Rendering;
using Lumui.Browser.Security;
using Lumui.Client;

namespace Lumui.Browser.Views;

public sealed partial class CredentialEditorDialog : Window
{
    private readonly ICredentialVault _credentials;
    private readonly CredentialRecord? _original;
    private Boolean _passwordVisible;

    public CredentialEditorDialog()
        : this(BrowserApplicationServices.Current.Credentials)
    {
    }

    public CredentialEditorDialog(
        ICredentialVault credentials,
        CredentialRecord? original = null)
    {
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _original = original;
        InitializeComponent();
        Boolean editing = original is not null;
        Title = (editing ? "Edit password" : "Add password") + " | Lumi";
        DialogTitleText.Text = editing ? "Edit password" : "Add password";
        DialogDescriptionText.Text = editing
            ? "Update the saved sign-in."
            : "Save a sign-in on this device.";
        if (original is not null)
        {
            OriginBox.Text = original.Origin.AbsoluteUri;
            UserNameBox.Text = original.UserName;
            PasswordBox.Text = original.Password;
        }
        SaveButton.IsEnabled = _credentials.IsAvailable;
        CancelButton.Click += (_, _) => Close(false);
        SaveButton.Click += SaveClicked;
        ShowButton.Click += ShowClicked;
        GenerateButton.Click += GenerateClicked;
        Opened += (_, _) => OriginBox.Focus();
        KeyDown += WindowKeyDown;
    }

    public String? SavedCredentialKey { get; private set; }

    private void ShowClicked(Object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        _passwordVisible = !_passwordVisible;
        PasswordBox.RevealPassword = _passwordVisible;
        ShowButton.Content = _passwordVisible ? "Hide" : "Show";
        ApplyReadingStyle();
    }

    private void GenerateClicked(Object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        String generated = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))
            .Replace('/', '-')
            .Replace('+', '_');
        PasswordBox.Text = generated;
        PasswordBox.Focus();
        PasswordBox.CaretIndex = generated.Length;
    }

    private void SaveClicked(Object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs) =>
        SaveCredential();

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
            SaveCredential();
        }
    }

    private void SaveCredential()
    {
        try
        {
            Uri origin = LumuiClient.NormalizeAddress(OriginBox.Text ?? String.Empty);
            String userName = (UserNameBox.Text ?? String.Empty).Trim();
            String password = PasswordBox.Text ?? String.Empty;
            if (userName.Length == 0 || password.Length == 0)
            {
                StatusText.Text = "Enter a username and password.";
                ApplyReadingStyle();
                return;
            }
            _credentials.Save(origin, userName, password);
            if (_original is not null
                && (_original.Origin != origin
                    || !String.Equals(
                        _original.UserName,
                        userName,
                        StringComparison.Ordinal)))
            {
                _credentials.Remove(_original.Origin, _original.UserName);
            }
            SavedCredentialKey = origin.AbsoluteUri + "\n" + userName;
            PasswordBox.Text = String.Empty;
            Close(true);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or CryptographicException
                or LumuiProtocolException
                or UriFormatException)
        {
            StatusText.Text = exception.Message;
            ApplyReadingStyle();
        }
    }

    private void ApplyReadingStyle() => ReadingTextFormatter.ApplyTree(
        this,
        Classes.Contains("bionic"));
}
