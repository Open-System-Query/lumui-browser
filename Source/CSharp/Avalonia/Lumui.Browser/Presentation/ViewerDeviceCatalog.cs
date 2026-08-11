namespace Lumui.Browser.Presentation;

public static class ViewerDeviceCatalog
{
    public static IReadOnlyList<DeviceProfileDefinition> All { get; } =
        Array.AsReadOnly(new DeviceProfileDefinition[]
        {
            Profile("responsive", "Browser", "Common", 1180D, 760D, "web", "Flexible window", DeviceProfileKind.Web),
            Profile("desktop", "Desktop", "Common", 1920D, 1080D, "desktop", "Large screen", DeviceProfileKind.Desktop),
            Profile("laptop", "Laptop", "Common", 1440D, 900D, "laptop", "Portable computer", DeviceProfileKind.Desktop),
            Profile("tablet", "Tablet", "Common", 768D, 1024D, "tablet", "Touch screen", DeviceProfileKind.Tablet),
            Profile("phone", "Phone", "Common", 390D, 844D, "phone", "Small touch screen", DeviceProfileKind.Phone),
            Profile("foldable", "Foldable", "Common", 720D, 900D, "foldable", "Folding phone", DeviceProfileKind.Tablet),
            Profile("watch", "Watch", "Wearable", 320D, 320D, "watch", "Small square screen", DeviceProfileKind.Watch),
            Profile("round-watch", "Round watch", "Wearable", 360D, 360D, "watch-round", "Small round screen", DeviceProfileKind.Watch),
            Profile("band", "Band", "Wearable", 192D, 490D, "band", "Narrow wrist screen", DeviceProfileKind.Watch),
            Profile("head-up", "Head-up", "Wearable", 640D, 240D, "hud", "Hands-free view", DeviceProfileKind.Watch),
            Profile("kiosk", "Kiosk", "Public", 1280D, 720D, "kiosk", "Public touch screen", DeviceProfileKind.Kiosk),
            Profile("checkout", "Checkout", "Public", 1024D, 768D, "pos", "Counter screen", DeviceProfileKind.Kiosk),
            Profile("atm", "ATM", "Public", 800D, 600D, "atm", "Secure task screen", DeviceProfileKind.Kiosk),
            Profile("sign", "Sign", "Public", 1920D, 1080D, "signage", "View from a distance", DeviceProfileKind.Desktop),
            Profile("tv", "TV", "Public", 1920D, 1080D, "tv", "Living-room screen", DeviceProfileKind.Desktop),
            Profile("car", "Car", "Public", 1280D, 480D, "vehicle", "Wide dashboard screen", DeviceProfileKind.Kiosk),
            Profile("fridge", "Fridge", "Home", 800D, 1280D, "fridge", "Tall shared screen", DeviceProfileKind.Appliance),
            Profile("oven", "Oven", "Home", 800D, 360D, "appliance", "Wide control screen", DeviceProfileKind.Appliance),
            Profile("washer", "Washer", "Home", 480D, 480D, "appliance-round", "Round control", DeviceProfileKind.Appliance),
            Profile("thermostat", "Thermostat", "Home", 480D, 480D, "thermostat", "Wall control", DeviceProfileKind.Appliance),
            Profile("home-panel", "Home panel", "Home", 1024D, 600D, "appliance", "Shared home control", DeviceProfileKind.Appliance),
            Profile("handheld", "Handheld", "Work", 480D, 800D, "rugged", "Tough mobile screen", DeviceProfileKind.Phone),
            Profile("scanner", "Scanner", "Work", 360D, 640D, "scanner", "Scan-first screen", DeviceProfileKind.Phone),
            Profile("control-panel", "Control panel", "Work", 1280D, 800D, "industrial", "Fixed work screen", DeviceProfileKind.Kiosk),
            Profile("medical", "Medical", "Work", 1280D, 1024D, "medical", "Clinical screen", DeviceProfileKind.Kiosk),
            Profile("printer", "Printer", "Print", 480D, 272D, "printer", "Built-in screen", DeviceProfileKind.Appliance),
            Profile("copier", "Copier", "Print", 1024D, 600D, "printer", "Large printer screen", DeviceProfileKind.Kiosk),
            Profile("receipt", "Receipt", "Print", 320D, 720D, "receipt", "Narrow print", DeviceProfileKind.Phone),
            Profile("paper", "Paper", "Print", 794D, 1123D, "paper", "Printed page", DeviceProfileKind.Desktop),
            Profile("badge", "Badge", "Print", 420D, 264D, "badge", "Small printed pass", DeviceProfileKind.Appliance),
            Profile("e-paper", "E-paper", "Other", 800D, 600D, "eink", "Low-power screen", DeviceProfileKind.Tablet),
            Profile("monochrome", "Monochrome", "Other", 720D, 400D, "mono", "Screen without color", DeviceProfileKind.Tablet),
            Profile("voice", "Voice", "Other", 560D, 640D, "voice", "Spoken steps", DeviceProfileKind.Appliance),
            Profile("screen-reader", "Screen reader", "Other", 720D, 900D, "transcript", "Reading order", DeviceProfileKind.Desktop)
        });

    public static IReadOnlyList<DeviceProfileDefinition> Quick { get; } =
        Array.AsReadOnly(new DeviceProfileDefinition[]
        {
            All[0],
            All[1],
            All[3],
            All[4],
            All[6],
            All[10]
        });

    private static DeviceProfileDefinition Profile(
        String id,
        String label,
        String group,
        Double width,
        Double height,
        String shape,
        String description,
        DeviceProfileKind kind)
    {
        Double border = shape switch
        {
            "web" => 7D,
            "desktop" or "laptop" => 10D,
            "phone" or "tablet" or "rugged" or "scanner" => 12D,
            "watch" or "watch-round" or "appliance-round" or "thermostat" => 16D,
            "band" => 12D,
            "paper" or "receipt" or "badge" => 1D,
            _ => 12D
        };
        Double radius = shape switch
        {
            "web" => 12D,
            "phone" or "tablet" or "rugged" or "scanner" => 34D,
            "watch" => width * 0.28D,
            "watch-round" or "appliance-round" or "thermostat" => width / 2D,
            "band" => 80D,
            "paper" or "receipt" or "badge" => 2D,
            _ => 18D
        };
        return new DeviceProfileDefinition(
            id,
            label,
            kind,
            width,
            height,
            border,
            radius,
            shape == "web" ? 28D : 0D,
            Math.Max(160D, width - (border * 2D) - 48D),
            group,
            description,
            shape);
    }
}
