using System.Text;
using System.Text.Json;
using LumuiProtocol = Lumui.Client.LumuiProtocol;

namespace Lumui.Browser.DeveloperTools;

public static class ProblemInspector
{
    public static IReadOnlyList<String> Find(JsonElement surface)
    {
        List<String> problems = new List<String>();
        Inspect(surface, problems);
        return problems;
    }

    private static void Inspect(JsonElement value, List<String> problems)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                Inspect(item, problems);
            }
            return;
        }
        if (value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        String kind = Text(value, LumuiProtocol.Fields.Kind);
        if (kind is LumuiProtocol.ComponentKinds.Graphic
            or LumuiProtocol.ComponentKinds.Clock)
        {
            Boolean hasFallback = value.TryGetProperty(
                LumuiProtocol.Fields.Fallback,
                out JsonElement fallback)
                && fallback.ValueKind is JsonValueKind.Object or JsonValueKind.String;
            if (!hasFallback)
            {
                problems.Add(
                    "Component '"
                    + Text(value, LumuiProtocol.Fields.Id)
                    + "' requires a usable fallback.");
            }
        }

        foreach (JsonProperty property in value.EnumerateObject())
        {
            Inspect(property.Value, problems);
        }
    }

    public static String Describe(IReadOnlyList<String> problems)
    {
        if (problems.Count == 0)
        {
            return "No problems found.\n\nThe document matches LUMUI 1.0 and every component has a native presentation or fallback.";
        }

        StringBuilder output = new StringBuilder();
        foreach (String problem in problems)
        {
            output.Append("• ");
            output.AppendLine(problem);
        }
        return output.ToString();
    }

    private static String Text(JsonElement value, String name) =>
        value.TryGetProperty(name, out JsonElement property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? String.Empty
            : String.Empty;
}
