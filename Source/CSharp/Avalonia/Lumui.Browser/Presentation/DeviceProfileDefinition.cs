namespace Lumui.Browser.Presentation;

public sealed class DeviceProfileDefinition
{
    public DeviceProfileDefinition(
        String id,
        String label,
        DeviceProfileKind kind,
        Double frameWidth,
        Double frameHeight,
        Double frameBorder,
        Double frameRadius,
        Double chromeHeight,
        Double contentWidth,
        String group = "Common",
        String description = "",
        String? shape = null)
    {
        Id = id;
        Label = label;
        Kind = kind;
        FrameWidth = frameWidth;
        FrameHeight = frameHeight;
        FrameBorder = frameBorder;
        FrameRadius = frameRadius;
        ChromeHeight = chromeHeight;
        ContentWidth = contentWidth;
        Group = group;
        Description = description;
        Shape = shape ?? kind switch
        {
            DeviceProfileKind.Web => "web",
            DeviceProfileKind.Desktop => "desktop",
            DeviceProfileKind.Tablet => "tablet",
            DeviceProfileKind.Phone => "phone",
            DeviceProfileKind.Watch => "watch",
            DeviceProfileKind.Kiosk => "kiosk",
            DeviceProfileKind.Appliance => "appliance",
            _ => "web"
        };
    }

    public String Id { get; }

    public String Label { get; }

    public DeviceProfileKind Kind { get; }

    public Double FrameWidth { get; }

    public Double FrameHeight { get; }

    public Double FrameBorder { get; }

    public Double FrameRadius { get; }

    public Double ChromeHeight { get; }

    public Double ContentWidth { get; }

    public String Group { get; }

    public String Description { get; }

    public String Shape { get; }

    public override String ToString() => Label;
}
