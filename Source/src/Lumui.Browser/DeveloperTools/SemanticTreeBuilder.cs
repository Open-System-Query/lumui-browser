using System.Text.Json;
using Avalonia.Controls;
using LumuiProtocol = Lumui.Client.LumuiProtocol;

namespace Lumui.Browser.DeveloperTools;

public static class SemanticTreeBuilder
{
    public static IReadOnlyList<TreeViewItem> Build(JsonElement surface)
    {
        List<TreeViewItem> roots = new List<TreeViewItem>();
        roots.Add(Node(surface, LumuiProtocol.Fields.Surface));
        return roots.AsReadOnly();
    }

    private static TreeViewItem Node(
        JsonElement element,
        String fallback)
    {
        TreeViewItem item = new TreeViewItem
        {
            Header = Header(element, fallback),
            DataContext = element.GetRawText(),
            IsExpanded = fallback == LumuiProtocol.Fields.Surface
        };
        List<TreeViewItem> children = new List<TreeViewItem>();
        AddChildren(
            element,
            LumuiProtocol.Fields.Pages,
            LumuiProtocol.SchemaDefinitions.Page,
            children);
        AddChildren(
            element,
            LumuiProtocol.Fields.Regions,
            DeveloperToolsText.RegionNode,
            children);
        AddChildren(
            element,
            LumuiProtocol.Fields.Items,
            DeveloperToolsText.ComponentNode,
            children);
        AddChildren(
            element,
            LumuiProtocol.Fields.Children,
            DeveloperToolsText.ComponentNode,
            children);
        item.ItemsSource = children;
        return item;
    }

    private static void AddChildren(
        JsonElement parent,
        String field,
        String fallback,
        ICollection<TreeViewItem> destination)
    {
        if (!parent.TryGetProperty(field, out JsonElement values)
            || values.ValueKind != JsonValueKind.Array)
        {
            return;
        }
        foreach (JsonElement value in values.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                destination.Add(Node(value, fallback));
            }
        }
    }

    private static String Header(
        JsonElement element,
        String fallback)
    {
        String kind = Text(
            element,
            LumuiProtocol.Fields.Kind,
            fallback);
        String id = Text(
            element,
            LumuiProtocol.Fields.Id,
            Text(
                element,
                LumuiProtocol.Fields.SurfaceId,
                String.Empty));
        String label = Text(
            element,
            LumuiProtocol.Fields.Label,
            Text(
                element,
                LumuiProtocol.Fields.Title,
                String.Empty));
        String result = kind;
        if (id.Length > 0)
        {
            result += "  " + id;
        }
        if (label.Length > 0 && label != id)
        {
            result += "  ·  " + label;
        }
        return result;
    }

    private static String Text(
        JsonElement element,
        String field,
        String fallback)
    {
        return element.TryGetProperty(field, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? fallback
                : fallback;
    }
}
