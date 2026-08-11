namespace Lumui.Browser.Presentation;

public sealed class AppearanceDefinition
{
    public AppearanceDefinition(
        String id,
        String label,
        AppearanceKind kind,
        String background,
        String surface,
        String surfaceAlternate,
        String text,
        String muted,
        String accent,
        String accentText,
        String border,
        String codeBackground,
        String codeText,
        String fontFamily,
        Double cornerRadius,
        Double controlRadius,
        Boolean raised)
    {
        Id = id;
        Label = label;
        Kind = kind;
        Background = background;
        Surface = surface;
        SurfaceAlternate = surfaceAlternate;
        Text = text;
        Muted = muted;
        Accent = accent;
        AccentText = accentText;
        Border = border;
        CodeBackground = codeBackground;
        CodeText = codeText;
        FontFamily = fontFamily;
        CornerRadius = cornerRadius;
        ControlRadius = controlRadius;
        Raised = raised;
    }

    public String Id { get; }

    public String Label { get; }

    public AppearanceKind Kind { get; }

    public String Background { get; }

    public String Surface { get; }

    public String SurfaceAlternate { get; }

    public String Text { get; }

    public String Muted { get; }

    public String Accent { get; }

    public String AccentText { get; }

    public String Border { get; }

    public String CodeBackground { get; }

    public String CodeText { get; }

    public String FontFamily { get; }

    public Double CornerRadius { get; }

    public Double ControlRadius { get; }

    public Boolean Raised { get; }

    public override String ToString() => Label;
}
