using System.Text;
using System.Text.Json;
using LumuiProtocol = Lumui.Client.LumuiProtocol;

namespace Lumui.Browser.DeveloperTools;

public static class ActionInspector
{
    public static String Describe(JsonElement surface)
    {
        if (!surface.TryGetProperty(
                LumuiProtocol.Fields.Actions,
                out JsonElement actions)
            || actions.ValueKind != JsonValueKind.Object
            || !actions.EnumerateObject().Any())
        {
            return "No actions are defined on this page.";
        }

        StringBuilder output = new StringBuilder();
        foreach (JsonProperty action in actions.EnumerateObject())
        {
            output.Append(action.Name);
            String confirmation = Text(
                action.Value,
                LumuiProtocol.Fields.Confirmation);
            if (confirmation.Length > 0)
            {
                output.Append("  ·  confirmation: ");
                output.Append(confirmation);
            }
            output.AppendLine();
        }
        return output.ToString();
    }

    private static String Text(JsonElement value, String name) =>
        value.TryGetProperty(name, out JsonElement property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? String.Empty
            : String.Empty;
}
