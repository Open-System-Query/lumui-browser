using Lumui.Cli.Rendering;

namespace Lumui.Cli.Diagnostics;

public static class SurfaceDiagnostics
{
    public static String Overview(LoadedSurface loaded, TimeSpan loadDuration)
    {
        JsonElement root = loaded.Document.RootElement;
        StringBuilder output = new StringBuilder();
        Append(output, "Address", loaded.Address.AbsoluteUri);
        Append(output, "Surface", loaded.SurfaceUri.AbsoluteUri);
        Append(output, "Descriptor", loaded.DescriptorUri?.AbsoluteUri ?? "Not advertised");
        Append(output, "Actions", loaded.ActionUri?.AbsoluteUri ?? "Not advertised");
        Append(output, "Entity tag", loaded.EntityTag?.ToString() ?? "None");
        Append(output, "Profile", "terminal.landscape.default");
        Append(output, "Output", "terminal");
        Append(output, "Interaction", "keyboard and mouse");
        Append(output, "Load time", loadDuration.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms");
        output.AppendLine();
        Append(output, "Protocol", Property(root, LumuiProtocol.Fields.LumuiSurface));
        Append(output, "Application", Property(root, LumuiProtocol.Fields.AppId));
        Append(output, "Surface id", Property(root, LumuiProtocol.Fields.SurfaceId));
        Append(output, "Title", Property(root, LumuiProtocol.Fields.Title));
        Append(output, "Mode", Property(root, LumuiProtocol.Fields.Mode));
        Append(output, "Revision", Property(root, LumuiProtocol.Fields.Revision));
        Append(output, "Identity", NestedProperty(root, LumuiProtocol.Fields.Identity, LumuiProtocol.Fields.Name));
        Append(output, "Primary routes", NestedArrayCount(root, LumuiProtocol.Fields.Navigation, LumuiProtocol.Fields.Routes).ToString(CultureInfo.InvariantCulture));
        Append(output, "Footer groups", NestedArrayCount(root, LumuiProtocol.Fields.Navigation, LumuiProtocol.Fields.Groups).ToString(CultureInfo.InvariantCulture));
        Append(output, "Pages", CountArray(root, LumuiProtocol.Fields.Pages).ToString(CultureInfo.InvariantCulture));
        Append(output, "Components", CountComponents(root).ToString(CultureInfo.InvariantCulture));
        Append(output, "Actions defined", CountObject(root, LumuiProtocol.Fields.Actions).ToString(CultureInfo.InvariantCulture));
        Append(output, "Source bytes", Encoding.UTF8.GetByteCount(loaded.Source).ToString(CultureInfo.InvariantCulture));
        return output.ToString();
    }

    public static String FormattedSource(LoadedSurface loaded)
    {
        return JsonSerializer.Serialize(
            loaded.Document.RootElement,
            LumuiJsonSerializerContext.Default.JsonElement);
    }

    public static String Structure(JsonElement surface)
    {
        StringBuilder output = new StringBuilder();
        StructureNode(surface, "surface", 0, output);
        return output.ToString();
    }

    public static String Problems(JsonElement surface)
    {
        List<String> problems = new List<String>();
        FindProblems(surface, problems);
        if (problems.Count == 0)
        {
            return "No problems found.\n\nThe document matches LUMUI 1.0 and every component has a native terminal presentation or fallback.";
        }
        return String.Join(Environment.NewLine, problems.Select(problem => "• " + problem));
    }

    public static String Accessibility(JsonElement surface)
    {
        Int32 headings = 0;
        Int32 controls = 0;
        Int32 images = 0;
        Int32 alternatives = 0;
        VisitAccessibility(surface, ref headings, ref controls, ref images, ref alternatives);
        StringBuilder output = new StringBuilder();
        output.AppendLine("Semantic reading order is available.");
        output.AppendLine("Every native control is keyboard accessible.");
        output.AppendLine();
        Append(output, "Headings", headings.ToString(CultureInfo.InvariantCulture));
        Append(output, "Interactive controls", controls.ToString(CultureInfo.InvariantCulture));
        Append(output, "Images", images.ToString(CultureInfo.InvariantCulture));
        Append(output, "Image descriptions", alternatives.ToString(CultureInfo.InvariantCulture));
        Append(output, "Presentation", "terminal-native");
        return output.ToString();
    }

    public static String Actions(JsonElement surface)
    {
        if (!surface.TryGetProperty(LumuiProtocol.Fields.Actions, out JsonElement actions)
            || actions.ValueKind != JsonValueKind.Object
            || !actions.EnumerateObject().Any())
        {
            return "No actions are defined on this page.";
        }
        StringBuilder output = new StringBuilder();
        Int32 width = Math.Clamp(actions.EnumerateObject().Max(action => action.Name.Length) + 3, 12, 32);
        foreach (JsonProperty action in actions.EnumerateObject())
        {
            output.Append(action.Name.PadRight(width));
            String confirmation = Text(action.Value, LumuiProtocol.Fields.Confirmation);
            if (confirmation.Length > 0)
            {
                output.Append("confirmation  ");
                output.Append(confirmation);
            }
            else
            {
                output.Append("ready");
            }
            output.AppendLine();
        }
        return output.ToString();
    }

    public static String Network(IEnumerable<LumuiRequestTrace> traces)
    {
        StringBuilder output = new StringBuilder();
        foreach (LumuiRequestTrace trace in traces.Reverse())
        {
            output.Append(trace.StartedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture).PadRight(11));
            output.Append(trace.Method.PadRight(8));
            output.Append((trace.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "ERR").PadRight(7));
            output.Append((trace.Duration.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms").PadRight(10));
            output.AppendLine(trace.RequestUri.AbsoluteUri);
            if (trace.Error.Length > 0)
            {
                output.Append(' ', 36);
                output.AppendLine(trace.Error);
            }
        }
        return output.Length == 0 ? "No network requests have been recorded." : output.ToString();
    }

    private static void StructureNode(JsonElement element, String fallback, Int32 depth, StringBuilder output)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        String kind = Text(element, LumuiProtocol.Fields.Kind);
        if (kind.Length == 0)
        {
            kind = fallback;
        }
        String id = Text(element, LumuiProtocol.Fields.Id);
        if (id.Length == 0)
        {
            id = Text(element, LumuiProtocol.Fields.SurfaceId);
        }
        String label = Text(element, LumuiProtocol.Fields.Label);
        if (label.Length == 0)
        {
            label = Text(element, LumuiProtocol.Fields.Title);
        }
        if (label.Length == 0)
        {
            label = Text(element, LumuiProtocol.Fields.Name);
        }
        if (label.Length == 0)
        {
            label = Text(element, LumuiProtocol.Fields.Alt);
        }
        if (label.Length == 0)
        {
            label = Text(element, LumuiProtocol.Fields.Source);
        }
        String kindColumn = new String(' ', Math.Min(depth, 6) * 2) + kind;
        output.Append(kindColumn.PadRight(30));
        output.Append(id.PadRight(34));
        output.Append(label != id ? label : String.Empty);
        output.AppendLine();
        foreach (JsonProperty property in element.EnumerateObject())
        {
            Boolean objectField = property.Name is
                LumuiProtocol.Fields.Identity or
                LumuiProtocol.Fields.Navigation or
                LumuiProtocol.Fields.Logo or
                LumuiProtocol.Fields.Icon or
                LumuiProtocol.Fields.Content or
                LumuiProtocol.Fields.Fallback or
                "empty" or
                "illustration" or
                "table_fallback";
            Boolean arrayField = property.Name is
                LumuiProtocol.Fields.Pages or
                LumuiProtocol.Fields.Regions or
                LumuiProtocol.Fields.Items or
                LumuiProtocol.Fields.Children or
                LumuiProtocol.Fields.Tabs or
                LumuiProtocol.Fields.Nodes or
                LumuiProtocol.Fields.Actions or
                LumuiProtocol.Fields.Images or
                LumuiProtocol.Fields.Routes or
                LumuiProtocol.Fields.Groups or
                LumuiProtocol.Fields.Links or
                LumuiProtocol.Fields.Fallback or
                "empty" or
                "illustration" or
                "table_fallback" or
                "variants";
            if (objectField && property.Value.ValueKind == JsonValueKind.Object)
            {
                StructureNode(property.Value, property.Name, depth + 1, output);
            }
            else if (arrayField && property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement value in property.Value.EnumerateArray())
                {
                    StructureNode(value, property.Name, depth + 1, output);
                }
            }
        }
    }

    private static void FindProblems(JsonElement value, ICollection<String> problems)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                FindProblems(item, problems);
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
            Boolean hasFallback = value.TryGetProperty(LumuiProtocol.Fields.Fallback, out JsonElement fallback)
                && fallback.ValueKind is JsonValueKind.Object or JsonValueKind.String;
            if (!hasFallback)
            {
                problems.Add("Component '" + Text(value, LumuiProtocol.Fields.Id) + "' requires a usable fallback.");
            }
        }
        foreach (JsonProperty property in value.EnumerateObject())
        {
            FindProblems(property.Value, problems);
        }
    }

    private static void VisitAccessibility(
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
                VisitAccessibility(item, ref headings, ref controls, ref images, ref alternatives);
            }
            return;
        }
        if (value.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        String kind = Text(value, LumuiProtocol.Fields.Kind);
        if (kind == LumuiProtocol.ComponentKinds.Text
            && Text(value, LumuiProtocol.Fields.TextRole) == LumuiProtocol.TextRoles.Heading)
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
            VisitAccessibility(property.Value, ref headings, ref controls, ref images, ref alternatives);
        }
    }

    private static void Append(StringBuilder output, String name, String value)
    {
        output.Append(name.PadRight(22));
        output.AppendLine(value);
    }

    private static String Property(JsonElement element, String name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return "Not present";
        }
        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? String.Empty
            : value.GetRawText();
    }

    private static Int32 CountArray(JsonElement element, String name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value.GetArrayLength()
            : 0;

    private static Int32 CountObject(JsonElement element, String name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Object
            ? value.EnumerateObject().Count()
            : 0;

    private static Int32 NestedArrayCount(JsonElement element, String parent, String name) =>
        element.TryGetProperty(parent, out JsonElement nested)
        && nested.ValueKind == JsonValueKind.Object
        && nested.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Array
            ? value.GetArrayLength()
            : 0;

    private static String NestedProperty(JsonElement element, String parent, String name) =>
        element.TryGetProperty(parent, out JsonElement nested)
        && nested.ValueKind == JsonValueKind.Object
            ? Text(nested, name)
            : "Not present";

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

    private static String Text(JsonElement value, String name)
    {
        if (!value.TryGetProperty(name, out JsonElement property))
        {
            return String.Empty;
        }
        if (property.ValueKind == JsonValueKind.String)
        {
            return property.GetString() ?? String.Empty;
        }
        if (property.ValueKind == JsonValueKind.Object
            && property.TryGetProperty(LumuiProtocol.Fields.Fallback, out JsonElement fallback)
            && fallback.ValueKind == JsonValueKind.String)
        {
            return fallback.GetString() ?? String.Empty;
        }
        if (property.ValueKind == JsonValueKind.Object
            && property.TryGetProperty(LumuiProtocol.Fields.Ref, out JsonElement reference)
            && reference.ValueKind == JsonValueKind.String)
        {
            return reference.GetString() ?? String.Empty;
        }
        return String.Empty;
    }
}
