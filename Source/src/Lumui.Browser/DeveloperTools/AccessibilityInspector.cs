using System.Text;
using System.Text.Json;
using Lumui.Browser.Presentation;
using LumuiProtocol = Lumui.Client.LumuiProtocol;

namespace Lumui.Browser.DeveloperTools;

public static class AccessibilityInspector
{
    public static String Describe(
        JsonElement surface,
        RendererSettings settings)
    {
        Int32 headings = 0;
        Int32 controls = 0;
        Int32 images = 0;
        Int32 alternatives = 0;
        Inspect(
            surface,
            ref headings,
            ref controls,
            ref images,
            ref alternatives);

        StringBuilder output = new StringBuilder();
        output.AppendLine("Semantic reading order is available.");
        output.AppendLine("Every native control is exposed to assistive technology.");
        output.AppendLine();
        output.AppendLine("Headings: " + headings);
        output.AppendLine("Interactive controls: " + controls);
        output.AppendLine("Images: " + images);
        output.AppendLine("Image descriptions: " + alternatives);
        output.AppendLine("Reading preferences: " + settings.AccessibilitySummary);
        output.AppendLine("Presentation: " + settings.Interaction.Label);
        return output.ToString();
    }

    private static void Inspect(
        JsonElement value,
        ref Int32 headings,
        ref Int32 controls,
        ref Int32 images,
        ref Int32 alternatives)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                Inspect(
                    item,
                    ref headings,
                    ref controls,
                    ref images,
                    ref alternatives);
            }
            return;
        }
        if (value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        String kind = Text(value, LumuiProtocol.Fields.Kind);
        String textRole = Text(value, LumuiProtocol.Fields.TextRole);
        if (kind == LumuiProtocol.ComponentKinds.Text
            && textRole == LumuiProtocol.TextRoles.Heading)
        {
            headings++;
        }
        if (kind is LumuiProtocol.ComponentKinds.Button
            or LumuiProtocol.ComponentKinds.Link
            or LumuiProtocol.ComponentKinds.Toggle
            or LumuiProtocol.ComponentKinds.CheckBox
            or LumuiProtocol.ComponentKinds.CheckOption
            or LumuiProtocol.ComponentKinds.Choice
            or LumuiProtocol.ComponentKinds.ComboBox
            or LumuiProtocol.ComponentKinds.RadioGroup
            or LumuiProtocol.ComponentKinds.MultiSelect
            or LumuiProtocol.ComponentKinds.Slider
            or LumuiProtocol.ComponentKinds.Stepper
            or LumuiProtocol.ComponentKinds.TextField
            or LumuiProtocol.ComponentKinds.TextArea
            or LumuiProtocol.ComponentKinds.PasswordField
            or LumuiProtocol.ComponentKinds.SearchField
            or LumuiProtocol.ComponentKinds.NumberField)
        {
            controls++;
        }
        if (kind == LumuiProtocol.ComponentKinds.Image)
        {
            images++;
            if (Text(value, LumuiProtocol.Fields.Alt).Length > 0)
            {
                alternatives++;
            }
        }

        foreach (JsonProperty property in value.EnumerateObject())
        {
            Inspect(
                property.Value,
                ref headings,
                ref controls,
                ref images,
                ref alternatives);
        }
    }

    private static String Text(JsonElement value, String name) =>
        value.TryGetProperty(name, out JsonElement property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? String.Empty
            : String.Empty;
}
