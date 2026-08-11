using System.Text;
using System.Text.Json;
using Lumui.Browser.Presentation;
using Lumui.Client;
using LumuiProtocol = Lumui.Client.LumuiProtocol;

namespace Lumui.Browser.DeveloperTools;

public static class DocumentInspector
{
    public static String Describe(
        LoadedSurface loaded,
        RendererSettings settings,
        TimeSpan loadDuration) =>
        Describe(
            loaded.Document.RootElement,
            loaded.Address,
            loaded.SurfaceUri,
            loaded.DescriptorUri,
            loaded.ActionUri,
            loaded.EntityTag?.ToString(),
            loaded.Source,
            settings,
            loadDuration);

    public static String Describe(
        JsonElement root,
        Uri address,
        Uri surfaceUri,
        Uri? descriptorUri,
        Uri? actionUri,
        String? entityTag,
        String source,
        RendererSettings settings,
        TimeSpan loadDuration)
    {
        StringBuilder output = new StringBuilder();
        Append(
            output,
            DeveloperToolsText.Address,
            address.AbsoluteUri);
        Append(
            output,
            DeveloperToolsText.Surface,
            surfaceUri.AbsoluteUri);
        Append(
            output,
            DeveloperToolsText.Descriptor,
            descriptorUri?.AbsoluteUri
                ?? DeveloperToolsText.NotAdvertised);
        Append(
            output,
            DeveloperToolsText.Actions,
            actionUri?.AbsoluteUri
                ?? DeveloperToolsText.NotAdvertised);
        Append(
            output,
            DeveloperToolsText.EntityTag,
            entityTag ?? DeveloperToolsText.None);
        Append(
            output,
            DeveloperToolsText.Profile,
            settings.Profile.Label
            + "  ("
            + settings.Profile.Id
            + ")");
        Append(
            output,
            DeveloperToolsText.Style,
            settings.Appearance.Label
            + "  ("
            + settings.Appearance.Id
            + ")");
        Append(
            output,
            DeveloperToolsText.Output,
            settings.Output.Label);
        Append(
            output,
            DeveloperToolsText.Interaction,
            settings.Interaction.Label);
        Append(
            output,
            DeveloperToolsText.Accessibility,
            settings.AccessibilitySummary);
        Append(
            output,
            DeveloperToolsText.LoadTime,
            DeveloperToolsText.LoadTimeValue(
                loadDuration.TotalMilliseconds));
        output.AppendLine();
        Append(
            output,
            DeveloperToolsText.Protocol,
            Property(root, LumuiProtocol.Fields.LumuiSurface));
        Append(
            output,
            DeveloperToolsText.Application,
            Property(root, LumuiProtocol.Fields.AppId));
        Append(
            output,
            DeveloperToolsText.SurfaceId,
            Property(root, LumuiProtocol.Fields.SurfaceId));
        Append(
            output,
            DeveloperToolsText.DocumentTitle,
            Property(root, LumuiProtocol.Fields.Title));
        Append(
            output,
            DeveloperToolsText.Mode,
            Property(root, LumuiProtocol.Fields.Mode));
        Append(
            output,
            DeveloperToolsText.Revision,
            Property(root, LumuiProtocol.Fields.Revision));
        Append(
            output,
            DeveloperToolsText.Pages,
            CountArray(
                root,
                LumuiProtocol.Fields.Pages).ToString());
        Append(
            output,
            DeveloperToolsText.Components,
            CountComponents(root).ToString());
        Append(
            output,
            DeveloperToolsText.ActionsDefined,
            CountObject(
                root,
                LumuiProtocol.Fields.Actions).ToString());
        Append(
            output,
            DeveloperToolsText.SourceBytes,
            Encoding.UTF8.GetByteCount(source).ToString());
        return output.ToString();
    }

    private static void Append(StringBuilder output, String name, String value)
    {
        output.Append(name.PadRight(18));
        output.AppendLine(value);
    }

    private static String Property(JsonElement element, String name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return DeveloperToolsText.NotPresent;
        }
        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? String.Empty
            : value.GetRawText();
    }

    private static Int32 CountArray(JsonElement element, String name)
    {
        return element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Array
                ? value.GetArrayLength()
                : 0;
    }

    private static Int32 CountObject(JsonElement element, String name)
    {
        return element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Object
                ? value.EnumerateObject().Count()
                : 0;
    }

    private static Int32 CountComponents(JsonElement value)
    {
        Int32 count = 0;
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty(LumuiProtocol.Fields.Kind, out JsonElement kind)
                && kind.ValueKind == JsonValueKind.String)
            {
                count++;
            }
            foreach (JsonProperty property in value.EnumerateObject())
            {
                count += CountComponents(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                count += CountComponents(item);
            }
        }
        return count;
    }
}
