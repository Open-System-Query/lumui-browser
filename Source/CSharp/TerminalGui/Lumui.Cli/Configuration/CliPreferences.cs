namespace Lumui.Cli.Configuration;

public sealed class CliPreferences
{
    public static readonly Uri DefaultHomeAddress = new Uri("https://lumuiopensource.com/", UriKind.Absolute);

    public const String DefaultAccentColor = "#006E63";

    public String HomePage { get; set; } = DefaultHomeAddress.AbsoluteUri;

    public CliStartupMode StartupMode { get; set; } = CliStartupMode.Home;

    public CliNewTabMode NewTabMode { get; set; } = CliNewTabMode.Home;

    public String NewTabPage { get; set; } = DefaultHomeAddress.AbsoluteUri;

    public String DownloadFolder { get; set; } = CliPaths.DefaultDownloadFolder;

    public Boolean AskWhereToSaveDownloads { get; set; } = true;

    public Boolean OfferToSavePasswords { get; set; } = true;

    public Boolean AutoFillPasswords { get; set; } = true;

    public Boolean RememberHistory { get; set; } = true;

    public Boolean ClearBrowsingDataOnExit { get; set; }

    public Boolean SendDoNotTrack { get; set; } = true;

    public Boolean AskBeforeSensitivePermissions { get; set; } = true;

    public Boolean ConfirmClosingMultipleTabs { get; set; } = true;

    public CliColorScheme ColorScheme { get; set; } = CliColorScheme.Light;

    public String AccentColor { get; set; } = DefaultAccentColor;

    public String Font { get; set; } = "Default";

    public String FontFamily { get; set; } = String.Empty;

    public String ColorVision { get; set; } = "Default";

    public Int32 TextScalePercent { get; set; } = 100;

    public Int32 PageZoomPercent { get; set; } = 100;

    public Boolean HighContrast { get; set; }

    public Boolean ReducedMotion { get; set; }

    public Boolean BionicReading { get; set; }

    public Boolean SeniorMode { get; set; }

    public Boolean SimpleReadingView { get; set; }

    public CliTerminalDensity TerminalDensity { get; set; } = CliTerminalDensity.Comfortable;

    public CliTerminalOutput TerminalOutput { get; set; } = CliTerminalOutput.Visual;

    public Boolean ShowOutline { get; set; } = true;

    public Boolean UseUnicode { get; set; } = true;

    public void Normalize()
    {
        HomePage = NormalizeAddress(HomePage, DefaultHomeAddress.AbsoluteUri);
        NewTabPage = NormalizeAddress(NewTabPage, DefaultHomeAddress.AbsoluteUri);
        DownloadFolder = String.IsNullOrWhiteSpace(DownloadFolder)
            ? CliPaths.DefaultDownloadFolder
            : DownloadFolder.Trim();
        if (!Enum.IsDefined(StartupMode))
        {
            StartupMode = CliStartupMode.Home;
        }
        if (!Enum.IsDefined(NewTabMode))
        {
            NewTabMode = CliNewTabMode.Home;
        }
        if (!Enum.IsDefined(ColorScheme))
        {
            ColorScheme = CliColorScheme.Light;
        }
        if (!Enum.IsDefined(TerminalDensity))
        {
            TerminalDensity = CliTerminalDensity.Comfortable;
        }
        if (!Enum.IsDefined(TerminalOutput))
        {
            TerminalOutput = CliTerminalOutput.Visual;
        }
        AccentColor = NormalizeColor(AccentColor);
        TextScalePercent = Math.Clamp(TextScalePercent, 90, 180);
        PageZoomPercent = Math.Clamp(PageZoomPercent, 50, 200);
        Font = String.IsNullOrWhiteSpace(Font) ? "Default" : Font.Trim();
        FontFamily = (FontFamily ?? String.Empty).Trim();
        ColorVision = Enum.TryParse(ColorVision, true, out CliColorVisionMode colorVision)
            && Enum.IsDefined(colorVision)
                ? colorVision.ToString()
                : CliColorVisionMode.Default.ToString();
    }

    public void ResetPresentation()
    {
        ColorScheme = CliColorScheme.Light;
        AccentColor = DefaultAccentColor;
        Font = "Default";
        FontFamily = String.Empty;
        ColorVision = "Default";
        TextScalePercent = 100;
        PageZoomPercent = 100;
        HighContrast = false;
        ReducedMotion = false;
        BionicReading = false;
        SeniorMode = false;
        SimpleReadingView = false;
        TerminalDensity = CliTerminalDensity.Comfortable;
        TerminalOutput = CliTerminalOutput.Visual;
        ShowOutline = true;
        UseUnicode = true;
    }

    private static String NormalizeAddress(String? value, String fallback)
    {
        String text = (value ?? String.Empty).Trim();
        if (text.Length == 0)
        {
            return fallback;
        }
        if (!text.Contains(Uri.SchemeDelimiter, StringComparison.Ordinal))
        {
            text = Uri.UriSchemeHttps + Uri.SchemeDelimiter + text;
        }
        return Uri.TryCreate(text, UriKind.Absolute, out Uri? address)
            && (address.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || address.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            ? address.AbsoluteUri
            : fallback;
    }

    private static String NormalizeColor(String? value)
    {
        String text = (value ?? String.Empty).Trim().ToUpperInvariant();
        return text.Length == 7
            && text[0] == '#'
            && text.Skip(1).All(Uri.IsHexDigit)
            ? text
            : DefaultAccentColor;
    }
}
