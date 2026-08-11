namespace Lumui.Browser.Presentation;

public sealed class RendererSettings
{
    public RendererSettings(
        DeviceProfileDefinition profile,
        AppearanceDefinition appearance,
        OutputModeDefinition output,
        InteractionModeDefinition interaction,
        Double textScale,
        Double pageScale,
        Boolean highContrast,
        Boolean reducedMotion,
        Boolean bionicReading,
        ColorVisionMode colorVision)
    {
        Profile = profile;
        Appearance = appearance;
        Output = output;
        Interaction = interaction;
        TextScale = Math.Clamp(textScale, 0.9D, 1.8D);
        PageScale = Math.Clamp(pageScale, 0.25D, 5D);
        HighContrast = highContrast;
        ReducedMotion = reducedMotion;
        BionicReading = bionicReading;
        ColorVision = colorVision;
    }

    public DeviceProfileDefinition Profile { get; }

    public AppearanceDefinition Appearance { get; }

    public OutputModeDefinition Output { get; }

    public InteractionModeDefinition Interaction { get; }

    public Double TextScale { get; }

    public Double PageScale { get; }

    public Boolean HighContrast { get; }

    public Boolean ReducedMotion { get; }

    public Boolean BionicReading { get; }

    public ColorVisionMode ColorVision { get; }

    public String AccessibilitySummary
    {
        get
        {
            List<String> values = new List<String>();
            if (TextScale > 1.01D)
            {
                values.Add($"Text {TextScale:P0}");
            }
            if (Math.Abs(PageScale - 1D) > 0.01D)
            {
                values.Add($"Page {PageScale:P0}");
            }
            if (HighContrast)
            {
                values.Add("High contrast");
            }
            if (ReducedMotion)
            {
                values.Add("Reduced motion");
            }
            if (BionicReading)
            {
                values.Add("Bionic reading");
            }
            if (ColorVision != ColorVisionMode.Default)
            {
                values.Add(ColorVision.ToString());
            }
            return values.Count == 0
                ? "Default"
                : String.Join(", ", values);
        }
    }
}
