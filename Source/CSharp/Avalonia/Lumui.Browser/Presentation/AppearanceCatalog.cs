namespace Lumui.Browser.Presentation;

public static class AppearanceCatalog
{
    public static readonly AppearanceDefinition Metro = new AppearanceDefinition(
        "metro",
        "LUMUI Metro",
        AppearanceKind.Metro,
        "#F7F7F7",
        "#FFFFFF",
        "#EDEDED",
        "#111111",
        "#5A5A5A",
        "#008F80",
        "#FFFFFF",
        "#B8B8B8",
        "#111111",
        "#FFFFFF",
        "Segoe UI",
        0D,
        0D,
        false);

    public static readonly AppearanceDefinition Aero = new AppearanceDefinition(
        "aero",
        "Aero",
        AppearanceKind.Aero,
        "#DCECF7",
        "#F7FCFF",
        "#CDE8F3",
        "#112A3B",
        "#3C5362",
        "#0B5F96",
        "#FFFFFF",
        "#82ABBA",
        "#102B3A",
        "#F4FCFF",
        "Segoe UI",
        14D,
        11D,
        true);

    public static readonly AppearanceDefinition Material = new AppearanceDefinition(
        "material",
        "Light",
        AppearanceKind.Material,
        "#FFFFFF",
        "#FFFFFF",
        "#F2F2F2",
        "#111111",
        "#595959",
        "#008575",
        "#FFFFFF",
        "#D2D7D6",
        "#111111",
        "#FFFFFF",
        "Segoe UI",
        0D,
        0D,
        false);

    public static readonly AppearanceDefinition Dark = new AppearanceDefinition(
        "dark",
        "LUMUI Metro Dark",
        AppearanceKind.Metro,
        "#000000",
        "#000000",
        "#1C1C1C",
        "#FFFFFF",
        "#C8C8C8",
        "#008F80",
        "#FFFFFF",
        "#626262",
        "#000000",
        "#FFFFFF",
        "Segoe UI",
        0D,
        0D,
        false);

    public static readonly AppearanceDefinition Aqua = new AppearanceDefinition(
        "aqua",
        "Aqua",
        AppearanceKind.Aqua,
        "#EAF5FF",
        "#FFFFFF",
        "#DCEEF8",
        "#142738",
        "#40566A",
        "#075B9E",
        "#FFFFFF",
        "#9EBED5",
        "#16354D",
        "#FFFFFF",
        "Trebuchet MS",
        18D,
        22D,
        true);

    public static readonly AppearanceDefinition Classic = new AppearanceDefinition(
        "classic",
        "Classic",
        AppearanceKind.Classic,
        "#ECE9D8",
        "#ECE9D8",
        "#D6D2BC",
        "#111111",
        "#3E3E3E",
        "#0A3B91",
        "#FFFFFF",
        "#7D7B70",
        "#111111",
        "#FFFFFF",
        "Tahoma",
        2D,
        2D,
        false);

    public static readonly AppearanceDefinition Steampunk = new AppearanceDefinition(
        "steampunk",
        "Steampunk",
        AppearanceKind.Steampunk,
        "#C4A66A",
        "#F7EDCE",
        "#EADBB8",
        "#2B2118",
        "#493625",
        "#6F3C0B",
        "#FFFFFF",
        "#8B6A38",
        "#2D2117",
        "#FFF5D6",
        "Georgia",
        5D,
        4D,
        true);

    public static readonly AppearanceDefinition ScienceFiction = new AppearanceDefinition(
        "scifi",
        "Sci-Fi",
        AppearanceKind.ScienceFiction,
        "#07121C",
        "#0B1A27",
        "#0A2531",
        "#F4FBFF",
        "#B7D0DC",
        "#26D7EB",
        "#061117",
        "#247489",
        "#02070C",
        "#D9FBFF",
        "Cascadia Mono",
        0D,
        0D,
        true);

    public static IReadOnlyList<AppearanceDefinition> All { get; } =
        Array.AsReadOnly(new AppearanceDefinition[]
        {
            Metro,
            Aero,
            Material,
            Aqua,
            Classic,
            Steampunk,
            ScienceFiction
        });

    public static AppearanceDefinition HighContrast(
        AppearanceDefinition source)
    {
        return new AppearanceDefinition(
            source.Id,
            source.Label,
            source.Kind,
            "#000000",
            "#000000",
            "#111111",
            "#FFFFFF",
            "#FFFFFF",
            "#FFF200",
            "#000000",
            "#FFFFFF",
            "#000000",
            "#FFFFFF",
            source.FontFamily,
            source.CornerRadius,
            source.ControlRadius,
            false);
    }

    public static AppearanceDefinition ForBrowser(
        BrowserColorScheme colorScheme,
        FontPreference font,
        String fontFamily,
        ColorVisionMode colorVision,
        Boolean highContrast,
        String accentColor)
    {
        AppearanceDefinition appearance = colorScheme == BrowserColorScheme.Dark
            ? Dark
            : Metro;
        appearance = ColorVisionPalette.WithAccent(appearance, accentColor);
        appearance = ColorVisionPalette.Apply(appearance, colorVision);
        appearance = ColorVisionPalette.WithFont(
            appearance,
            FontPreferenceCatalog.Resolve(font, fontFamily));
        return highContrast
            ? HighContrast(appearance)
            : appearance;
    }
}
