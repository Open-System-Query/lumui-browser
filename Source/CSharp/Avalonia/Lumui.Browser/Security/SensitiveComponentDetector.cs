using System.Text.Json;
using LumuiProtocol = Lumui.Client.LumuiProtocol;

namespace Lumui.Browser.Security;

public static class SensitiveComponentDetector
{
    public static Boolean IsSensitiveAction(
        JsonElement surface,
        String componentId)
    {
        if (surface.ValueKind == JsonValueKind.Object)
        {
            if (Text(surface, LumuiProtocol.Fields.Id) == componentId
                && IsSensitiveKind(Text(surface, LumuiProtocol.Fields.Kind)))
            {
                return true;
            }
            foreach (JsonProperty property in surface.EnumerateObject())
            {
                if (IsSensitiveAction(property.Value, componentId))
                {
                    return true;
                }
            }
        }
        else if (surface.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in surface.EnumerateArray())
            {
                if (IsSensitiveAction(child, componentId))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static Boolean IsSensitiveKind(String kind) => kind is
        LumuiProtocol.ComponentKinds.ContactPicker
        or LumuiProtocol.ComponentKinds.FilePicker
        or LumuiProtocol.ComponentKinds.LocationPicker
        or LumuiProtocol.ComponentKinds.MediaPicker
        or LumuiProtocol.ComponentKinds.Dialer;

    private static String Text(JsonElement element, String name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? String.Empty
            : String.Empty;
}
