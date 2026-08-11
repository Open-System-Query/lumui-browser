using Lumui.Browser.Presentation;

namespace Lumui.Browser.Configuration;

public sealed class BrowserPreferences
{
    public const String DefaultAccentColor = "#008F80";

    public String HomePage { get; set; } =
        BrowserDefaults.HomeAddress.AbsoluteUri;

    public BrowserStartupMode StartupMode { get; set; } =
        BrowserStartupMode.Home;

    public BrowserNewTabMode NewTabMode { get; set; } =
        BrowserNewTabMode.Home;

    public String NewTabPage { get; set; } =
        BrowserDefaults.HomeAddress.AbsoluteUri;

    public String DownloadFolder { get; set; } =
        BrowserPaths.DefaultDownloadFolder;

    public Boolean AskWhereToSaveDownloads { get; set; } = true;

    public Boolean OfferToSavePasswords { get; set; } = true;

    public Boolean AutoFillPasswords { get; set; } = true;

    public Boolean RememberHistory { get; set; } = true;

    public Boolean ClearBrowsingDataOnExit { get; set; }

    public Boolean SendDoNotTrack { get; set; } = true;

    public Boolean AskBeforeSensitivePermissions { get; set; } = true;

    public Boolean ConfirmClosingMultipleTabs { get; set; } = true;

    public BrowserColorScheme ColorScheme { get; set; } =
        BrowserColorScheme.Light;

    public String AccentColor { get; set; } = DefaultAccentColor;

    public FontPreference Font { get; set; } = FontPreference.Default;

    public String FontFamily { get; set; } = String.Empty;

    public ColorVisionMode ColorVision { get; set; } =
        ColorVisionMode.Default;

    public Int32 TextScalePercent { get; set; } = 100;

    public Int32 PageZoomPercent { get; set; } = 100;

    public Boolean HighContrast { get; set; }

    public Boolean ReducedMotion { get; set; }

    public Boolean BionicReading { get; set; }

    public Boolean SeniorMode { get; set; }

    public Boolean SimpleReadingView { get; set; }

    public Double TextScale => TextScalePercent / 100D;

    public Double PageScale => PageZoomPercent / 100D;

    public void Normalize()
    {
        HomePage = NormalizeAddress(HomePage, BrowserDefaults.HomeAddress.AbsoluteUri);
        NewTabPage = NormalizeAddress(NewTabPage, BrowserDefaults.HomeAddress.AbsoluteUri);
        DownloadFolder = String.IsNullOrWhiteSpace(DownloadFolder)
            ? BrowserPaths.DefaultDownloadFolder
            : DownloadFolder.Trim();
        if (!Enum.IsDefined(StartupMode))
        {
            StartupMode = BrowserStartupMode.Home;
        }
        if (!Enum.IsDefined(NewTabMode))
        {
            NewTabMode = BrowserNewTabMode.Home;
        }
        TextScalePercent = Math.Clamp(TextScalePercent, 90, 180);
        PageZoomPercent = PageZoomCatalog.Normalize(PageZoomPercent);
        if (!Enum.IsDefined(ColorScheme))
        {
            ColorScheme = BrowserColorScheme.Light;
        }
        AccentColor = NormalizeColor(AccentColor);
        if (!Enum.IsDefined(Font))
        {
            Font = FontPreference.Default;
        }
        FontFamily = (FontFamily ?? String.Empty).Trim();
        if (!Enum.IsDefined(ColorVision))
        {
            ColorVision = ColorVisionMode.Default;
        }
    }

    public void Reset()
    {
        ColorScheme = BrowserColorScheme.Light;
        AccentColor = DefaultAccentColor;
        Font = FontPreference.Default;
        FontFamily = String.Empty;
        ColorVision = ColorVisionMode.Default;
        TextScalePercent = 100;
        HighContrast = false;
        ReducedMotion = false;
        BionicReading = false;
        SeniorMode = false;
        SimpleReadingView = false;
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
            && (address.Scheme.Equals(
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase)
                || address.Scheme.Equals(
                    Uri.UriSchemeHttp,
                    StringComparison.OrdinalIgnoreCase))
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
