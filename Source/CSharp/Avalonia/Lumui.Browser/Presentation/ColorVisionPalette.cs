namespace Lumui.Browser.Presentation;

public static class ColorVisionPalette
{
    public static AppearanceDefinition Apply(
        AppearanceDefinition source,
        ColorVisionMode mode)
    {
        Boolean dark = source.Id == "dark";
        (String accent, String border) = mode switch
        {
            ColorVisionMode.Deuteranopia =>
                (dark ? "#72A7FF" : "#005FCC", "#6D8299"),
            ColorVisionMode.Protanopia =>
                (dark ? "#72A7FF" : "#005FCC", "#667F96"),
            ColorVisionMode.Tritanopia =>
                (dark ? "#F08ABC" : "#A63D75", "#806B78"),
            _ => (source.Accent, source.Border)
        };
        if (mode == ColorVisionMode.Default)
        {
            return source;
        }

        return Copy(
            source,
            accent: accent,
            border: border);
    }

    public static BrandDefinition Apply(
        BrandDefinition source,
        ColorVisionMode mode)
    {
        return mode switch
        {
            ColorVisionMode.Deuteranopia => new BrandDefinition(
                "#005FCC",
                "#F0A202",
                "#007C91",
                "#FFD166",
                source.Ink,
                source.Warm,
                source.Cool,
                source.Motif),
            ColorVisionMode.Protanopia => new BrandDefinition(
                "#005FCC",
                "#A67C00",
                "#007C91",
                "#E9C46A",
                source.Ink,
                source.Warm,
                source.Cool,
                source.Motif),
            ColorVisionMode.Tritanopia => new BrandDefinition(
                "#A63D75",
                "#006B63",
                "#7A5195",
                "#E07A5F",
                source.Ink,
                source.Warm,
                source.Cool,
                source.Motif),
            _ => source
        };
    }

    public static AppearanceDefinition WithFont(
        AppearanceDefinition source,
        String fontFamily) =>
        Copy(source, fontFamily: fontFamily);

    public static AppearanceDefinition WithAccent(
        AppearanceDefinition source,
        String accent) =>
        Copy(source, accent: accent);

    private static AppearanceDefinition Copy(
        AppearanceDefinition source,
        String? accent = null,
        String? surfaceAlternate = null,
        String? border = null,
        String? fontFamily = null) =>
        new AppearanceDefinition(
            source.Id,
            source.Label,
            source.Kind,
            source.Background,
            source.Surface,
            surfaceAlternate ?? source.SurfaceAlternate,
            source.Text,
            source.Muted,
            accent ?? source.Accent,
            source.AccentText,
            border ?? source.Border,
            source.CodeBackground,
            source.CodeText,
            fontFamily ?? source.FontFamily,
            source.CornerRadius,
            source.ControlRadius,
            source.Raised);
}
