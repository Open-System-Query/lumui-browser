using Lumui.Cli.Configuration;

namespace Lumui.Cli.Rendering;

public sealed class TerminalSurfaceRenderer
{
    private static readonly HashSet<String> InputKinds = new HashSet<String>(StringComparer.Ordinal)
    {
        LumuiProtocol.ComponentKinds.TextField,
        LumuiProtocol.ComponentKinds.SearchField,
        LumuiProtocol.ComponentKinds.NumberField,
        LumuiProtocol.ComponentKinds.DateField,
        LumuiProtocol.ComponentKinds.TimeField,
        LumuiProtocol.ComponentKinds.DateTimeField,
        LumuiProtocol.ComponentKinds.ColorField,
        LumuiProtocol.ComponentKinds.OtpField,
        LumuiProtocol.ComponentKinds.PasswordField,
        LumuiProtocol.ComponentKinds.TextArea,
        LumuiProtocol.ComponentKinds.Toggle,
        LumuiProtocol.ComponentKinds.CheckBox,
        LumuiProtocol.ComponentKinds.CheckOption,
        LumuiProtocol.ComponentKinds.Choice,
        LumuiProtocol.ComponentKinds.ComboBox,
        LumuiProtocol.ComponentKinds.RadioGroup,
        LumuiProtocol.ComponentKinds.MultiSelect,
        LumuiProtocol.ComponentKinds.OptionBar,
        LumuiProtocol.ComponentKinds.Slider,
        LumuiProtocol.ComponentKinds.Rating,
        LumuiProtocol.ComponentKinds.Stepper,
        LumuiProtocol.ComponentKinds.DateRangeField,
        LumuiProtocol.ComponentKinds.FilePicker,
        LumuiProtocol.ComponentKinds.MediaPicker,
        LumuiProtocol.ComponentKinds.ContactPicker,
        LumuiProtocol.ComponentKinds.LocationPicker
    };

    private readonly CliPreferences _preferences;

    public TerminalSurfaceRenderer(CliPreferences preferences)
    {
        _preferences = preferences;
    }

    public TerminalSurfaceDocument Parse(LoadedSurface loaded)
    {
        JsonElement root = loaded.Document.RootElement;
        String title = Text(root, LumuiProtocol.Fields.Title, loaded.Address.Host);
        String description = Text(root, LumuiProtocol.Fields.Description);
        List<TerminalPage> pages = new List<TerminalPage>();
        Dictionary<String, Object?> initialInput = new Dictionary<String, Object?>(StringComparer.Ordinal);
        if (root.TryGetProperty(LumuiProtocol.Fields.Pages, out JsonElement pageValues)
            && pageValues.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement page in pageValues.EnumerateArray())
            {
                if (page.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                List<SemanticComponent> components = new List<SemanticComponent>();
                if (page.TryGetProperty(LumuiProtocol.Fields.Regions, out JsonElement regions)
                    && regions.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement region in regions.EnumerateArray())
                    {
                        if (region.ValueKind == JsonValueKind.Object)
                        {
                            components.Add(ParseComponent(region, loaded.SurfaceUri, LumuiProtocol.ComponentKinds.Section, initialInput));
                        }
                    }
                }
                AddActionReferences(
                    page,
                    Text(page, LumuiProtocol.Fields.Id, "page-" + pages.Count),
                    components);
                pages.Add(new TerminalPage(
                    Text(page, LumuiProtocol.Fields.Id, "page-" + pages.Count),
                    Text(page, LumuiProtocol.Fields.Title, title),
                    Text(page, LumuiProtocol.Fields.Description),
                    Text(page, LumuiProtocol.Fields.Role),
                    components));
            }
        }
        if (pages.Count == 0)
        {
            pages.Add(new TerminalPage("surface", title, description, "application", new[]
            {
                ParseComponent(root, loaded.SurfaceUri, LumuiProtocol.ComponentKinds.Page, initialInput)
            }));
        }
        String requested = Text(root, LumuiProtocol.Fields.RequestedPageId);
        Int32 requestedIndex = pages.FindIndex(page => page.Id.Equals(requested, StringComparison.Ordinal));
        Int32 activePageIndex = requestedIndex < 0 ? 0 : requestedIndex;
        TerminalSiteChrome siteChrome = ParseSiteChrome(
            root,
            loaded.SurfaceUri,
            title,
            pages,
            activePageIndex);
        return new TerminalSurfaceDocument(
            root,
            loaded.SurfaceUri,
            title,
            description,
            pages,
            activePageIndex,
            siteChrome,
            initialInput);
    }

    private static TerminalSiteChrome ParseSiteChrome(
        JsonElement root,
        Uri baseUri,
        String surfaceTitle,
        IReadOnlyList<TerminalPage> pages,
        Int32 requestedPageIndex)
    {
        Boolean hasIdentity = root.TryGetProperty(
            LumuiProtocol.Fields.Identity,
            out JsonElement identity)
            && identity.ValueKind == JsonValueKind.Object;
        String name = hasIdentity
            ? Text(identity, LumuiProtocol.Fields.Name, surfaceTitle)
            : surfaceTitle;
        String shortName = hasIdentity
            ? Text(identity, LumuiProtocol.Fields.ShortName)
            : String.Empty;
        SemanticComponent? home = hasIdentity
            ? ParseSiteLink(
                identity,
                "site.home",
                name + " home",
                Text(identity, LumuiProtocol.Fields.Home),
                String.Empty,
                baseUri)
            : null;
        SemanticComponent? logo = hasIdentity
            ? ParseIdentityAsset(identity, LumuiProtocol.Fields.Logo, "site.logo", "Logo", baseUri)
            : null;
        SemanticComponent? icon = hasIdentity
            ? ParseIdentityAsset(identity, LumuiProtocol.Fields.Icon, "site.icon", "Favicon", baseUri)
            : null;
        List<SemanticComponent> routes = new List<SemanticComponent>();
        List<SemanticComponent> groups = new List<SemanticComponent>();
        if (root.TryGetProperty(LumuiProtocol.Fields.Navigation, out JsonElement navigation)
            && navigation.ValueKind == JsonValueKind.Object)
        {
            ParseRoutes(navigation, baseUri, pages, requestedPageIndex, routes);
            ParseNavigationGroups(navigation, baseUri, groups);
        }
        String copyright = String.Empty;
        if (hasIdentity)
        {
            String holder = Text(identity, LumuiProtocol.Fields.CopyrightHolder, name);
            Int32 currentYear = DateTime.Now.Year;
            Int32 startYear = identity.TryGetProperty(
                    LumuiProtocol.Fields.CopyrightStartYear,
                    out JsonElement year)
                && year.TryGetInt32(out Int32 parsedYear)
                    ? parsedYear
                    : currentYear;
            String years = startYear < currentYear
                ? startYear.ToString(CultureInfo.InvariantCulture)
                    + "-"
                    + currentYear.ToString(CultureInfo.InvariantCulture)
                : currentYear.ToString(CultureInfo.InvariantCulture);
            copyright = "© " + years + " " + holder;
        }
        return new TerminalSiteChrome(
            hasIdentity,
            name,
            shortName,
            home,
            logo,
            icon,
            routes,
            groups,
            copyright);
    }

    private static void ParseRoutes(
        JsonElement navigation,
        Uri baseUri,
        IReadOnlyList<TerminalPage> pages,
        Int32 requestedPageIndex,
        ICollection<SemanticComponent> routes)
    {
        if (!navigation.TryGetProperty(LumuiProtocol.Fields.Routes, out JsonElement values)
            || values.ValueKind != JsonValueKind.Array)
        {
            return;
        }
        String currentPageId = pages[Math.Clamp(requestedPageIndex, 0, pages.Count - 1)].Id;
        Int32 routeIndex = 0;
        foreach (JsonElement route in values.EnumerateArray())
        {
            if (route.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            String href = Text(route, LumuiProtocol.Fields.Href);
            String from = Text(route, LumuiProtocol.Fields.From);
            String to = Text(route, LumuiProtocol.Fields.To);
            String action = Text(route, LumuiProtocol.Fields.Action);
            if (href.Length == 0
                && (action.Length == 0 || !from.Equals(currentPageId, StringComparison.Ordinal)))
            {
                continue;
            }
            String pageId = Text(route, "page_id", to);
            String fallbackLabel = pages
                .FirstOrDefault(page => page.Id.Equals(pageId, StringComparison.Ordinal))
                ?.Title
                ?? "Page";
            String id = Text(
                route,
                LumuiProtocol.Fields.Id,
                "site.route." + routeIndex.ToString(CultureInfo.InvariantCulture));
            routes.Add(new SemanticComponent(
                route,
                id,
                LumuiProtocol.ComponentKinds.Link,
                Text(route, LumuiProtocol.Fields.Label, fallbackLabel),
                String.Empty,
                action,
                ResolveUri(baseUri, href),
                Array.Empty<SemanticComponent>(),
                Array.Empty<SemanticOption>(),
                Array.Empty<MediaSourceDescriptor>()));
            routeIndex++;
        }
    }

    private static void ParseNavigationGroups(
        JsonElement navigation,
        Uri baseUri,
        ICollection<SemanticComponent> groups)
    {
        if (!navigation.TryGetProperty(LumuiProtocol.Fields.Groups, out JsonElement values)
            || values.ValueKind != JsonValueKind.Array)
        {
            return;
        }
        Int32 groupIndex = 0;
        foreach (JsonElement group in values.EnumerateArray())
        {
            if (group.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            List<SemanticComponent> links = new List<SemanticComponent>();
            if (group.TryGetProperty(LumuiProtocol.Fields.Links, out JsonElement linkValues)
                && linkValues.ValueKind == JsonValueKind.Array)
            {
                Int32 linkIndex = 0;
                foreach (JsonElement link in linkValues.EnumerateArray())
                {
                    if (link.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }
                    String id = "site.group."
                        + groupIndex.ToString(CultureInfo.InvariantCulture)
                        + ".link."
                        + linkIndex.ToString(CultureInfo.InvariantCulture);
                    SemanticComponent? parsed = ParseSiteLink(
                        link,
                        id,
                        Text(link, LumuiProtocol.Fields.Label, "Link"),
                        Text(link, LumuiProtocol.Fields.Href),
                        String.Empty,
                        baseUri);
                    if (parsed is not null)
                    {
                        links.Add(parsed);
                        linkIndex++;
                    }
                }
            }
            groups.Add(new SemanticComponent(
                group,
                "site.group." + groupIndex.ToString(CultureInfo.InvariantCulture),
                LumuiProtocol.ComponentKinds.Section,
                Text(group, LumuiProtocol.Fields.Label, "Links"),
                Text(group, LumuiProtocol.Fields.Description),
                String.Empty,
                null,
                links,
                Array.Empty<SemanticOption>(),
                Array.Empty<MediaSourceDescriptor>()));
            groupIndex++;
        }
    }

    private static SemanticComponent? ParseSiteLink(
        JsonElement element,
        String id,
        String label,
        String href,
        String action,
        Uri baseUri)
    {
        Uri? target = ResolveUri(baseUri, href);
        if (href.Length == 0 && action.Length == 0)
        {
            return null;
        }
        return new SemanticComponent(
            element,
            id,
            LumuiProtocol.ComponentKinds.Link,
            label,
            href,
            action,
            target,
            Array.Empty<SemanticComponent>(),
            Array.Empty<SemanticOption>(),
            Array.Empty<MediaSourceDescriptor>());
    }

    private static SemanticComponent? ParseIdentityAsset(
        JsonElement identity,
        String field,
        String id,
        String fallbackLabel,
        Uri baseUri)
    {
        if (!identity.TryGetProperty(field, out JsonElement asset)
            || asset.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        List<MediaSourceDescriptor> sources = ParseMediaSources(asset, baseUri);
        Uri? target = sources.FirstOrDefault()?.Uri;
        if (target is null)
        {
            return null;
        }
        String label = Text(asset, LumuiProtocol.Fields.Alt);
        if (String.IsNullOrWhiteSpace(label))
        {
            label = fallbackLabel;
        }
        return new SemanticComponent(
            asset,
            id,
            LumuiProtocol.ComponentKinds.Image,
            label,
            String.Empty,
            String.Empty,
            target,
            Array.Empty<SemanticComponent>(),
            Array.Empty<SemanticOption>(),
            sources);
    }

    public RenderedTerminalPage Render(
        TerminalSurfaceDocument document,
        Int32 pageIndex,
        IReadOnlyDictionary<String, Object?> input,
        Boolean guided,
        Int32 guidedStep)
    {
        TerminalPage page = document.Pages[Math.Clamp(pageIndex, 0, document.Pages.Count - 1)];
        List<TerminalRenderLine> lines = new List<TerminalRenderLine>();
        List<String> outline = new List<String>();
        lines.Add(new TerminalRenderLine(page.Title, TerminalLineRole.Title));
        if (document.Description.Length > 0 && pageIndex == document.RequestedPageIndex)
        {
            lines.Add(new TerminalRenderLine(document.Description, TerminalLineRole.Body));
        }
        if (page.Description.Length > 0
            && !page.Description.Equals(document.Description, StringComparison.CurrentCulture))
        {
            lines.Add(new TerminalRenderLine(page.Description, TerminalLineRole.Body));
        }
        lines.Add(new TerminalRenderLine(String.Empty, TerminalLineRole.Space));

        IReadOnlyList<SemanticComponent> components = page.Components;
        Int32 stepCount = Math.Max(1, components.Count);
        IEnumerable<SemanticComponent> visible = guided && components.Count > 0
            ? components.Skip(Math.Clamp(guidedStep, 0, components.Count - 1)).Take(1)
            : components;
        foreach (SemanticComponent component in visible)
        {
            RenderComponent(component, input, lines, outline, 0);
        }
        TrimSpaces(lines);
        return new RenderedTerminalPage(lines, outline, stepCount);
    }

    private SemanticComponent ParseComponent(
        JsonElement element,
        Uri baseUri,
        String fallbackKind,
        IDictionary<String, Object?> initialInput)
    {
        String kind = Text(element, LumuiProtocol.Fields.Kind, fallbackKind);
        String id = Text(element, LumuiProtocol.Fields.Id);
        String label = Text(
            element,
            LumuiProtocol.Fields.Label,
            Text(element, LumuiProtocol.Fields.Title));
        String text = Text(
            element,
            LumuiProtocol.Fields.Text,
            Text(
                element,
                LumuiProtocol.Fields.Content,
                Text(
                    element,
                    LumuiProtocol.Fields.Body,
                    Text(
                        element,
                        LumuiProtocol.Fields.Message,
                        Text(
                            element,
                            LumuiProtocol.Fields.StateDescription,
                            Text(element, LumuiProtocol.Fields.Summary))))));
        String action = Text(element, LumuiProtocol.Fields.Action);
        Uri? target = ResolveUri(baseUri, TargetValue(element));
        List<SemanticComponent> children = new List<SemanticComponent>();
        foreach (String field in ChildFields(kind))
        {
            if (!element.TryGetProperty(field, out JsonElement values)
                || !IncludeChildField(element, field))
            {
                continue;
            }
            if (values.ValueKind == JsonValueKind.Array)
            {
                Int32 childIndex = 0;
                foreach (JsonElement child in values.EnumerateArray())
                {
                    if (child.ValueKind == JsonValueKind.Object)
                    {
                        children.Add(ParseComponent(child, baseUri, InferKind(field), initialInput));
                    }
                    else if (IsActionReferenceField(field) && child.ValueKind == JsonValueKind.String)
                    {
                        AddActionReference(element, id, field, child, childIndex, children);
                    }
                    childIndex++;
                }
            }
            else if (values.ValueKind == JsonValueKind.Object)
            {
                children.Add(ParseComponent(values, baseUri, InferKind(field), initialInput));
            }
            else if (IsActionReferenceField(field) && values.ValueKind == JsonValueKind.String)
            {
                AddActionReference(element, id, field, values, 0, children);
            }
        }
        List<SemanticOption> options = ParseOptions(element);
        List<MediaSourceDescriptor> media = ParseMediaSources(element, baseUri);
        SemanticComponent component = new SemanticComponent(
            element,
            id,
            kind,
            label,
            text,
            action,
            target,
            children,
            options,
            media);
        component.SetOrigin(baseUri);
        if (id.Length > 0 && InputKinds.Contains(kind))
        {
            initialInput[id] = InitialValue(element, kind);
        }
        return component;
    }

    private static Boolean IncludeChildField(JsonElement element, String field)
    {
        if (!field.Equals("empty", StringComparison.Ordinal))
        {
            return true;
        }
        foreach (String collectionField in new[]
        {
            LumuiProtocol.Fields.Items,
            LumuiProtocol.Fields.Children,
            LumuiProtocol.Fields.Nodes,
            LumuiProtocol.Fields.Images,
            LumuiProtocol.Fields.Rows
        })
        {
            if (element.TryGetProperty(collectionField, out JsonElement values)
                && values.ValueKind == JsonValueKind.Array
                && values.GetArrayLength() > 0)
            {
                return false;
            }
        }
        return true;
    }

    private static void AddActionReferences(
        JsonElement element,
        String parentId,
        ICollection<SemanticComponent> children)
    {
        foreach (String field in ActionReferenceFields())
        {
            if (!element.TryGetProperty(field, out JsonElement value))
            {
                continue;
            }
            if (value.ValueKind == JsonValueKind.Array)
            {
                Int32 index = 0;
                foreach (JsonElement item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        AddActionReference(element, parentId, field, item, index, children);
                    }
                    index++;
                }
            }
            else if (value.ValueKind == JsonValueKind.String)
            {
                AddActionReference(element, parentId, field, value, 0, children);
            }
        }
    }

    private static void AddActionReference(
        JsonElement owner,
        String parentId,
        String field,
        JsonElement value,
        Int32 index,
        ICollection<SemanticComponent> children)
    {
        String action = value.GetString() ?? String.Empty;
        if (action.Length == 0)
        {
            return;
        }
        String prefix = parentId.Length > 0 ? parentId : "component";
        String label = field switch
        {
            "clear_action" => "Clear",
            "copy_action" => "Copy",
            "reset_action" => "Reset",
            "submit_action" => "Submit",
            _ => Humanize(action)
        };
        children.Add(new SemanticComponent(
            owner,
            prefix + "." + field + "." + index.ToString(CultureInfo.InvariantCulture),
            LumuiProtocol.ComponentKinds.Button,
            label,
            String.Empty,
            action,
            null,
            Array.Empty<SemanticComponent>(),
            Array.Empty<SemanticOption>(),
            Array.Empty<MediaSourceDescriptor>()));
    }

    private static Boolean IsActionReferenceField(String field) =>
        ActionReferenceFields().Contains(field, StringComparer.Ordinal);

    private static IEnumerable<String> ActionReferenceFields()
    {
        yield return LumuiProtocol.Fields.Actions;
        yield return "clear_action";
        yield return "copy_action";
        yield return "primary_action";
        yield return "reset_action";
        yield return "secondary_actions";
        yield return "submit_action";
    }

    private void RenderComponent(
        SemanticComponent component,
        IReadOnlyDictionary<String, Object?> input,
        ICollection<TerminalRenderLine> lines,
        ICollection<String> outline,
        Int32 depth)
    {
        if (!component.Visible)
        {
            return;
        }
        String indent = new String(' ', Math.Min(depth, 6) * 2);
        String label = component.Label.Length > 0 ? component.Label : Humanize(component.Kind);
        switch (component.Kind)
        {
            case LumuiProtocol.ComponentKinds.Page:
                RenderChildren(component, input, lines, outline, depth);
                return;
            case LumuiProtocol.ComponentKinds.Section:
            case LumuiProtocol.ComponentKinds.Form:
                AddSpace(lines);
                if (component.Label.Length > 0)
                {
                    String heading = component.Label.ToUpperInvariant();
                    lines.Add(new TerminalRenderLine(indent + heading, TerminalLineRole.Eyebrow));
                    outline.Add(new String(' ', depth * 2) + component.Label);
                }
                AddDescription(component, lines, indent);
                RenderChildren(component, input, lines, outline, depth + 1);
                AddSpace(lines);
                return;
            case LumuiProtocol.ComponentKinds.Text:
            case LumuiProtocol.ComponentKinds.RichText:
                RenderText(component, lines, outline, indent, depth);
                return;
            case LumuiProtocol.ComponentKinds.CodeBlock:
                RenderCode(component, lines, indent);
                return;
            case LumuiProtocol.ComponentKinds.Quote:
                lines.Add(new TerminalRenderLine(indent + "“" + PlainText(component.Text), TerminalLineRole.Body));
                String attribution = Text(component.Element, LumuiProtocol.Fields.Attribution);
                if (attribution.Length > 0)
                {
                    lines.Add(new TerminalRenderLine(indent + "  — " + attribution, TerminalLineRole.Muted));
                }
                return;
            case LumuiProtocol.ComponentKinds.Button:
                lines.Add(ControlLine(component, indent + "› " + label, TerminalInteraction.Action));
                return;
            case LumuiProtocol.ComponentKinds.Link:
            case LumuiProtocol.ComponentKinds.Navigation:
            case LumuiProtocol.ComponentKinds.Breadcrumb:
                String destination = component.Target is null ? String.Empty : "  ·  " + component.Target.AbsoluteUri;
                TerminalInteraction linkInteraction =
                    component.Element.TryGetProperty(LumuiProtocol.Fields.Download, out JsonElement download)
                    && download.ValueKind is not JsonValueKind.False and not JsonValueKind.Null
                        ? TerminalInteraction.Download
                        : TerminalInteraction.Navigate;
                lines.Add(ControlLine(
                    component,
                    indent + (linkInteraction == TerminalInteraction.Download ? "↓ " : "↗ ") + label + destination,
                    linkInteraction));
                RenderChildren(component, input, lines, outline, depth + 1);
                return;
            case LumuiProtocol.ComponentKinds.Toggle:
            case LumuiProtocol.ComponentKinds.CheckBox:
            case LumuiProtocol.ComponentKinds.CheckOption:
                Boolean enabled = BooleanValue(CurrentValue(component, input));
                lines.Add(ControlLine(component, indent + (enabled ? "[x] " : "[ ] ") + label, TerminalInteraction.Toggle));
                AddDescription(component, lines, indent + "    ");
                return;
            case LumuiProtocol.ComponentKinds.Choice:
            case LumuiProtocol.ComponentKinds.ComboBox:
            case LumuiProtocol.ComponentKinds.RadioGroup:
            case LumuiProtocol.ComponentKinds.MultiSelect:
                lines.Add(ControlLine(
                    component,
                    indent + "◉ " + label + "  [" + DisplayValue(CurrentValue(component, input), component.Kind) + "]",
                    TerminalInteraction.Choose));
                AddDescription(component, lines, indent + "    ");
                return;
            case LumuiProtocol.ComponentKinds.TextField:
            case LumuiProtocol.ComponentKinds.SearchField:
            case LumuiProtocol.ComponentKinds.NumberField:
            case LumuiProtocol.ComponentKinds.DateField:
            case LumuiProtocol.ComponentKinds.TimeField:
            case LumuiProtocol.ComponentKinds.DateTimeField:
            case LumuiProtocol.ComponentKinds.ColorField:
            case LumuiProtocol.ComponentKinds.OtpField:
            case LumuiProtocol.ComponentKinds.PasswordField:
            case LumuiProtocol.ComponentKinds.TextArea:
            case LumuiProtocol.ComponentKinds.DateRangeField:
                String value = DisplayValue(CurrentValue(component, input), component.Kind);
                lines.Add(ControlLine(component, indent + "✎ " + label + "  [" + value + "]", TerminalInteraction.Edit));
                AddDescription(component, lines, indent + "    ");
                return;
            case LumuiProtocol.ComponentKinds.Slider:
            case LumuiProtocol.ComponentKinds.Stepper:
            case LumuiProtocol.ComponentKinds.Rating:
                String numeric = DisplayValue(CurrentValue(component, input), component.Kind);
                String unit = Text(component.Element, LumuiProtocol.Fields.Unit);
                lines.Add(ControlLine(
                    component,
                    indent + "◀ " + label + "  " + numeric + (unit.Length > 0 ? " " + unit : String.Empty) + " ▶",
                    TerminalInteraction.Edit));
                return;
            case LumuiProtocol.ComponentKinds.Progress:
            case LumuiProtocol.ComponentKinds.Meter:
                RenderMeter(component, input, lines, indent);
                return;
            case LumuiProtocol.ComponentKinds.Audio:
            case LumuiProtocol.ComponentKinds.AudioPlayer:
            case LumuiProtocol.ComponentKinds.Video:
            case LumuiProtocol.ComponentKinds.VideoPlayer:
                RenderMedia(component, lines, indent);
                return;
            case LumuiProtocol.ComponentKinds.Image:
            case LumuiProtocol.ComponentKinds.Graphic:
                String alt = Text(component.Element, LumuiProtocol.Fields.Alt, Text(component.Element, LumuiProtocol.Fields.Caption, label));
                lines.Add(new TerminalRenderLine(indent + "▣ " + alt, TerminalLineRole.Media));
                if (component.Target is not null)
                {
                    lines.Add(ControlLine(component, indent + "  Source  ·  " + component.Target.AbsoluteUri, TerminalInteraction.Navigate));
                }
                return;
            case LumuiProtocol.ComponentKinds.Icon:
                String symbol = Text(
                    component.Element,
                    LumuiProtocol.Fields.Symbol,
                    Text(component.Element, LumuiProtocol.Fields.Icon, label));
                lines.Add(new TerminalRenderLine(indent + symbol, TerminalLineRole.Data));
                return;
            case LumuiProtocol.ComponentKinds.ImageCollection:
            case LumuiProtocol.ComponentKinds.Figure:
                if (component.Label.Length > 0)
                {
                    lines.Add(new TerminalRenderLine(indent + component.Label, TerminalLineRole.Heading));
                }
                RenderChildren(component, input, lines, outline, depth + 1);
                AddDescription(component, lines, indent);
                return;
            case LumuiProtocol.ComponentKinds.List:
            case LumuiProtocol.ComponentKinds.Grid:
            case LumuiProtocol.ComponentKinds.Tree:
            case LumuiProtocol.ComponentKinds.Tabs:
            case LumuiProtocol.ComponentKinds.Menu:
            case LumuiProtocol.ComponentKinds.Toolbar:
            case LumuiProtocol.ComponentKinds.OptionBar:
            case LumuiProtocol.ComponentKinds.ImageOption:
            case LumuiProtocol.ComponentKinds.DetailOption:
                if (component.Label.Length > 0)
                {
                    lines.Add(new TerminalRenderLine(indent + component.Label, TerminalLineRole.Heading));
                    outline.Add(new String(' ', depth * 2) + component.Label);
                }
                RenderChildren(component, input, lines, outline, depth + 1);
                return;
            case LumuiProtocol.ComponentKinds.Table:
                RenderTable(component, lines, indent);
                return;
            case LumuiProtocol.ComponentKinds.Chart:
                RenderChart(component, lines, indent);
                return;
            case LumuiProtocol.ComponentKinds.Alert:
            case LumuiProtocol.ComponentKinds.Error:
                lines.Add(new TerminalRenderLine(indent + "! " + label, TerminalLineRole.Warning));
                AddMessage(component, lines, indent + "  ");
                RenderChildren(component, input, lines, outline, depth + 1);
                return;
            case LumuiProtocol.ComponentKinds.Status:
            case LumuiProtocol.ComponentKinds.Badge:
            case LumuiProtocol.ComponentKinds.Notification:
            case LumuiProtocol.ComponentKinds.Toast:
            case LumuiProtocol.ComponentKinds.Dialog:
            case LumuiProtocol.ComponentKinds.EmptyState:
                lines.Add(new TerminalRenderLine(indent + "• " + label, TerminalLineRole.Data));
                AddMessage(component, lines, indent + "  ");
                RenderChildren(component, input, lines, outline, depth + 1);
                return;
            case LumuiProtocol.ComponentKinds.Activity:
                lines.Add(new TerminalRenderLine(indent + "◌ " + label, TerminalLineRole.Data));
                return;
            case LumuiProtocol.ComponentKinds.ValueDisplay:
                lines.Add(new TerminalRenderLine(
                    indent + (component.Label.Length > 0 ? component.Label + "  " : String.Empty)
                    + DisplayValue(PropertyValue(component.Element, LumuiProtocol.Fields.Value), component.Kind)
                    + Text(component.Element, LumuiProtocol.Fields.Unit),
                    TerminalLineRole.Data));
                AddDescription(component, lines, indent + "  ");
                return;
            case LumuiProtocol.ComponentKinds.Calendar:
            case LumuiProtocol.ComponentKinds.Map:
                lines.Add(new TerminalRenderLine(indent + label + "  " + DescribeFields(component.Element), TerminalLineRole.Data));
                RenderChildren(component, input, lines, outline, depth + 1);
                return;
            case LumuiProtocol.ComponentKinds.Clock:
            {
                String clockValue = DisplayValue(
                    PropertyValue(component.Element, LumuiProtocol.Fields.Value),
                    component.Kind);
                if (clockValue.Length == 0
                    && component.Element.TryGetProperty(LumuiProtocol.Fields.Fallback, out JsonElement fallback)
                    && fallback.ValueKind == JsonValueKind.Object)
                {
                    clockValue = Text(fallback, LumuiProtocol.Fields.Text);
                    if (clockValue.Length == 0)
                    {
                        clockValue = DisplayValue(
                            PropertyValue(fallback, LumuiProtocol.Fields.Value),
                            component.Kind);
                    }
                }
                String timezone = Text(component.Element, "timezone");
                if (clockValue.Length == 0)
                {
                    DateTime displayedAt = DateTime.Now;
                    if (timezone.Length > 0)
                    {
                        try
                        {
                            TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(timezone);
                            displayedAt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
                        }
                        catch (TimeZoneNotFoundException)
                        {
                            displayedAt = DateTime.Now;
                        }
                        catch (InvalidTimeZoneException)
                        {
                            displayedAt = DateTime.Now;
                        }
                    }
                    clockValue = displayedAt.ToString("HH:mm", CultureInfo.CurrentCulture);
                }
                lines.Add(new TerminalRenderLine(
                    indent
                    + label
                    + "  "
                    + clockValue
                    + (timezone.Length > 0 ? "  " + timezone : String.Empty),
                    TerminalLineRole.Data));
                return;
            }
            case LumuiProtocol.ComponentKinds.FilePicker:
            case LumuiProtocol.ComponentKinds.MediaPicker:
            case LumuiProtocol.ComponentKinds.ContactPicker:
            case LumuiProtocol.ComponentKinds.LocationPicker:
            case LumuiProtocol.ComponentKinds.Dialer:
                lines.Add(ControlLine(component, indent + "… " + label, TerminalInteraction.Edit));
                AddDescription(component, lines, indent + "    ");
                return;
            case LumuiProtocol.ComponentKinds.Preview:
                lines.Add(new TerminalRenderLine(
                    indent + "┌ Preview" + (component.Label.Length > 0 ? "  ·  " + component.Label : String.Empty),
                    TerminalLineRole.Heading));
                AddDescription(component, lines, indent + "  ");
                RenderChildren(component, input, lines, outline, depth + 1);
                lines.Add(new TerminalRenderLine(indent + "└", TerminalLineRole.Data));
                return;
            default:
                RenderFallback(component, input, lines, outline, indent, depth);
                return;
        }
    }

    private void RenderText(
        SemanticComponent component,
        ICollection<TerminalRenderLine> lines,
        ICollection<String> outline,
        String indent,
        Int32 depth)
    {
        String value = PlainText(component.Text.Length > 0
            ? component.Text
            : DisplayJsonProperty(component.Element, LumuiProtocol.Fields.Content));
        if (_preferences.BionicReading)
        {
            value = Bionic(value);
        }
        if (value.Length == 0)
        {
            value = component.Label;
        }
        String role = Text(component.Element, LumuiProtocol.Fields.TextRole);
        TerminalLineRole lineRole = role == LumuiProtocol.TextRoles.Heading
            ? TerminalLineRole.Heading
            : TerminalLineRole.Body;
        if (lineRole == TerminalLineRole.Heading)
        {
            outline.Add(new String(' ', depth * 2) + value);
        }
        foreach (String line in Lines(value))
        {
            lines.Add(new TerminalRenderLine(indent + line, lineRole));
        }
    }

    private static void RenderCode(SemanticComponent component, ICollection<TerminalRenderLine> lines, String indent)
    {
        String language = Text(component.Element, "language");
        if (language.Length > 0)
        {
            lines.Add(new TerminalRenderLine(indent + language.ToUpperInvariant(), TerminalLineRole.Eyebrow));
        }
        foreach (String line in Lines(component.Text.Length > 0 ? component.Text : DisplayJsonProperty(component.Element, LumuiProtocol.Fields.Content)))
        {
            lines.Add(new TerminalRenderLine(indent + "│ " + line, TerminalLineRole.Code));
        }
    }

    private static void RenderMeter(
        SemanticComponent component,
        IReadOnlyDictionary<String, Object?> input,
        ICollection<TerminalRenderLine> lines,
        String indent)
    {
        Double min = Number(component.Element, LumuiProtocol.Fields.Min, 0D);
        Double max = Number(component.Element, LumuiProtocol.Fields.Max, 100D);
        Double value = NumberValue(CurrentValue(component, input), min);
        Double ratio = max <= min ? 0D : Math.Clamp((value - min) / (max - min), 0D, 1D);
        Int32 filled = (Int32)Math.Round(ratio * 20D);
        String bar = new String('=', filled) + new String('-', 20 - filled);
        lines.Add(new TerminalRenderLine(
            indent + component.Label + "  [" + bar + "] " + value.ToString("0.##", CultureInfo.InvariantCulture),
            TerminalLineRole.Data));
    }

    private static void RenderMedia(SemanticComponent component, ICollection<TerminalRenderLine> lines, String indent)
    {
        Boolean video = component.Kind is LumuiProtocol.ComponentKinds.Video or LumuiProtocol.ComponentKinds.VideoPlayer;
        String title = component.Label.Length > 0 ? component.Label : video ? "Video" : "Audio";
        String mediaType = video ? "▶ VIDEO" : "♪ AUDIO PLAYER";
        TerminalInteraction interaction = component.MediaSources.Count > 0 ? TerminalInteraction.Media : TerminalInteraction.None;
        lines.Add(new TerminalRenderLine(
            indent + mediaType + "  ·  " + title,
            TerminalLineRole.Media,
            interaction == TerminalInteraction.None ? null : component,
            interaction));
        if (!video)
        {
            Double durationMilliseconds = Math.Max(0D, Number(component.Element, "duration_ms", 0D));
            Double positionMilliseconds = Math.Clamp(
                Number(component.Element, "position_ms", 0D),
                0D,
                durationMilliseconds > 0D ? durationMilliseconds : Double.MaxValue);
            Double ratio = durationMilliseconds > 0D
                ? positionMilliseconds / durationMilliseconds
                : 0D;
            const Int32 barWidth = 20;
            Int32 marker = Math.Clamp(
                (Int32)Math.Round(ratio * (barWidth - 1)),
                0,
                barWidth - 1);
            String bar = new String('━', marker)
                + "●"
                + new String('─', barWidth - marker - 1);
            lines.Add(new TerminalRenderLine(
                indent
                    + "  "
                    + MediaClock(positionMilliseconds)
                    + " ["
                    + bar
                    + "] "
                    + (durationMilliseconds > 0D ? MediaClock(durationMilliseconds) : "--:--")
                    + "  ·  Enter opens controls",
                TerminalLineRole.Media));
        }
        String description = Text(component.Element, LumuiProtocol.Fields.Description);
        if (description.Length > 0)
        {
            lines.Add(new TerminalRenderLine(indent + "  " + description, TerminalLineRole.Body));
        }
        foreach (MediaSourceDescriptor source in component.MediaSources.Take(1))
        {
            lines.Add(new TerminalRenderLine(indent + "  Source  ·  " + source.Uri.AbsoluteUri, TerminalLineRole.Muted));
        }
        foreach ((String Label, String Field) resourceType in new (String Label, String Field)[]
        {
            ("Source", "source"),
            ("License", "license"),
            ("Transcript", "transcript"),
            ("Captions", "captions"),
            ("Audio description", "audio_description")
        })
        {
            foreach (Uri resource in ResourceUris(
                component.Element,
                component.MediaSources.FirstOrDefault()?.Uri,
                resourceType.Field))
            {
                lines.Add(new TerminalRenderLine(
                    indent + "  " + resourceType.Label + "  ·  " + resource.AbsoluteUri,
                    TerminalLineRole.Muted));
            }
        }
    }

    private static String MediaClock(Double milliseconds)
    {
        TimeSpan value = TimeSpan.FromMilliseconds(Math.Max(0D, milliseconds));
        return value.TotalHours >= 1D
            ? value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
            : value.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    private static void RenderTable(SemanticComponent component, ICollection<TerminalRenderLine> lines, String indent)
    {
        if (component.Label.Length > 0)
        {
            lines.Add(new TerminalRenderLine(indent + component.Label, TerminalLineRole.Heading));
        }
        List<String> columns = new List<String>();
        if (component.Element.TryGetProperty(LumuiProtocol.Fields.Columns, out JsonElement columnValues)
            && columnValues.ValueKind == JsonValueKind.Array)
        {
            columns.AddRange(columnValues.EnumerateArray().Select(column =>
                column.ValueKind == JsonValueKind.String
                    ? column.GetString() ?? String.Empty
                    : Text(column, LumuiProtocol.Fields.Label, Text(column, LumuiProtocol.Fields.Id))));
        }
        if (columns.Count > 0)
        {
            lines.Add(new TerminalRenderLine(indent + String.Join("  │  ", columns), TerminalLineRole.Eyebrow));
        }
        if (component.Element.TryGetProperty(LumuiProtocol.Fields.Rows, out JsonElement rows)
            && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement row in rows.EnumerateArray())
            {
                List<String> values = new List<String>();
                if (row.ValueKind == JsonValueKind.Array)
                {
                    values.AddRange(row.EnumerateArray().Select(Display));
                }
                else if (row.ValueKind == JsonValueKind.Object)
                {
                    if (columns.Count > 0)
                    {
                        foreach (String column in columns)
                        {
                            values.Add(row.TryGetProperty(column, out JsonElement value) ? Display(value) : String.Empty);
                        }
                    }
                    else
                    {
                        values.AddRange(row.EnumerateObject().Select(property => property.Name + ": " + Display(property.Value)));
                    }
                }
                else
                {
                    values.Add(Display(row));
                }
                lines.Add(new TerminalRenderLine(indent + String.Join("  │  ", values), TerminalLineRole.Data));
            }
        }
    }

    private static void RenderChart(SemanticComponent component, ICollection<TerminalRenderLine> lines, String indent)
    {
        if (component.Label.Length > 0)
        {
            lines.Add(new TerminalRenderLine(indent + component.Label, TerminalLineRole.Heading));
        }
        JsonElement values = component.Element.TryGetProperty(LumuiProtocol.Fields.Values, out JsonElement found)
            ? found
            : default;
        List<(String Label, Double Value)> points = new List<(String Label, Double Value)>();
        if (values.ValueKind == JsonValueKind.Array)
        {
            Int32 index = 1;
            foreach (JsonElement point in values.EnumerateArray())
            {
                if (point.ValueKind == JsonValueKind.Number && point.TryGetDouble(out Double number))
                {
                    points.Add((index++.ToString(CultureInfo.InvariantCulture), number));
                }
                else if (point.ValueKind == JsonValueKind.Object)
                {
                    points.Add((Text(point, LumuiProtocol.Fields.Label, index++.ToString(CultureInfo.InvariantCulture)), Number(point, LumuiProtocol.Fields.Value, 0D)));
                }
            }
        }
        Double maximum = points.Count == 0 ? 1D : Math.Max(1D, points.Max(point => Math.Abs(point.Value)));
        foreach ((String pointLabel, Double pointValue) in points)
        {
            Int32 size = Math.Clamp((Int32)Math.Round(Math.Abs(pointValue) / maximum * 24D), 1, 24);
            lines.Add(new TerminalRenderLine(
                indent + pointLabel.PadRight(12) + new String('=', size) + " " + pointValue.ToString("0.##", CultureInfo.InvariantCulture),
                TerminalLineRole.Data));
        }
        if (points.Count == 0)
        {
            lines.Add(new TerminalRenderLine(indent + Display(component.Element), TerminalLineRole.Data));
        }
    }

    private void RenderFallback(
        SemanticComponent component,
        IReadOnlyDictionary<String, Object?> input,
        ICollection<TerminalRenderLine> lines,
        ICollection<String> outline,
        String indent,
        Int32 depth)
    {
        String fallback = DisplayJsonProperty(component.Element, LumuiProtocol.Fields.Fallback);
        String value = component.Text.Length > 0
            ? component.Text
            : component.Label.Length > 0
                ? component.Label
                : fallback.Length > 0
                    ? fallback
                    : Humanize(component.Kind);
        TerminalInteraction interaction = component.ActionId.Length > 0
            ? TerminalInteraction.Action
            : component.Target is not null
                ? TerminalInteraction.Navigate
                : TerminalInteraction.None;
        lines.Add(new TerminalRenderLine(
            indent + value,
            TerminalLineRole.Body,
            interaction == TerminalInteraction.None ? null : component,
            interaction));
        RenderChildren(component, input, lines, outline, depth + 1);
    }

    private void RenderChildren(
        SemanticComponent component,
        IReadOnlyDictionary<String, Object?> input,
        ICollection<TerminalRenderLine> lines,
        ICollection<String> outline,
        Int32 depth)
    {
        foreach (SemanticComponent child in component.Children)
        {
            RenderComponent(child, input, lines, outline, depth);
        }
    }

    private static TerminalRenderLine ControlLine(
        SemanticComponent component,
        String text,
        TerminalInteraction interaction)
    {
        String disabled = component.Enabled && !component.ReadOnly ? String.Empty : "  (unavailable)";
        return new TerminalRenderLine(
            text + disabled,
            TerminalLineRole.Control,
            component.Enabled && !component.ReadOnly ? component : null,
            component.Enabled && !component.ReadOnly ? interaction : TerminalInteraction.None);
    }

    private static void AddDescription(SemanticComponent component, ICollection<TerminalRenderLine> lines, String indent)
    {
        String description = Text(component.Element, LumuiProtocol.Fields.Description);
        if (description.Length > 0)
        {
            lines.Add(new TerminalRenderLine(indent + description, TerminalLineRole.Muted));
        }
    }

    private static void AddMessage(SemanticComponent component, ICollection<TerminalRenderLine> lines, String indent)
    {
        String message = Text(component.Element, LumuiProtocol.Fields.Message, Text(component.Element, LumuiProtocol.Fields.Description, component.Text));
        if (message.Length > 0)
        {
            lines.Add(new TerminalRenderLine(indent + message, TerminalLineRole.Body));
        }
    }

    private static void AddSpace(ICollection<TerminalRenderLine> lines)
    {
        if (lines.LastOrDefault()?.Role != TerminalLineRole.Space)
        {
            lines.Add(new TerminalRenderLine(String.Empty, TerminalLineRole.Space));
        }
    }

    private static void TrimSpaces(List<TerminalRenderLine> lines)
    {
        while (lines.Count > 0 && lines[^1].Role == TerminalLineRole.Space)
        {
            lines.RemoveAt(lines.Count - 1);
        }
    }

    private static Object? CurrentValue(SemanticComponent component, IReadOnlyDictionary<String, Object?> input)
    {
        return component.Id.Length > 0 && input.TryGetValue(component.Id, out Object? value)
            ? value
            : InitialValue(component.Element, component.Kind);
    }

    private static Object? InitialValue(JsonElement element, String kind)
    {
        if (!element.TryGetProperty(LumuiProtocol.Fields.Value, out JsonElement value))
        {
            if (kind == LumuiProtocol.ComponentKinds.MultiSelect
                && element.TryGetProperty(LumuiProtocol.Fields.Values, out JsonElement values))
            {
                return JsonValue(values);
            }
            if (kind == LumuiProtocol.ComponentKinds.DateRangeField)
            {
                String start = Text(element, LumuiProtocol.Fields.Start);
                String end = Text(element, LumuiProtocol.Fields.End);
                return start.Length > 0 || end.Length > 0
                    ? start + (end.Length > 0 ? " – " + end : String.Empty)
                    : String.Empty;
            }
            if (kind == LumuiProtocol.ComponentKinds.OptionBar
                && element.TryGetProperty(LumuiProtocol.Fields.Options, out JsonElement options)
                && options.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement option in options.EnumerateArray())
                {
                    if (option.ValueKind == JsonValueKind.Object
                        && option.TryGetProperty("selected", out JsonElement selected)
                        && selected.ValueKind == JsonValueKind.True)
                    {
                        return option.TryGetProperty(LumuiProtocol.Fields.Value, out JsonElement selectedValue)
                            ? JsonValue(selectedValue)
                            : Text(option, LumuiProtocol.Fields.Id);
                    }
                }
            }
            return kind is LumuiProtocol.ComponentKinds.Toggle
                or LumuiProtocol.ComponentKinds.CheckBox
                or LumuiProtocol.ComponentKinds.CheckOption
                ? false
                : String.Empty;
        }
        return JsonValue(value);
    }

    private static Object? JsonValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt64(out Int64 integer) => integer,
            JsonValueKind.Number when value.TryGetDouble(out Double number) => number,
            JsonValueKind.Array => value.EnumerateArray().Select(JsonValue).ToArray(),
            JsonValueKind.Object => value.Clone(),
            _ => null
        };
    }

    private static List<SemanticOption> ParseOptions(JsonElement element)
    {
        List<SemanticOption> options = new List<SemanticOption>();
        if (!element.TryGetProperty(LumuiProtocol.Fields.Options, out JsonElement values)
            || values.ValueKind != JsonValueKind.Array)
        {
            return options;
        }
        foreach (JsonElement option in values.EnumerateArray())
        {
            if (option.ValueKind == JsonValueKind.String)
            {
                String value = option.GetString() ?? String.Empty;
                options.Add(new SemanticOption(value, value, String.Empty));
            }
            else if (option.ValueKind == JsonValueKind.Object)
            {
                Object? value = option.TryGetProperty(LumuiProtocol.Fields.Value, out JsonElement optionValue)
                    ? JsonValue(optionValue)
                    : Text(option, LumuiProtocol.Fields.Id);
                options.Add(new SemanticOption(
                    Text(option, LumuiProtocol.Fields.Label, DisplayValue(value, String.Empty)),
                    value,
                    Text(option, LumuiProtocol.Fields.Description)));
            }
        }
        return options;
    }

    private static List<MediaSourceDescriptor> ParseMediaSources(JsonElement node, Uri baseUri)
    {
        List<MediaSourceDescriptor> sources = new List<MediaSourceDescriptor>();
        HashSet<String> identities = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
        if (node.TryGetProperty(LumuiProtocol.Fields.Source, out JsonElement source))
        {
            AddMediaSources(source, baseUri, Text(node, LumuiProtocol.Fields.Type), sources, identities);
        }
        if (node.TryGetProperty(LumuiProtocol.Fields.Src, out JsonElement directSource))
        {
            AddMediaSources(directSource, baseUri, Text(node, LumuiProtocol.Fields.Type), sources, identities);
        }
        if (node.TryGetProperty("session", out JsonElement session))
        {
            AddMediaSources(session, baseUri, String.Empty, sources, identities);
        }
        if (node.TryGetProperty("variants", out JsonElement variants))
        {
            AddMediaSources(variants, baseUri, String.Empty, sources, identities);
        }
        return sources;
    }

    private static void AddMediaSources(
        JsonElement value,
        Uri baseUri,
        String inheritedMimeType,
        ICollection<MediaSourceDescriptor> sources,
        ISet<String> identities)
    {
        String mimeType = value.ValueKind == JsonValueKind.Object
            ? Text(value, LumuiProtocol.Fields.Type, inheritedMimeType)
            : inheritedMimeType;
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                AddMediaSources(item, baseUri, mimeType, sources, identities);
            }
            return;
        }
        Uri? uri = SourceUriValue(value, baseUri);
        if (uri is not null && identities.Add(uri.AbsoluteUri))
        {
            sources.Add(new MediaSourceDescriptor(uri, mimeType));
        }
        if (value.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        foreach (String field in new[] { LumuiProtocol.Fields.Source, "sources", "variants" })
        {
            if (value.TryGetProperty(field, out JsonElement nested))
            {
                AddMediaSources(nested, baseUri, mimeType, sources, identities);
            }
        }
    }

    private static IReadOnlyList<Uri> ResourceUris(
        JsonElement node,
        Uri? baseUri,
        params String[] fields)
    {
        List<Uri> result = new List<Uri>();
        if (baseUri is null)
        {
            return result;
        }
        foreach (String field in fields)
        {
            if (node.TryGetProperty(field, out JsonElement value))
            {
                AddResourceUris(value, baseUri, result);
            }
        }
        return result.DistinctBy(uri => uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddResourceUris(JsonElement value, Uri baseUri, ICollection<Uri> result)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                AddResourceUris(item, baseUri, result);
            }
            return;
        }
        Uri? uri = SourceUriValue(value, baseUri);
        if (uri is not null)
        {
            result.Add(uri);
        }
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (String field in new[] { LumuiProtocol.Fields.Source, "sources", "variants" })
            {
                if (value.TryGetProperty(field, out JsonElement nested))
                {
                    AddResourceUris(nested, baseUri, result);
                }
            }
        }
    }

    private static Uri? SourceUriValue(JsonElement source, Uri baseUri)
    {
        String value = source.ValueKind switch
        {
            JsonValueKind.String => source.GetString() ?? String.Empty,
            JsonValueKind.Object => Text(
                source,
                LumuiProtocol.Fields.Src,
                Text(
                    source,
                    LumuiProtocol.Fields.Source,
                    Text(source, LumuiProtocol.Fields.Href, Text(source, "url", Text(source, "uri"))))),
            _ => String.Empty
        };
        return ResolveUri(baseUri, value);
    }

    private static Uri? ResolveUri(Uri baseUri, String value)
    {
        if (String.IsNullOrWhiteSpace(value) || !Uri.TryCreate(baseUri, value, out Uri? uri))
        {
            return null;
        }
        Boolean web = uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback;
        Boolean external = uri.Scheme.Equals("mailto", StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals("tel", StringComparison.OrdinalIgnoreCase);
        return web || external ? uri : null;
    }

    private static String TargetValue(JsonElement element)
    {
        foreach (String field in new[]
        {
            LumuiProtocol.Fields.Href,
            LumuiProtocol.Fields.Src,
            LumuiProtocol.Fields.SourceSurface,
            "source_link",
            "cite"
        })
        {
            if (element.TryGetProperty(field, out JsonElement value))
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString() ?? String.Empty;
                }
                if (value.ValueKind == JsonValueKind.Object)
                {
                    return Text(value, LumuiProtocol.Fields.Href, Text(value, LumuiProtocol.Fields.Src));
                }
            }
        }
        return String.Empty;
    }

    private static IEnumerable<String> ChildFields(String kind)
    {
        yield return LumuiProtocol.Fields.Regions;
        yield return LumuiProtocol.Fields.Items;
        yield return LumuiProtocol.Fields.Children;
        yield return LumuiProtocol.Fields.Content;
        yield return LumuiProtocol.Fields.Tabs;
        yield return LumuiProtocol.Fields.Nodes;
        yield return LumuiProtocol.Fields.Actions;
        yield return LumuiProtocol.Fields.Fallback;
        yield return "empty";
        yield return "illustration";
        yield return "table_fallback";
        yield return "clear_action";
        yield return "copy_action";
        yield return "primary_action";
        yield return "reset_action";
        yield return "secondary_actions";
        yield return "submit_action";
        if (kind == LumuiProtocol.ComponentKinds.ImageCollection)
        {
            yield return LumuiProtocol.Fields.Images;
        }
    }

    private static String InferKind(String field) => field switch
    {
        LumuiProtocol.Fields.Regions => LumuiProtocol.ComponentKinds.Section,
        LumuiProtocol.Fields.Tabs => LumuiProtocol.ComponentKinds.Section,
        LumuiProtocol.Fields.Nodes => LumuiProtocol.ComponentKinds.Tree,
        LumuiProtocol.Fields.Images => LumuiProtocol.ComponentKinds.Image,
        LumuiProtocol.Fields.Actions => LumuiProtocol.ComponentKinds.Button,
        "illustration" => LumuiProtocol.ComponentKinds.Icon,
        "table_fallback" => LumuiProtocol.ComponentKinds.Table,
        _ => LumuiProtocol.ComponentKinds.Section
    };

    private static Object? PropertyValue(JsonElement element, String field) =>
        element.TryGetProperty(field, out JsonElement value) ? JsonValue(value) : null;

    private static String DisplayValue(Object? value, String kind)
    {
        if (value is null)
        {
            return String.Empty;
        }
        if (kind == LumuiProtocol.ComponentKinds.PasswordField)
        {
            return new String('•', Math.Clamp(value.ToString()?.Length ?? 0, 0, 16));
        }
        if (value is IEnumerable<Object?> values && value is not String)
        {
            return String.Join(", ", values.Select(item => item?.ToString()));
        }
        if (value is JsonElement element)
        {
            return Display(element);
        }
        return Convert.ToString(value, CultureInfo.CurrentCulture) ?? String.Empty;
    }

    private static Boolean BooleanValue(Object? value) => value switch
    {
        Boolean booleanValue => booleanValue,
        String text when Boolean.TryParse(text, out Boolean parsedBoolean) => parsedBoolean,
        _ => false
    };

    private static Double NumberValue(Object? value, Double fallback) => value switch
    {
        Double doubleValue => doubleValue,
        Single singleValue => singleValue,
        Decimal decimalValue => (Double)decimalValue,
        Int32 integerValue => integerValue,
        Int64 longValue => longValue,
        String text when Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out Double parsedNumber) => parsedNumber,
        _ => fallback
    };

    private static Double Number(JsonElement element, String field, Double fallback)
    {
        return element.TryGetProperty(field, out JsonElement value) && value.TryGetDouble(out Double number)
            ? number
            : fallback;
    }

    private static String DescribeFields(JsonElement element)
    {
        String[] ignored =
        {
            LumuiProtocol.Fields.Id,
            LumuiProtocol.Fields.Kind,
            LumuiProtocol.Fields.Label,
            LumuiProtocol.Fields.Items,
            LumuiProtocol.Fields.Children
        };
        return String.Join(
            "  ·  ",
            element.EnumerateObject()
                .Where(property => !ignored.Contains(property.Name, StringComparer.Ordinal))
                .Take(6)
                .Select(property => Humanize(property.Name) + ": " + Display(property.Value)));
    }

    private static String DisplayJsonProperty(JsonElement element, String field) =>
        element.TryGetProperty(field, out JsonElement value) ? Display(value) : String.Empty;

    private static String Display(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? String.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => String.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.Array => String.Join(", ", value.EnumerateArray().Select(Display)),
            JsonValueKind.Object => String.Join(", ", value.EnumerateObject().Select(property => property.Name + ": " + Display(property.Value))),
            _ => String.Empty
        };
    }

    private static String PlainText(String value)
    {
        StringBuilder output = new StringBuilder(value.Length);
        Boolean tag = false;
        foreach (Char character in value)
        {
            if (character == '<')
            {
                tag = true;
                continue;
            }
            if (character == '>')
            {
                tag = false;
                continue;
            }
            if (!tag)
            {
                output.Append(character);
            }
        }
        return output.ToString()
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase)
            .Replace("&lt;", "<", StringComparison.OrdinalIgnoreCase)
            .Replace("&gt;", ">", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static String Bionic(String value)
    {
        StringBuilder output = new StringBuilder(value.Length);
        Int32 wordPosition = 0;
        foreach (Char character in value)
        {
            if (!Char.IsLetterOrDigit(character))
            {
                output.Append(character);
                wordPosition = 0;
                continue;
            }
            output.Append(wordPosition < 2 ? Char.ToUpperInvariant(character) : character);
            wordPosition++;
        }
        return output.ToString();
    }

    private static IEnumerable<String> Lines(String value)
    {
        String normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return normalized.Split('\n').Select(line => line.TrimEnd());
    }

    private static String Humanize(String value)
    {
        if (value.Length == 0)
        {
            return String.Empty;
        }
        StringBuilder result = new StringBuilder();
        for (Int32 index = 0; index < value.Length; index++)
        {
            Char character = value[index];
            if (character is '_' or '-')
            {
                result.Append(' ');
            }
            else
            {
                if (index > 0 && Char.IsUpper(character) && Char.IsLower(value[index - 1]))
                {
                    result.Append(' ');
                }
                result.Append(index == 0 ? Char.ToUpperInvariant(character) : character);
            }
        }
        return result.ToString();
    }

    private static String Text(JsonElement value, String name, String fallback = "")
    {
        if (value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty(name, out JsonElement property))
        {
            return fallback;
        }
        if (property.ValueKind == JsonValueKind.String)
        {
            return property.GetString() ?? fallback;
        }
        if (property.ValueKind == JsonValueKind.Object
            && property.TryGetProperty(LumuiProtocol.Fields.Fallback, out JsonElement localizedFallback)
            && localizedFallback.ValueKind == JsonValueKind.String)
        {
            return localizedFallback.GetString() ?? fallback;
        }
        if (property.ValueKind == JsonValueKind.Object
            && property.TryGetProperty(LumuiProtocol.Fields.Ref, out JsonElement reference)
            && reference.ValueKind == JsonValueKind.String)
        {
            return reference.GetString() ?? fallback;
        }
        return fallback;
    }
}
