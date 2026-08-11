using System.Reflection;
using System.Text.Json;
using LumuiProtocol = Lumui.Client.LumuiProtocol;

namespace Lumui.Client;

public sealed class LumuiSpecification
{
    private LumuiSpecification(
        IReadOnlyDictionary<String, ComponentContract> components,
        IReadOnlySet<String> actionReferenceFields,
        IReadOnlySet<String> componentReferenceFields,
        IReadOnlySet<String> componentCollectionFields,
        JsonElement surfaceSchema,
        JsonElement descriptorSchema,
        JsonElement discoverySchema,
        JsonElement actionMessageSchema)
    {
        Components = components;
        ActionReferenceFields = actionReferenceFields;
        ComponentReferenceFields = componentReferenceFields;
        ComponentCollectionFields = componentCollectionFields;
        SurfaceSchema = surfaceSchema;
        DescriptorSchema = descriptorSchema;
        DiscoverySchema = discoverySchema;
        ActionMessageSchema = actionMessageSchema;
    }

    public IReadOnlyDictionary<String, ComponentContract> Components { get; }

    public IReadOnlySet<String> ActionReferenceFields { get; }

    public IReadOnlySet<String> ComponentReferenceFields { get; }

    public IReadOnlySet<String> ComponentCollectionFields { get; }

    internal JsonElement SurfaceSchema { get; }

    internal JsonElement DescriptorSchema { get; }

    internal JsonElement DiscoverySchema { get; }

    internal JsonElement ActionMessageSchema { get; }

    public static LumuiSpecification LoadEmbedded()
    {
        using JsonDocument catalogDocument = ReadResource(
            LumuiSpecificationResources.ComponentCatalog);
        JsonElement catalog = catalogDocument.RootElement;
        RequireVersion(
            catalog,
            LumuiProtocol.Fields.LumuiComponentCatalog,
            LumuiProtocol.Versions.ComponentCatalog);

        HashSet<String> commonFields = new HashSet<String>(StringComparer.Ordinal);
        AddStringArray(catalog, LumuiProtocol.Fields.CommonRequired, commonFields);
        AddStringArray(catalog, LumuiProtocol.Fields.CommonOptional, commonFields);
        HashSet<String> actionReferenceFields = new HashSet<String>(StringComparer.Ordinal);
        AddStringArray(
            catalog,
            LumuiProtocol.Fields.ActionReferenceFields,
            actionReferenceFields);
        HashSet<String> componentReferenceFields = new HashSet<String>(StringComparer.Ordinal);
        AddStringArray(
            catalog,
            LumuiProtocol.Fields.ComponentReferenceFields,
            componentReferenceFields);
        HashSet<String> componentCollectionFields = new HashSet<String>(StringComparer.Ordinal);
        AddStringArray(
            catalog,
            LumuiProtocol.Fields.ComponentCollectionFields,
            componentCollectionFields);
        if (catalog.TryGetProperty(
                LumuiProtocol.Fields.SemanticHints,
                out JsonElement semanticHints)
            && semanticHints.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in semanticHints.EnumerateObject())
            {
                commonFields.Add(property.Name);
            }
        }

        Dictionary<String, ComponentContract> components =
            new Dictionary<String, ComponentContract>(StringComparer.Ordinal);
        JsonElement componentValues = RequiredObject(
            catalog,
            LumuiProtocol.Fields.Components);
        foreach (JsonProperty component in componentValues.EnumerateObject())
        {
            HashSet<String> allowed = new HashSet<String>(commonFields, StringComparer.Ordinal);
            List<String[]> required = new List<String[]>();
            HashSet<String> forbidden = new HashSet<String>(StringComparer.Ordinal);
            AddRequirements(
                component.Value,
                LumuiProtocol.Fields.Required,
                allowed,
                required);
            AddRequirements(
                component.Value,
                LumuiProtocol.Fields.Optional,
                allowed,
                null);
            AddOptionalStringArray(
                component.Value,
                LumuiProtocol.Fields.Forbidden,
                forbidden);
            components.Add(
                component.Name,
                new ComponentContract(component.Name, allowed, required, forbidden));
        }

        using JsonDocument surfaceSchema = ReadResource(
            LumuiSpecificationResources.SurfaceSchema);
        using JsonDocument descriptorSchema = ReadResource(
            LumuiSpecificationResources.DescriptorSchema);
        using JsonDocument discoverySchema = ReadResource(
            LumuiSpecificationResources.DiscoverySchema);
        using JsonDocument actionMessageSchema = ReadResource(
            LumuiSpecificationResources.ActionMessageSchema);

        return new LumuiSpecification(
            components,
            actionReferenceFields,
            componentReferenceFields,
            componentCollectionFields,
            surfaceSchema.RootElement.Clone(),
            descriptorSchema.RootElement.Clone(),
            discoverySchema.RootElement.Clone(),
            actionMessageSchema.RootElement.Clone());
    }

    private static JsonDocument ReadResource(String name)
    {
        Assembly assembly = typeof(LumuiSpecification).Assembly;
        Stream stream = assembly.GetManifestResourceStream(
            LumuiSpecificationResources.Prefix + name)
            ?? throw new LumuiProtocolException(
                $"The embedded LUMUI specification resource '{name}' is unavailable.");
        try
        {
            return JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 128 });
        }
        finally
        {
            stream.Dispose();
        }
    }

    private static JsonElement RequiredObject(JsonElement value, String name)
    {
        if (!value.TryGetProperty(name, out JsonElement result)
            || result.ValueKind != JsonValueKind.Object)
        {
            throw new LumuiProtocolException(
                $"The embedded LUMUI specification does not define '{name}'.");
        }
        return result;
    }

    private static void AddStringArray(
        JsonElement value,
        String name,
        HashSet<String> destination)
    {
        if (!value.TryGetProperty(name, out JsonElement array)
            || array.ValueKind != JsonValueKind.Array)
        {
            throw new LumuiProtocolException(
                $"The embedded LUMUI component catalog does not define '{name}'.");
        }
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String
                || String.IsNullOrWhiteSpace(item.GetString()))
            {
                throw new LumuiProtocolException(
                    $"The embedded LUMUI component catalog contains an invalid '{name}' entry.");
            }
            destination.Add(item.GetString()!);
        }
    }

    private static void AddRequirements(
        JsonElement definition,
        String name,
        HashSet<String> allowed,
        List<String[]>? required)
    {
        if (!definition.TryGetProperty(name, out JsonElement values)
            || values.ValueKind != JsonValueKind.Array)
        {
            return;
        }
        foreach (JsonElement value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String
                || String.IsNullOrWhiteSpace(value.GetString()))
            {
                throw new LumuiProtocolException(
                    $"The embedded LUMUI component catalog contains an invalid '{name}' entry.");
            }
            String[] alternatives = value.GetString()!
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (String field in alternatives)
            {
                allowed.Add(field);
            }
            required?.Add(alternatives);
        }
    }

    private static void AddOptionalStringArray(
        JsonElement value,
        String name,
        HashSet<String> destination)
    {
        if (!value.TryGetProperty(name, out JsonElement array))
        {
            return;
        }
        if (array.ValueKind != JsonValueKind.Array)
        {
            throw new LumuiProtocolException(
                $"The embedded LUMUI component catalog contains an invalid '{name}' member.");
        }
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String
                || String.IsNullOrWhiteSpace(item.GetString()))
            {
                throw new LumuiProtocolException(
                    $"The embedded LUMUI component catalog contains an invalid '{name}' entry.");
            }
            destination.Add(item.GetString()!);
        }
    }

    private static void RequireVersion(JsonElement value, String field, String expected)
    {
        if (!value.TryGetProperty(field, out JsonElement version)
            || version.ValueKind != JsonValueKind.String
            || version.GetString() != expected)
        {
            throw new LumuiProtocolException(
                $"The embedded LUMUI specification resource '{field}' is unsupported.");
        }
    }
}
