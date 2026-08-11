using LumuiProtocol = Lumui.Client.LumuiProtocol;

namespace Lumui.Browser.Presentation;

public static class DeviceProfileCatalog
{
    public static readonly DeviceProfileDefinition Web = new DeviceProfileDefinition(
        LumuiProtocol.RenderProfiles.WebResponsiveDefault,
        "Web",
        DeviceProfileKind.Web,
        1280D,
        820D,
        2D,
        14D,
        44D,
        1160D);

    public static readonly DeviceProfileDefinition Desktop = new DeviceProfileDefinition(
        LumuiProtocol.RenderProfiles.DesktopLandscapeDefault,
        "Desktop",
        DeviceProfileKind.Desktop,
        1920D,
        1080D,
        10D,
        20D,
        42D,
        1800D);

    public static readonly DeviceProfileDefinition Tablet = new DeviceProfileDefinition(
        LumuiProtocol.RenderProfiles.TabletLandscapeDefault,
        "Tablet",
        DeviceProfileKind.Tablet,
        1024D,
        768D,
        18D,
        32D,
        30D,
        760D);

    public static readonly DeviceProfileDefinition Phone = new DeviceProfileDefinition(
        LumuiProtocol.RenderProfiles.SmartphonePortraitDefault,
        "Phone",
        DeviceProfileKind.Phone,
        430D,
        820D,
        14D,
        44D,
        30D,
        400D);

    public static readonly DeviceProfileDefinition Watch = new DeviceProfileDefinition(
        LumuiProtocol.RenderProfiles.SmartwatchSquareDefault,
        "Watch",
        DeviceProfileKind.Watch,
        390D,
        390D,
        18D,
        110D,
        24D,
        342D);

    public static readonly DeviceProfileDefinition Kiosk = new DeviceProfileDefinition(
        LumuiProtocol.RenderProfiles.KioskLandscapePublic,
        "Kiosk",
        DeviceProfileKind.Kiosk,
        1280D,
        720D,
        16D,
        8D,
        26D,
        1160D);

    public static readonly DeviceProfileDefinition Appliance = new DeviceProfileDefinition(
        LumuiProtocol.RenderProfiles.ApplianceLandscapeShared,
        "Appliance",
        DeviceProfileKind.Appliance,
        900D,
        580D,
        22D,
        18D,
        34D,
        820D);

    public static IReadOnlyList<DeviceProfileDefinition> All { get; } =
        Array.AsReadOnly(new DeviceProfileDefinition[]
        {
            Web,
            Desktop,
            Tablet,
            Phone,
            Watch,
            Kiosk,
            Appliance
        });
}
