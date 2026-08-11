using System.Text.Json;
using LumuiProtocol = Lumui.Client.LumuiProtocol;

namespace Lumui.Browser.Presentation;

public sealed class BrandDefinition
{
    public BrandDefinition(
        String accent,
        String accentSecondary,
        String accentTertiary,
        String highlight,
        String ink,
        String warm,
        String cool,
        BrandMotif motif)
    {
        Accent = accent;
        AccentSecondary = accentSecondary;
        AccentTertiary = accentTertiary;
        Highlight = highlight;
        Ink = ink;
        Warm = warm;
        Cool = cool;
        Motif = motif;
    }

    public String Accent { get; }

    public String AccentSecondary { get; }

    public String AccentTertiary { get; }

    public String Highlight { get; }

    public String Ink { get; }

    public String Warm { get; }

    public String Cool { get; }

    public BrandMotif Motif { get; }

    public static BrandDefinition FromSurface(
        JsonElement surface,
        AppearanceDefinition appearance,
        Boolean highContrast,
        ColorVisionMode colorVision)
    {
        if (highContrast
            || !surface.TryGetProperty(
                LumuiProtocol.Fields.Identity,
                out JsonElement identity)
            || !identity.TryGetProperty(
                LumuiProtocol.Fields.Brand,
                out JsonElement brand)
            || brand.ValueKind != JsonValueKind.Object)
        {
            BrandDefinition fallback = Default(appearance, highContrast);
            return highContrast
                ? fallback
                : ColorVisionPalette.Apply(fallback, colorVision);
        }

        Boolean preserveArtwork = String.Equals(
            appearance.Id,
            "dark",
            StringComparison.Ordinal);
        String accentValue = Text(
            brand,
            LumuiProtocol.Fields.Accent,
            appearance.Accent);
        String accentSecondaryValue = Text(
            brand,
            LumuiProtocol.Fields.AccentSecondary,
            appearance.SurfaceAlternate);
        String accentTertiaryValue = Text(
            brand,
            LumuiProtocol.Fields.AccentTertiary,
            appearance.SurfaceAlternate);
        String highlightValue = Text(
            brand,
            LumuiProtocol.Fields.Highlight,
            appearance.SurfaceAlternate);
        String warmValue = Text(
            brand,
            LumuiProtocol.Fields.Warm,
            appearance.Surface);
        String coolValue = Text(
            brand,
            LumuiProtocol.Fields.Cool,
            appearance.SurfaceAlternate);

        String accent = preserveArtwork
            ? accentValue
            : AccessibleForeground(
                accentValue,
                appearance.Surface,
                appearance.Accent);
        String accentSecondary = preserveArtwork
            ? accentSecondaryValue
            : AccessibleSurface(
                accentSecondaryValue,
                appearance.Text,
                appearance.SurfaceAlternate);
        String accentTertiary = preserveArtwork
            ? accentTertiaryValue
            : AccessibleSurface(
                accentTertiaryValue,
                appearance.Text,
                appearance.SurfaceAlternate);
        String highlight = preserveArtwork
            ? highlightValue
            : AccessibleSurface(
                highlightValue,
                appearance.Text,
                appearance.SurfaceAlternate);
        String warm = preserveArtwork
            ? warmValue
            : AccessibleSurface(
                warmValue,
                appearance.Text,
                appearance.Surface);
        String cool = preserveArtwork
            ? coolValue
            : AccessibleSurface(
                coolValue,
                appearance.Text,
                appearance.SurfaceAlternate);
        return ColorVisionPalette.Apply(new BrandDefinition(
            accent,
            accentSecondary,
            accentTertiary,
            highlight,
            Text(brand, LumuiProtocol.Fields.Ink, appearance.Text),
            warm,
            cool,
            ParseMotif(Text(
                brand,
                LumuiProtocol.Fields.Motif,
                LumuiProtocol.BrandMotifs.None))),
            colorVision);
    }

    private static BrandDefinition Default(
        AppearanceDefinition appearance,
        Boolean highContrast) =>
        new BrandDefinition(
            appearance.Accent,
            appearance.Accent,
            appearance.Accent,
            appearance.SurfaceAlternate,
            appearance.Text,
            appearance.Surface,
            appearance.SurfaceAlternate,
            highContrast ? BrandMotif.None : BrandMotif.Lines);

    private static BrandMotif ParseMotif(String value) =>
        value switch
        {
            LumuiProtocol.BrandMotifs.Orbs => BrandMotif.Orbs,
            LumuiProtocol.BrandMotifs.Lines => BrandMotif.Lines,
            LumuiProtocol.BrandMotifs.Grid => BrandMotif.Grid,
            LumuiProtocol.BrandMotifs.Waves => BrandMotif.Waves,
            _ => BrandMotif.None
        };

    private static String Text(
        JsonElement element,
        String name,
        String fallback)
    {
        return element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? fallback
                : fallback;
    }

    private static String AccessibleForeground(
        String candidate,
        String background,
        String fallback) =>
        ColorContrast.IsReadable(candidate, background)
            ? candidate
            : fallback;

    private static String AccessibleSurface(
        String candidate,
        String foreground,
        String fallback) =>
        ColorContrast.IsReadable(foreground, candidate)
            ? candidate
            : fallback;
}
