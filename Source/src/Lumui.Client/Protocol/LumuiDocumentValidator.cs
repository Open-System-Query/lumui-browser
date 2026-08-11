using System.Text.Json;
using LumuiProtocol = Lumui.Client.LumuiProtocol;

namespace Lumui.Client;

public sealed class LumuiDocumentValidator
{
    private const Int32 MaximumComponents = 5_000;
    private const Int32 MaximumDepth = 12;
    private const Int32 MaximumReferenceDepth = 64;
    private readonly LumuiSpecification _specification;
    private readonly JsonSchemaValidator _surfaceSchemaValidator;
    private readonly JsonSchemaValidator _descriptorSchemaValidator;
    private readonly JsonSchemaValidator _discoverySchemaValidator;
    private readonly JsonSchemaValidator _actionMessageSchemaValidator;

    public LumuiDocumentValidator(LumuiSpecification specification)
    {
        _specification = specification ?? throw new ArgumentNullException(nameof(specification));
        _surfaceSchemaValidator = new JsonSchemaValidator(_specification.SurfaceSchema);
        _descriptorSchemaValidator = new JsonSchemaValidator(_specification.DescriptorSchema);
        _discoverySchemaValidator = new JsonSchemaValidator(_specification.DiscoverySchema);
        _actionMessageSchemaValidator = new JsonSchemaValidator(
            _specification.ActionMessageSchema);
    }

    public static LumuiDocumentValidator CreateDefault()
    {
        return new LumuiDocumentValidator(LumuiSpecification.LoadEmbedded());
    }

    public void ValidateSurface(JsonElement root)
    {
        _surfaceSchemaValidator.Validate(root, "Surface");
        JsonElement actions = root.GetProperty(LumuiProtocol.Fields.Actions);
        JsonElement pages = root.GetProperty(LumuiProtocol.Fields.Pages);

        HashSet<String> identifiers = new HashSet<String>(StringComparer.Ordinal);
        HashSet<String> pageIdentifiers = new HashSet<String>(StringComparer.Ordinal);
        Int32 componentCount = 0;
        foreach (JsonElement page in pages.EnumerateArray())
        {
            String pageId = ClaimId(page, LumuiProtocol.Fields.Id, identifiers);
            pageIdentifiers.Add(pageId);
            ValidateActionReferences(page, actions, $"Page '{pageId}'");
            JsonElement regions = page.GetProperty(LumuiProtocol.Fields.Regions);
            foreach (JsonElement region in regions.EnumerateArray())
            {
                ValidateComponent(
                    region,
                    actions,
                    identifiers,
                    ref componentCount,
                    1,
                    true);
            }
        }
        ValidateSurfaceReferences(root, actions, pageIdentifiers);
        ValidateReferences(root, 0);
    }

    public void ValidateDescriptor(JsonElement root)
    {
        _descriptorSchemaValidator.Validate(root, "Descriptor");
        ValidateLink(
            root,
            LumuiProtocol.Fields.Surface,
            LumuiProtocol.MediaTypes.LumuiJson);
        ValidateLink(
            root,
            LumuiProtocol.Fields.Fallback,
            LumuiProtocol.MediaTypes.Html);

        if (root.TryGetProperty(LumuiProtocol.Fields.Actions, out JsonElement actions))
        {
            ValidateLinkValue(
                actions,
                LumuiProtocol.Fields.Actions,
                LumuiProtocol.MediaTypes.LumuiJson);
        }
        ValidateReferences(root, 0);
    }

    public void ValidateDiscovery(JsonElement root)
    {
        _discoverySchemaValidator.Validate(root, "Discovery");
        ValidateLink(
            root,
            LumuiProtocol.Fields.Descriptor,
            LumuiProtocol.MediaTypes.LumuiJson);
        ValidateLink(
            root,
            LumuiProtocol.Fields.Fallback,
            LumuiProtocol.MediaTypes.Html);
        ValidateReferences(root, 0);
    }

    public void ValidateActionResultEnvelope(JsonElement root)
    {
        _actionMessageSchemaValidator.ValidateDefinition(
            root,
            LumuiProtocol.SchemaDefinitions.Result,
            LumuiProtocol.Fields.Result);
        ValidateReferences(root, 0);
    }

    private static void ValidateSurfaceReferences(
        JsonElement root,
        JsonElement actions,
        IReadOnlySet<String> pageIdentifiers)
    {
        if (root.TryGetProperty(
                LumuiProtocol.Fields.RequestedPageId,
                out JsonElement requestedPage)
            && !pageIdentifiers.Contains(requestedPage.GetString() ?? String.Empty))
        {
            throw new LumuiProtocolException(
                "The requested page is not published by this surface.");
        }
        if (!root.TryGetProperty(
                LumuiProtocol.Fields.Navigation,
                out JsonElement navigation))
        {
            return;
        }

        String startPage = navigation
            .GetProperty(LumuiProtocol.Fields.StartPage)
            .GetString()
            ?? String.Empty;
        if (!pageIdentifiers.Contains(startPage))
        {
            throw new LumuiProtocolException(
                "The navigation start page is not published by this surface.");
        }
        if (!navigation.TryGetProperty(
                LumuiProtocol.Fields.Routes,
                out JsonElement routes))
        {
            return;
        }
        foreach (JsonElement route in routes.EnumerateArray())
        {
            if (route.TryGetProperty(
                    LumuiProtocol.Fields.Href,
                    out JsonElement _))
            {
                continue;
            }
            String from = route
                .GetProperty(LumuiProtocol.Fields.From)
                .GetString()
                ?? String.Empty;
            String to = route
                .GetProperty(LumuiProtocol.Fields.To)
                .GetString()
                ?? String.Empty;
            String action = route
                .GetProperty(LumuiProtocol.Fields.Action)
                .GetString()
                ?? String.Empty;
            if (!pageIdentifiers.Contains(from)
                || !pageIdentifiers.Contains(to)
                || !actions.TryGetProperty(action, out JsonElement _))
            {
                throw new LumuiProtocolException(
                    "A navigation transition references an unavailable page or action.");
            }
        }
    }

    private void ValidateComponent(
        JsonElement component,
        JsonElement actions,
        HashSet<String> identifiers,
        ref Int32 componentCount,
        Int32 depth,
        Boolean requireItems)
    {
        if (depth > MaximumDepth)
        {
            throw new LumuiProtocolException(
                $"The component tree exceeds {MaximumDepth} levels.");
        }
        RequireObject(component, "component");
        String id = ClaimId(component, LumuiProtocol.Fields.Id, identifiers);
        String kind = component
            .GetProperty(LumuiProtocol.Fields.Kind)
            .GetString()
            ?? String.Empty;
        if (!_specification.Components.TryGetValue(
                kind,
                out ComponentContract? contract))
        {
            throw new LumuiProtocolException(
                $"Component '{id}' uses unknown kind '{kind}'.");
        }

        RejectForbiddenFields(component, contract.ForbiddenFields, id);
        RejectUnknownFields(component, contract.AllowedFields, $"Component '{id}'");
        RequireCatalogFields(component, id, contract);
        componentCount++;
        if (componentCount > MaximumComponents)
        {
            throw new LumuiProtocolException(
                $"The surface exceeds {MaximumComponents} components.");
        }
        ValidateActionReferences(component, actions, $"Component '{id}'");

        if (requireItems
            && (!component.TryGetProperty(
                    LumuiProtocol.Fields.Items,
                    out JsonElement requiredItems)
                || requiredItems.ValueKind != JsonValueKind.Array))
        {
            throw new LumuiProtocolException($"Region '{id}' requires an items array.");
        }

        foreach (String field in _specification.ComponentCollectionFields)
        {
            if (!component.TryGetProperty(field, out JsonElement children)
                || children.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            foreach (JsonElement child in children.EnumerateArray())
            {
                if (child.ValueKind == JsonValueKind.Object
                    && child.TryGetProperty(LumuiProtocol.Fields.Kind, out JsonElement _))
                {
                    ValidateComponent(
                        child,
                        actions,
                        identifiers,
                        ref componentCount,
                        depth + 1,
                        false);
                }
            }
        }

        foreach (String field in _specification.ComponentReferenceFields)
        {
            if (component.TryGetProperty(field, out JsonElement child)
                && child.ValueKind == JsonValueKind.Object
                && child.TryGetProperty(LumuiProtocol.Fields.Kind, out JsonElement _))
            {
                ValidateComponent(
                    child,
                    actions,
                    identifiers,
                    ref componentCount,
                    depth + 1,
                    false);
            }
        }
    }

    private static void RequireCatalogFields(
        JsonElement component,
        String id,
        ComponentContract contract)
    {
        foreach (String[] alternatives in contract.RequiredFields)
        {
            Boolean present = alternatives.Any((String field) =>
                component.TryGetProperty(field, out JsonElement value)
                && value.ValueKind is not (
                    JsonValueKind.Null or
                    JsonValueKind.Undefined));
            if (!present)
            {
                throw new LumuiProtocolException(
                    $"Component '{id}' requires field '{String.Join("|", alternatives)}'.");
            }
        }
    }

    private static void RejectUnknownFields(
        JsonElement value,
        IReadOnlySet<String> allowedFields,
        String subject)
    {
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (!allowedFields.Contains(property.Name))
            {
                throw new LumuiProtocolException(
                    $"{subject} does not define field '{property.Name}'.");
            }
        }
    }

    private static void RejectForbiddenFields(
        JsonElement component,
        IReadOnlySet<String> forbiddenFields,
        String id)
    {
        foreach (String field in forbiddenFields)
        {
            if (component.TryGetProperty(field, out JsonElement _))
            {
                throw new LumuiProtocolException(
                    $"Component '{id}' forbids field '{field}'.");
            }
        }
    }

    private void ValidateActionReferences(
        JsonElement value,
        JsonElement actions,
        String subject)
    {
        foreach (JsonProperty property in value.EnumerateObject())
        {
            Boolean isAction = _specification.ActionReferenceFields.Contains(property.Name);
            if (isAction)
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    if (!actions.TryGetProperty(
                            property.Value.GetString() ?? String.Empty,
                            out JsonElement _))
                    {
                        throw new LumuiProtocolException(
                            $"{subject} references an undeclared action.");
                    }
                    continue;
                }
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in property.Value.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String
                            || !actions.TryGetProperty(
                                item.GetString() ?? String.Empty,
                                out JsonElement _))
                        {
                            throw new LumuiProtocolException(
                                $"{subject} references an undeclared action.");
                        }
                    }
                    continue;
                }
                throw new LumuiProtocolException(
                    $"{subject} has an invalid action reference.");
            }
        }
    }

    private static void ValidateReferences(JsonElement value, Int32 depth)
    {
        if (depth > MaximumReferenceDepth)
        {
            throw new LumuiProtocolException(
                "The LUMUI resource exceeds the validation depth limit.");
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                ValidateReferences(item, depth + 1);
            }
            return;
        }
        if (value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty property in value.EnumerateObject())
        {
            Boolean isReference = property.Name is
                LumuiProtocol.Fields.Href or
                LumuiProtocol.Fields.Src or
                LumuiProtocol.Fields.Source or
                LumuiProtocol.Fields.SourceSurface;
            if (isReference && property.Value.ValueKind == JsonValueKind.String)
            {
                ValidateReference(property.Value.GetString() ?? String.Empty);
            }
            ValidateReferences(property.Value, depth + 1);
        }
    }

    private static void ValidateLink(
        JsonElement root,
        String name,
        String expectedType)
    {
        if (!root.TryGetProperty(name, out JsonElement link))
        {
            throw new LumuiProtocolException(
                $"The LUMUI resource does not provide '{name}'.");
        }
        ValidateLinkValue(link, name, expectedType);
    }

    private static void ValidateLinkValue(
        JsonElement link,
        String name,
        String expectedType)
    {
        if (link.ValueKind != JsonValueKind.Object
            || !link.TryGetProperty(
                LumuiProtocol.Fields.Href,
                out JsonElement href)
            || href.ValueKind != JsonValueKind.String
            || String.IsNullOrWhiteSpace(href.GetString())
            || !link.TryGetProperty(
                LumuiProtocol.Fields.Type,
                out JsonElement type)
            || type.ValueKind != JsonValueKind.String
            || type.GetString() != expectedType)
        {
            throw new LumuiProtocolException(
                $"The LUMUI '{name}' link is invalid.");
        }
        ValidateReference(href.GetString()!);
    }

    private static void ValidateReference(String value)
    {
        if (String.IsNullOrWhiteSpace(value)
            || value.Any(Char.IsControl)
            || !Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out Uri? reference))
        {
            throw new LumuiProtocolException(
                "A LUMUI link is not a valid URI reference.");
        }
        if (reference.IsAbsoluteUri
            && reference.Scheme is not (
                LumuiProtocol.Schemes.Https or
                LumuiProtocol.Schemes.Http or
                LumuiProtocol.Schemes.Mail or
                LumuiProtocol.Schemes.Telephone))
        {
            throw new LumuiProtocolException(
                $"A LUMUI link uses unsupported scheme '{reference.Scheme}'.");
        }
    }

    private static String ClaimId(
        JsonElement element,
        String name,
        HashSet<String> identifiers)
    {
        String value = element.GetProperty(name).GetString() ?? String.Empty;
        if (!identifiers.Add(value))
        {
            throw new LumuiProtocolException(
                $"LUMUI id '{value}' is duplicated.");
        }
        return value;
    }

    private static void RequireObject(JsonElement element, String name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new LumuiProtocolException(
                $"The {name} must be a JSON object.");
        }
    }

}
