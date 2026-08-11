namespace Lumui.Cli.Configuration;

public static class CliTheme
{
    public static void Apply(CliPreferences preferences)
    {
        preferences.Normalize();
        if (!TerminalColor.TryParse(
                preferences.AccentColor,
                CultureInfo.InvariantCulture,
                out TerminalColor accent))
        {
            accent = new TerminalColor(CliPreferences.DefaultAccentColor);
        }
        if (Enum.TryParse(preferences.ColorVision, true, out CliColorVisionMode colorVision))
        {
            accent = colorVision switch
            {
                CliColorVisionMode.Deuteranopia => new TerminalColor("#0072B2"),
                CliColorVisionMode.Protanopia => new TerminalColor("#0072B2"),
                CliColorVisionMode.Tritanopia => new TerminalColor("#B23A48"),
                CliColorVisionMode.Monochrome => new TerminalColor("#606060"),
                _ => accent
            };
        }
        Boolean dark = preferences.ColorScheme == CliColorScheme.Dark;
        TerminalColor foreground = dark ? new TerminalColor("#F4FAF8") : new TerminalColor("#111816");
        TerminalColor background = dark ? new TerminalColor("#07110F") : new TerminalColor("#F5F8F7");
        if (preferences.HighContrast)
        {
            foreground = dark ? TerminalColor.White : TerminalColor.Black;
            background = dark ? TerminalColor.Black : TerminalColor.White;
        }
        TerminalColor accentForeground = accent.IsDarkColor() ? TerminalColor.White : TerminalColor.Black;
        TerminalColor muted = dark ? new TerminalColor("#9DB6AF") : new TerminalColor("#52645F");
        TerminalColor codeBackground = dark ? new TerminalColor("#0B211C") : new TerminalColor("#E5EEEB");
        TerminalColor chromeBackground = dark ? new TerminalColor("#102A24") : new TerminalColor("#DCEBE7");

        TerminalAttribute normal = new TerminalAttribute(foreground, background);
        TerminalAttribute focused = new TerminalAttribute(accentForeground, accent);
        TerminalAttribute disabled = new TerminalAttribute(muted, background);
        TerminalAttribute editable = new TerminalAttribute(foreground, codeBackground);
        TerminalAttribute readOnly = new TerminalAttribute(muted, codeBackground);
        TerminalScheme baseScheme = CreateScheme(normal, focused, disabled, editable, readOnly);
        SchemeManager.AddScheme("Base", baseScheme);
        SchemeManager.AddScheme("Dialog", baseScheme);
        SchemeManager.AddScheme(
            "Menu",
            CreateScheme(
                new TerminalAttribute(foreground, chromeBackground),
                focused,
                new TerminalAttribute(muted, chromeBackground),
                new TerminalAttribute(foreground, chromeBackground),
                new TerminalAttribute(muted, chromeBackground)));
        SchemeManager.AddScheme(
            "Muted",
            CreateScheme(disabled, focused, disabled, editable, readOnly));
        SchemeManager.AddScheme(
            "Code",
            CreateScheme(editable, focused, readOnly, editable, readOnly));
        SchemeManager.AddScheme(
            "Accent",
            CreateScheme(
                focused,
                new TerminalAttribute(foreground, background),
                new TerminalAttribute(muted, accent),
                focused,
                new TerminalAttribute(muted, accent)));
        SchemeManager.AddScheme(
            "Success",
            SolidScheme(new TerminalAttribute(TerminalColor.White, new TerminalColor("#287A4B")), focused));
        SchemeManager.AddScheme(
            "Error",
            SolidScheme(new TerminalAttribute(TerminalColor.White, new TerminalColor("#A3212B")), focused));
    }

    private static TerminalScheme CreateScheme(
        TerminalAttribute normal,
        TerminalAttribute focus,
        TerminalAttribute disabled,
        TerminalAttribute editable,
        TerminalAttribute readOnly) => new TerminalScheme(normal)
    {
        Normal = normal,
        Focus = focus,
        HotNormal = normal,
        HotFocus = focus,
        Active = focus,
        HotActive = focus,
        Highlight = focus,
        Editable = editable,
        ReadOnly = readOnly,
        Disabled = disabled
    };

    private static TerminalScheme SolidScheme(TerminalAttribute normal, TerminalAttribute focus) =>
        CreateScheme(normal, focus, normal, normal, normal);
}
