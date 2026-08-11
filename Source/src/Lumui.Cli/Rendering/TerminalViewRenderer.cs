using Lumui.Cli.Configuration;

namespace Lumui.Cli.Rendering;

public sealed class TerminalViewRenderer
{
    private readonly CliPreferences _preferences;

    public TerminalViewRenderer(CliPreferences preferences)
    {
        _preferences = preferences;
    }

    private Boolean LinearOutput =>
        _preferences.TerminalOutput == CliTerminalOutput.Linear
        || _preferences.SimpleReadingView;

    private Boolean ComfortableSpacing =>
        _preferences.TerminalDensity == CliTerminalDensity.Comfortable
        || _preferences.SeniorMode;

    public TerminalViewPage Render(
        TerminalSurfaceDocument document,
        Int32 pageIndex,
        IDictionary<String, Object?> input,
        Boolean guided,
        Int32 guidedStep,
        Int32 availableWidth,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        Double scale = _preferences.PageZoomPercent / 100D * _preferences.TextScalePercent / 100D;
        if (_preferences.SeniorMode)
        {
            scale = Math.Max(scale, 1.15D);
        }
        Int32 width = Math.Max(24, (Int32)Math.Floor((availableWidth - 1) / Math.Max(0.5D, scale)));
        TerminalPage page = document.Pages[Math.Clamp(pageIndex, 0, document.Pages.Count - 1)];
        IReadOnlyList<SemanticComponent> steps = GuidedSteps(page);
        IReadOnlyList<SemanticComponent> visible = guided && steps.Count > 0
            ? new[] { steps[Math.Clamp(guidedStep, 0, steps.Count - 1)] }
            : page.Components.Where(component => component.Visible).ToArray();
        StackBuilder root = new StackBuilder(width);
        List<String> outline = new List<String>();
        BuiltView siteHeader = SiteHeader(document.SiteChrome, width, interact);
        root.Add(siteHeader.View, siteHeader.Height);
        if (ComfortableSpacing)
        {
            root.Space();
        }
        root.Add(Heading(page.Title, width, true), WrappedHeight(page.Title, width));
        if (document.Description.Length > 0 && pageIndex == document.RequestedPageIndex)
        {
            root.Add(TextLabel(document.Description, width, "Base"), WrappedHeight(document.Description, width));
        }
        if (page.Description.Length > 0
            && !page.Description.Equals(document.Description, StringComparison.CurrentCulture))
        {
            root.Add(TextLabel(page.Description, width, "Base"), WrappedHeight(page.Description, width));
        }
        if (ComfortableSpacing)
        {
            root.Space();
        }
        foreach (SemanticComponent component in visible)
        {
            BuiltView built = Build(component, input, width, 0, outline, interact);
            root.Add(built.View, built.Height);
            if (ComfortableSpacing)
            {
                root.Space();
            }
        }
        BuiltView? siteFooter = SiteFooter(document.SiteChrome, width, interact);
        if (siteFooter is not null)
        {
            root.Add(siteFooter.View, siteFooter.Height);
        }
        return new TerminalViewPage(
            root.View,
            Math.Max(1, root.Height),
            outline,
            Math.Max(1, steps.Count));
    }

    private BuiltView SiteHeader(
        TerminalSiteChrome chrome,
        Int32 width,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        Int32 innerWidth = Math.Max(20, width - 2);
        StackBuilder content = new StackBuilder(innerWidth);
        String identityName = chrome.ShortName.Length > 0
            && !chrome.ShortName.Equals(chrome.Name, StringComparison.CurrentCultureIgnoreCase)
                ? chrome.ShortName + "  ·  " + chrome.Name
                : chrome.Name;
        content.Add(
            TextLabel(identityName, innerWidth, "Accent"),
            WrappedHeight(identityName, innerWidth));
        SemanticComponent? identityPreview = chrome.Icon ?? chrome.Logo;
        if (identityPreview is not null
            && identityPreview.MediaSources.FirstOrDefault() is MediaSourceDescriptor identitySource)
        {
            TerminalMediaPreviewView preview = new TerminalMediaPreviewView(
                identitySource,
                false,
                () => _ = interact(identityPreview, TerminalInteraction.Media))
            {
                Width = innerWidth,
                SchemeName = "Base"
            };
            content.Add(preview, 6);
        }
        List<(SemanticComponent Component, String Label, TerminalInteraction Interaction, Boolean Current, Boolean Accent)> identityActions =
            new List<(SemanticComponent Component, String Label, TerminalInteraction Interaction, Boolean Current, Boolean Accent)>();
        if (chrome.Home is not null)
        {
            identityActions.Add((chrome.Home, "⌂ Home", TerminalInteraction.Navigate, false, true));
        }
        if (chrome.Logo is not null)
        {
            identityActions.Add((chrome.Logo, "Logo", TerminalInteraction.Media, false, false));
        }
        if (chrome.Icon is not null)
        {
            identityActions.Add((chrome.Icon, "Favicon", TerminalInteraction.Media, false, false));
        }
        if (identityActions.Count > 0)
        {
            BuiltView identity = ButtonFlow(identityActions, innerWidth, interact);
            content.Add(identity.View, identity.Height);
        }
        if (chrome.Routes.Count > 0)
        {
            content.Add(TextLabel("Menu", innerWidth, "Muted"), 1);
            List<(SemanticComponent Component, String Label, TerminalInteraction Interaction, Boolean Current, Boolean Accent)> routes =
                chrome.Routes
                    .Select(route => (
                        Component: route,
                        Label: route.Label,
                        Interaction: TerminalInteraction.Navigate,
                        Current: CurrentRoute(route),
                        Accent: CurrentRoute(route)))
                    .ToList();
            BuiltView navigation = ButtonFlow(routes, innerWidth, interact);
            content.Add(navigation.View, navigation.Height);
        }
        FrameView frame = new FrameView
        {
            Title = FitButtonLabel(chrome.ShortName.Length > 0 ? chrome.ShortName : "Site header", width),
            Width = width,
            Height = Math.Max(3, content.Height + 2),
            CanFocus = true,
            TabStop = TabBehavior.NoStop,
            SchemeName = "Menu"
        };
        content.View.Width = Dim.Fill();
        content.View.Height = Math.Max(1, content.Height);
        frame.Add(content.View);
        return new BuiltView(frame, Math.Max(3, content.Height + 2));
    }

    private BuiltView? SiteFooter(
        TerminalSiteChrome chrome,
        Int32 width,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        if (!chrome.HasIdentity && chrome.Groups.Count == 0 && chrome.Copyright.Length == 0)
        {
            return null;
        }
        Int32 innerWidth = Math.Max(20, width - 2);
        StackBuilder content = new StackBuilder(innerWidth);
        content.View.SchemeName = "Menu";
        content.Add(TextLabel(chrome.Name, innerWidth, "Accent"), WrappedHeight(chrome.Name, innerWidth));
        foreach (SemanticComponent group in chrome.Groups)
        {
            if (!group.Visible)
            {
                continue;
            }
            if (group.Label.Length > 0)
            {
                content.Add(
                    Heading(group.Label, innerWidth, false),
                    WrappedHeight(group.Label, innerWidth));
            }
            AddDescription(group, content, innerWidth);
            foreach (SemanticComponent link in group.Children.Where(child => child.Visible))
            {
                String label = link.Label.Length > 0 ? link.Label : "Link";
                BuiltView built = Link(link, label, innerWidth, interact);
                content.Add(built.View, built.Height);
            }
            if (ComfortableSpacing)
            {
                content.Space();
            }
        }
        if (chrome.Copyright.Length > 0)
        {
            content.Add(
                TextLabel(chrome.Copyright, innerWidth, "Muted"),
                WrappedHeight(chrome.Copyright, innerWidth));
        }
        FrameView frame = new FrameView
        {
            Title = "Site footer",
            Width = width,
            Height = Math.Max(3, content.Height + 2),
            CanFocus = true,
            TabStop = TabBehavior.NoStop,
            SchemeName = "Menu"
        };
        content.View.Width = Dim.Fill();
        content.View.Height = Math.Max(1, content.Height);
        frame.Add(content.View);
        return new BuiltView(frame, Math.Max(3, content.Height + 2));
    }

    private static BuiltView ButtonFlow(
        IReadOnlyList<(SemanticComponent Component, String Label, TerminalInteraction Interaction, Boolean Current, Boolean Accent)> items,
        Int32 width,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        View flow = new View
        {
            Width = width,
            CanFocus = true,
            TabStop = TabBehavior.NoStop
        };
        Int32 x = 0;
        Int32 y = 0;
        foreach ((SemanticComponent component, String label, TerminalInteraction interaction, Boolean current, Boolean accent) in items)
        {
            String prefix = current ? "● " : String.Empty;
            String fitted = FitButtonLabel(prefix + label, width);
            Int32 buttonWidth = Math.Min(width, Math.Max(7, fitted.Length + 4));
            if (x > 0 && x + buttonWidth > width)
            {
                x = 0;
                y++;
            }
            Button button = new CliButton
            {
                Id = component.Id,
                Text = fitted,
                X = x,
                Y = y,
                Width = buttonWidth,
                Enabled = !current && component.Enabled && !component.ReadOnly,
                SchemeName = accent ? "Accent" : "Base"
            };
            if (!current)
            {
                button.Accepting += (_, _) => _ = interact(component, interaction);
            }
            flow.Add(button);
            x += buttonWidth + 1;
        }
        flow.Height = Math.Max(1, y + 1);
        return new BuiltView(flow, Math.Max(1, y + 1));
    }

    private static Boolean CurrentRoute(SemanticComponent component) =>
        component.Element.TryGetProperty(LumuiProtocol.Fields.Current, out JsonElement current)
        && current.ValueKind == JsonValueKind.True;

    private static String FitButtonLabel(String value, Int32 width)
    {
        Int32 maximum = Math.Max(3, width - 4);
        return value.Length <= maximum
            ? value
            : value[..Math.Max(1, maximum - 1)] + "…";
    }

    public IReadOnlyList<SemanticComponent> GuidedSteps(TerminalPage page)
    {
        SemanticComponent[] pageComponents = page.Components.Where(component => component.Visible).ToArray();
        if (pageComponents.Length != 1)
        {
            return pageComponents;
        }
        SemanticComponent root = pageComponents[0];
        while (root.Children.Count == 1
            && root.Kind is LumuiProtocol.ComponentKinds.Page or LumuiProtocol.ComponentKinds.Section)
        {
            root = root.Children[0];
        }
        if (root.Children.Count > 1
            && root.Kind is LumuiProtocol.ComponentKinds.Page
                or LumuiProtocol.ComponentKinds.Section
                or LumuiProtocol.ComponentKinds.Form)
        {
            return root.Children.Where(component => component.Visible).ToArray();
        }
        return pageComponents;
    }

    private BuiltView Build(
        SemanticComponent component,
        IDictionary<String, Object?> input,
        Int32 width,
        Int32 depth,
        ICollection<String> outline,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        BuiltView built = BuildCore(component, input, width, depth, outline, interact);
        if (component.Visible
            && component.Enabled)
        {
            built.View.CanFocus = true;
            built.View.TabStop = TabBehavior.TabStop;
            if (component.Id.Length > 0 && String.IsNullOrWhiteSpace(built.View.Id))
            {
                built.View.Id = component.Id;
            }
        }
        return built;
    }

    private BuiltView BuildCore(
        SemanticComponent component,
        IDictionary<String, Object?> input,
        Int32 width,
        Int32 depth,
        ICollection<String> outline,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        if (!component.Visible)
        {
            return new BuiltView(new View { Visible = false }, 0);
        }
        String label = component.Label.Length > 0 ? component.Label : Humanize(component.Kind);
        switch (component.Kind)
        {
            case LumuiProtocol.ComponentKinds.Page:
                return Children(component, input, width, depth, outline, interact);
            case LumuiProtocol.ComponentKinds.Section:
                return Section(component, input, width, depth, outline, interact, false);
            case LumuiProtocol.ComponentKinds.Form:
                return Section(component, input, width, depth, outline, interact, true);
            case LumuiProtocol.ComponentKinds.Text:
            case LumuiProtocol.ComponentKinds.RichText:
                return TextComponent(component, width, outline, depth);
            case LumuiProtocol.ComponentKinds.CodeBlock:
                return Code(component, width, interact);
            case LumuiProtocol.ComponentKinds.Quote:
                return Quote(component, width, interact);
            case LumuiProtocol.ComponentKinds.Button:
                return ActionButton(component, label, width, TerminalInteraction.Action, interact, true);
            case LumuiProtocol.ComponentKinds.Link:
                return Link(component, label, width, interact);
            case LumuiProtocol.ComponentKinds.Navigation:
                return component.Target is not null || component.ActionId.Length > 0
                    ? Link(component, label, width, interact)
                    : Navigation(component, width);
            case LumuiProtocol.ComponentKinds.Breadcrumb:
                return Collection(component, input, width, depth, outline, interact);
            case LumuiProtocol.ComponentKinds.Toggle:
            case LumuiProtocol.ComponentKinds.CheckBox:
            case LumuiProtocol.ComponentKinds.CheckOption:
                return Toggle(component, input, width, interact);
            case LumuiProtocol.ComponentKinds.Choice:
            case LumuiProtocol.ComponentKinds.RadioGroup:
                return Options(component, input, width, false, interact);
            case LumuiProtocol.ComponentKinds.MultiSelect:
                return Options(component, input, width, true, interact);
            case LumuiProtocol.ComponentKinds.ComboBox:
                return ChoiceButton(component, input, width, interact);
            case LumuiProtocol.ComponentKinds.ImageOption:
            case LumuiProtocol.ComponentKinds.DetailOption:
                return Detail(component, width, interact);
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
                return Input(component, input, width, interact);
            case LumuiProtocol.ComponentKinds.FilePicker:
            case LumuiProtocol.ComponentKinds.MediaPicker:
            case LumuiProtocol.ComponentKinds.ContactPicker:
            case LumuiProtocol.ComponentKinds.LocationPicker:
                return Picker(component, input, width, interact);
            case LumuiProtocol.ComponentKinds.Dialer:
                return Dialer(component, input, width, depth, outline, interact);
            case LumuiProtocol.ComponentKinds.Slider:
            case LumuiProtocol.ComponentKinds.Stepper:
            case LumuiProtocol.ComponentKinds.Rating:
                return Stepper(component, input, width, interact);
            case LumuiProtocol.ComponentKinds.Progress:
            case LumuiProtocol.ComponentKinds.Meter:
                return Meter(component, input, width);
            case LumuiProtocol.ComponentKinds.Audio:
            case LumuiProtocol.ComponentKinds.AudioPlayer:
            case LumuiProtocol.ComponentKinds.Video:
            case LumuiProtocol.ComponentKinds.VideoPlayer:
                return Media(component, width, interact, _preferences.UseUnicode);
            case LumuiProtocol.ComponentKinds.Image:
            case LumuiProtocol.ComponentKinds.Graphic:
                return Image(component, input, width, depth, outline, interact);
            case LumuiProtocol.ComponentKinds.Icon:
                return Icon(component, width, interact, _preferences.UseUnicode);
            case LumuiProtocol.ComponentKinds.ImageCollection:
                return Grid(component, input, width, depth, outline, interact);
            case LumuiProtocol.ComponentKinds.Figure:
                return Figure(component, input, width, depth, outline, interact);
            case LumuiProtocol.ComponentKinds.Grid:
                return Grid(component, input, width, depth, outline, interact);
            case LumuiProtocol.ComponentKinds.Tabs:
                return LinearOutput
                    ? Collection(component, input, width, depth, outline, interact)
                    : TabSet(component, input, width, depth, outline, interact);
            case LumuiProtocol.ComponentKinds.List:
            case LumuiProtocol.ComponentKinds.Tree:
            case LumuiProtocol.ComponentKinds.Menu:
            case LumuiProtocol.ComponentKinds.Toolbar:
                return Collection(component, input, width, depth, outline, interact);
            case LumuiProtocol.ComponentKinds.OptionBar:
                return Options(component, input, width, false, interact);
            case LumuiProtocol.ComponentKinds.Table:
                return Table(component, input, width, depth, outline, interact);
            case LumuiProtocol.ComponentKinds.Chart:
                return Chart(component, input, width, depth, outline, interact);
            case LumuiProtocol.ComponentKinds.Alert:
            case LumuiProtocol.ComponentKinds.Error:
                return Message(component, input, width, depth, outline, interact, true);
            case LumuiProtocol.ComponentKinds.Status:
            case LumuiProtocol.ComponentKinds.Badge:
            case LumuiProtocol.ComponentKinds.Notification:
            case LumuiProtocol.ComponentKinds.Toast:
            case LumuiProtocol.ComponentKinds.Dialog:
            case LumuiProtocol.ComponentKinds.EmptyState:
            case LumuiProtocol.ComponentKinds.Activity:
                return Message(component, input, width, depth, outline, interact, false);
            case LumuiProtocol.ComponentKinds.ValueDisplay:
                return Data(component, input, width, depth, outline, interact);
            case LumuiProtocol.ComponentKinds.Calendar:
                return Calendar(component, input, width, depth, outline, interact);
            case LumuiProtocol.ComponentKinds.Clock:
                return Clock(component, input, width, depth, outline, interact);
            case LumuiProtocol.ComponentKinds.Map:
                return Map(component, input, width, depth, outline, interact);
            case LumuiProtocol.ComponentKinds.Preview:
                return Preview(component, input, width, depth, outline, interact);
            default:
                return Fallback(component, input, width, depth, outline, interact);
        }
    }

    private BuiltView Section(
        SemanticComponent component,
        IDictionary<String, Object?> input,
        Int32 width,
        Int32 depth,
        ICollection<String> outline,
        Func<SemanticComponent, TerminalInteraction, Task> interact,
        Boolean framed)
    {
        Int32 innerWidth = framed ? Math.Max(20, width - 2) : width;
        StackBuilder content = new StackBuilder(innerWidth);
        if (!framed && component.Label.Length > 0)
        {
            content.Add(
                Heading(component.Label, innerWidth, false),
                WrappedHeight(component.Label, innerWidth));
        }
        if (component.Label.Length > 0)
        {
            outline.Add(new String(' ', Math.Min(depth, 3) * 2) + component.Label);
        }
        AddDescription(component, content, innerWidth);
        foreach (SemanticComponent child in component.Children.Where(child => !IsRedundantSectionText(component, child)))
        {
            BuiltView built = Build(child, input, innerWidth, depth + 1, outline, interact);
            content.Add(built.View, built.Height);
            if (ComfortableSpacing)
            {
                content.Space();
            }
        }
        if (!framed)
        {
            return new BuiltView(content.View, Math.Max(1, content.Height));
        }
        FrameView frame = new FrameView
        {
            Title = component.Label.Length > 0 ? component.Label : "Form",
            Width = width,
            Height = Math.Max(3, content.Height + 2),
            CanFocus = true,
            TabStop = TabBehavior.NoStop
        };
        content.View.Width = Dim.Fill();
        content.View.Height = Math.Max(1, content.Height);
        frame.Add(content.View);
        return new BuiltView(frame, Math.Max(3, content.Height + 2));
    }

    private BuiltView Children(
        SemanticComponent component,
        IDictionary<String, Object?> input,
        Int32 width,
        Int32 depth,
        ICollection<String> outline,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        StackBuilder stack = new StackBuilder(width);
        foreach (SemanticComponent child in component.Children)
        {
            BuiltView built = Build(child, input, width, depth, outline, interact);
            stack.Add(built.View, built.Height);
            if (ComfortableSpacing)
            {
                stack.Space();
            }
        }
        return new BuiltView(stack.View, Math.Max(1, stack.Height));
    }

    private BuiltView TextComponent(
        SemanticComponent component,
        Int32 width,
        ICollection<String> outline,
        Int32 depth)
    {
        String value = PlainText(component.Text.Length > 0 ? component.Text : component.Label);
        if (_preferences.BionicReading)
        {
            value = Bionic(value);
        }
        if (component.Label.Length > 0 && component.Text.Length > 0)
        {
            outline.Add(new String(' ', Math.Min(depth, 3) * 2) + component.Label);
            StackBuilder stack = new StackBuilder(width);
            stack.Add(Heading(component.Label, width, false), WrappedHeight(component.Label, width));
            stack.Add(TextLabel(value, width, "Base"), WrappedHeight(value, width));
            return new BuiltView(stack.View, stack.Height);
        }
        return new BuiltView(TextLabel(value, width, "Base"), WrappedHeight(value, width));
    }

    private static BuiltView Code(
        SemanticComponent component,
        Int32 width,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        String value = component.Text.Length > 0
            ? component.Text
            : Text(component.Element, LumuiProtocol.Fields.Value);
        ObservableCollection<String> lines = new ObservableCollection<String>(NormalizeLines(value));
        ListView code = new ListView
        {
            Width = width,
            Height = Math.Clamp(lines.Count, 3, 14),
            CanFocus = true,
            TabStop = TabBehavior.TabStop,
            SchemeName = "Code"
        };
        code.SetSource(lines);
        code.HorizontalScrollBar.Visible = true;
        code.VerticalScrollBar.Visible = lines.Count > 14;
        StackBuilder stack = new StackBuilder(width);
        stack.Add(code, Math.Clamp(lines.Count, 3, 14));
        foreach (SemanticComponent child in component.Children)
        {
            BuiltView action = ActionButton(
                child,
                child.Label.Length > 0 ? child.Label : "Copy",
                width,
                TerminalInteraction.Action,
                interact,
                false);
            stack.Add(action.View, action.Height);
        }
        return new BuiltView(stack.View, Math.Max(1, stack.Height));
    }

    private static BuiltView Quote(
        SemanticComponent component,
        Int32 width,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        String attribution = Text(component.Element, LumuiProtocol.Fields.Attribution);
        String value = "> " + PlainText(component.Text)
            + (attribution.Length > 0 ? Environment.NewLine + "  — " + attribution : String.Empty);
        StackBuilder stack = new StackBuilder(width);
        stack.Add(TextLabel(value, width, "Muted"), WrappedHeight(value, width));
        AddFields(component, stack, width, "language");
        if (component.Target is not null)
        {
            SemanticComponent cite = ResourceComponent(component, "Citation", component.Target, 0);
            BuiltView link = Link(cite, "Citation", width, interact);
            stack.Add(link.View, link.Height);
        }
        return new BuiltView(stack.View, Math.Max(1, stack.Height));
    }

    private static BuiltView ActionButton(
        SemanticComponent component,
        String label,
        Int32 width,
        TerminalInteraction interaction,
        Func<SemanticComponent, TerminalInteraction, Task> interact,
        Boolean primary)
    {
        Button button = new CliButton
        {
            Id = component.Id,
            Text = label,
            Width = Math.Min(width, Math.Max(8, label.Length + 4)),
            Enabled = component.Enabled && !component.ReadOnly,
            SchemeName = primary ? "Accent" : "Base"
        };
        button.Accepting += (_, _) => _ = interact(component, interaction);
        return new BuiltView(button, 1);
    }

    private static BuiltView Link(
        SemanticComponent component,
        String label,
        Int32 width,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        Boolean download = component.Element.TryGetProperty(LumuiProtocol.Fields.Download, out JsonElement value)
            && value.ValueKind is not JsonValueKind.False and not JsonValueKind.Null;
        TerminalInteraction interaction = download
            ? TerminalInteraction.Download
            : TerminalInteraction.Navigate;
        StackBuilder stack = new StackBuilder(width);
        String marker = download ? "↓ " : component.Target is not null ? "↗ " : "› ";
        String address = Text(
            component.Element,
            LumuiProtocol.Fields.Href,
            component.Target?.AbsoluteUri ?? component.Text);
        String caption = Wrap(marker + label, width);
        String destination = address.Length > 0 ? Wrap("URL  " + address, width) : String.Empty;
        String linkText = destination.Length > 0
            ? caption + Environment.NewLine + destination
            : caption;
        Int32 linkHeight = WrappedHeight(marker + label, width)
            + (address.Length > 0 ? WrappedHeight("URL  " + address, width) : 0);
        Button linkButton = new CliButton
        {
            Id = component.Id,
            Text = linkText,
            Width = width,
            Height = Math.Max(1, linkHeight),
            NoDecorations = true,
            NoPadding = true,
            Enabled = component.Enabled && !component.ReadOnly,
            SchemeName = "Accent"
        };
        linkButton.Accepting += (_, _) => _ = interact(component, interaction);
        stack.Add(linkButton, Math.Max(1, linkHeight));
        String description = Text(component.Element, LumuiProtocol.Fields.Description);
        if (description.Length > 0)
        {
            stack.Add(TextLabel(description, width, "Muted"), WrappedHeight(description, width));
        }
        return new BuiltView(stack.View, stack.Height);
    }

    private static BuiltView Detail(
        SemanticComponent component,
        Int32 width,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        StackBuilder stack = new StackBuilder(width);
        String marker = component.Kind == LumuiProtocol.ComponentKinds.ImageOption ? "▣ " : "• ";
        String label = component.Label.Length > 0 ? component.Label : Humanize(component.Kind);
        if (component.ActionId.Length > 0 || component.Target is not null)
        {
            BuiltView action = ActionButton(
                component,
                marker + label,
                width,
                component.ActionId.Length > 0 ? TerminalInteraction.Action : TerminalInteraction.Navigate,
                interact,
                false);
            stack.Add(action.View, action.Height);
        }
        else
        {
            String heading = marker + label;
            stack.Add(TextLabel(heading, width, "Base"), WrappedHeight(heading, width));
        }
        List<String> details = new List<String>();
        foreach (String field in new[]
        {
            LumuiProtocol.Fields.Description,
            LumuiProtocol.Fields.Text,
            LumuiProtocol.Fields.Value
        })
        {
            if (component.Element.TryGetProperty(field, out JsonElement detail))
            {
                String displayed = Display(detail);
                if (displayed.Length > 0 && !details.Contains(displayed, StringComparer.CurrentCulture))
                {
                    details.Add(displayed);
                }
            }
        }
        if (details.Count > 0)
        {
            String detailText = String.Join("  ·  ", details);
            stack.Add(TextLabel(detailText, width, "Muted"), WrappedHeight(detailText, width));
        }
        if (component.MediaSources.Count > 0)
        {
            String source = "Source  " + component.MediaSources[0].Uri.AbsoluteUri;
            stack.Add(TextLabel(source, width, "Muted"), WrappedHeight(source, width));
            BuiltView preview = ActionButton(component, "View image", width, TerminalInteraction.Media, interact, false);
            stack.Add(preview.View, preview.Height);
        }
        return new BuiltView(stack.View, Math.Max(1, stack.Height));
    }

    private static BuiltView Navigation(SemanticComponent component, Int32 width)
    {
        StackBuilder stack = new StackBuilder(width);
        String summary = Text(component.Element, LumuiProtocol.Fields.RouteSummary, component.Label);
        if (summary.Length > 0)
        {
            stack.Add(TextLabel("➜ " + summary, width, "Accent"), WrappedHeight(summary, Math.Max(1, width - 2)));
        }
        if (component.Element.TryGetProperty("destination", out JsonElement destination))
        {
            String destinationText = destination.ValueKind == JsonValueKind.Object
                ? Text(destination, LumuiProtocol.Fields.Label, Display(destination))
                : Display(destination);
            if (destinationText.Length > 0)
            {
                stack.Add(TextLabel("Destination  " + destinationText, width, "Base"), WrappedHeight(destinationText, Math.Max(1, width - 13)));
            }
        }
        if (component.Element.TryGetProperty("current_step", out JsonElement currentStep))
        {
            String instruction = currentStep.ValueKind == JsonValueKind.Object
                ? Text(currentStep, "instruction", Display(currentStep))
                : Display(currentStep);
            if (instruction.Length > 0)
            {
                stack.Add(TextLabel("Now  " + instruction, width, "Base"), WrappedHeight(instruction, Math.Max(1, width - 5)));
            }
        }
        if (component.Element.TryGetProperty("maneuvers", out JsonElement maneuvers)
            && maneuvers.ValueKind == JsonValueKind.Array)
        {
            Int32 index = 1;
            foreach (JsonElement maneuver in maneuvers.EnumerateArray())
            {
                String instruction = maneuver.ValueKind == JsonValueKind.Object
                    ? Text(maneuver, "instruction", Display(maneuver))
                    : Display(maneuver);
                String line = index.ToString(CultureInfo.InvariantCulture) + ". " + instruction;
                stack.Add(TextLabel(line, width, "Muted"), WrappedHeight(line, width));
                index++;
            }
        }
        foreach (String field in new[] { "distance_remaining", "eta" })
        {
            if (component.Element.TryGetProperty(field, out JsonElement value))
            {
                String line = Humanize(field) + "  " + Display(value);
                stack.Add(TextLabel(line, width, "Muted"), WrappedHeight(line, width));
            }
        }
        return new BuiltView(stack.View, Math.Max(1, stack.Height));
    }

    private static BuiltView Toggle(
        SemanticComponent component,
        IDictionary<String, Object?> input,
        Int32 width,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        StackBuilder stack = new StackBuilder(width);
        CheckBox box = new CheckBox
        {
            Id = component.Id,
            Text = Wrap(component.Label, Math.Max(8, width - 4)),
            Value = BooleanValue(Current(component, input)) ? CheckState.Checked : CheckState.UnChecked,
            Width = width,
            CanFocus = true,
            TabStop = TabBehavior.TabStop,
            Enabled = component.Enabled && !component.ReadOnly && component.Id.Length > 0
        };
        box.ValueChanged += (_, _) =>
        {
            if (component.Id.Length == 0)
            {
                return;
            }
            input[component.Id] = box.Value == CheckState.Checked;
            if (component.ActionId.Length > 0)
            {
                _ = interact(component, TerminalInteraction.Action);
            }
        };
        stack.Add(box, WrappedHeight(component.Label, Math.Max(8, width - 4)));
        AddDescription(component, stack, width);
        return new BuiltView(stack.View, stack.Height);
    }

    private static BuiltView Options(
        SemanticComponent component,
        IDictionary<String, Object?> input,
        Int32 width,
        Boolean multiple,
        Func<SemanticComponent, TerminalInteraction, Task> interact,
        Boolean showLabel = true)
    {
        StackBuilder stack = new StackBuilder(width);
        if (showLabel && component.Label.Length > 0)
        {
            stack.Add(TextLabel(component.Label, width, "Base"), WrappedHeight(component.Label, width));
        }
        List<CheckBox> boxes = new List<CheckBox>();
        Boolean synchronizing = false;
        HashSet<String> selected = SelectedValues(Current(component, input));
        for (Int32 index = 0; index < component.Options.Count; index++)
        {
            SemanticOption option = component.Options[index];
            String identity = ValueIdentity(option.Value);
            CheckBox box = new CheckBox
            {
                Id = component.Id + "-option-" + index.ToString(CultureInfo.InvariantCulture),
                Text = Wrap(option.Label, Math.Max(8, width - 4)),
                Value = selected.Contains(identity) ? CheckState.Checked : CheckState.UnChecked,
                RadioStyle = !multiple,
                Width = width,
                CanFocus = true,
                TabStop = TabBehavior.TabStop,
                Enabled = component.Enabled && !component.ReadOnly && component.Id.Length > 0
            };
            boxes.Add(box);
            box.ValueChanged += (_, _) =>
            {
                if (synchronizing || component.Id.Length == 0)
                {
                    return;
                }
                synchronizing = true;
                if (multiple)
                {
                    List<Object?> values = new List<Object?>();
                    for (Int32 optionIndex = 0; optionIndex < boxes.Count; optionIndex++)
                    {
                        if (boxes[optionIndex].Value == CheckState.Checked)
                        {
                            values.Add(component.Options[optionIndex].Value);
                        }
                    }
                    input[component.Id] = values;
                }
                else if (box.Value == CheckState.Checked)
                {
                    foreach (CheckBox other in boxes)
                    {
                        if (!ReferenceEquals(other, box))
                        {
                            other.Value = CheckState.UnChecked;
                        }
                    }
                    input[component.Id] = option.Value;
                }
                synchronizing = false;
                if (component.ActionId.Length > 0)
                {
                    _ = interact(component, TerminalInteraction.Action);
                }
            };
            stack.Add(box, WrappedHeight(option.Label, Math.Max(8, width - 4)));
            if (option.Description.Length > 0)
            {
                stack.Add(TextLabel(option.Description, width, "Muted"), WrappedHeight(option.Description, width));
            }
        }
        AddDescription(component, stack, width);
        AddFields(component, stack, width, "min_selected", "max_selected", "allow_empty", "editable", "filter_mode");
        return new BuiltView(stack.View, Math.Max(1, stack.Height));
    }

    private static BuiltView ChoiceButton(
        SemanticComponent component,
        IDictionary<String, Object?> input,
        Int32 width,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        String current = DisplayValue(Current(component, input), component.Kind);
        String label = component.Label + (current.Length > 0 ? ": " + current : String.Empty);
        return ActionButton(component, label, width, TerminalInteraction.Choose, interact, false);
    }

    private static BuiltView Input(
        SemanticComponent component,
        IDictionary<String, Object?> input,
        Int32 width,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        StackBuilder stack = new StackBuilder(width);
        if (component.Label.Length > 0)
        {
            stack.Add(TextLabel(component.Label, width, "Base"), WrappedHeight(component.Label, width));
        }
        TextField field = new TextField
        {
            Id = component.Id,
            Text = DisplayValue(Current(component, input), String.Empty),
            Secret = component.Kind == LumuiProtocol.ComponentKinds.PasswordField,
            Width = width,
            CanFocus = true,
            TabStop = TabBehavior.TabStop,
            Enabled = component.Enabled && !component.ReadOnly && component.Id.Length > 0
        };
        field.TextChanged += (_, _) =>
        {
            if (component.Id.Length > 0)
            {
                input[component.Id] = InputValue(component.Kind, field.Text);
            }
        };
        field.Accepted += (_, _) =>
        {
            if (component.ActionId.Length > 0)
            {
                _ = interact(component, TerminalInteraction.Action);
            }
        };
        stack.Add(field, 1);
        AddDescription(component, stack, width);
        AddFields(
            component,
            stack,
            width,
            "placeholder",
            "content_type",
            "keyboard",
            "autocomplete",
            "min_length",
            "max_length",
            "pattern",
            "suggestions",
            "result_count",
            "min",
            "max",
            "step",
            "unit",
            "format",
            "timezone",
            "palette",
            "allow_custom",
            "allow_negative",
            "allow_open_end",
            "allow_reveal",
            "length",
            "step_minutes",
            "precision",
            "rules",
            "content_extent");
        foreach (SemanticComponent child in component.Children)
        {
            BuiltView action = ActionButton(
                child,
                child.Label.Length > 0 ? child.Label : Humanize(child.ActionId),
                width,
                TerminalInteraction.Action,
                interact,
                false);
            stack.Add(action.View, action.Height);
        }
        return new BuiltView(stack.View, stack.Height);
    }

    private static BuiltView Picker(
        SemanticComponent component,
        IDictionary<String, Object?> input,
        Int32 width,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        StackBuilder stack = new StackBuilder(width);
        AddDescription(component, stack, width);
        String current = DisplayValue(Current(component, input), component.Kind);
        if (current.Length > 0)
        {
            String selected = "Selected  " + current;
            stack.Add(TextLabel(selected, width, "Base"), WrappedHeight(selected, width));
        }
        List<String> details = new List<String>();
        foreach (String field in new[]
        {
            "accept",
            "media_types",
            "selection_mode",
            "mode",
            "intent",
            "target",
            "copy_policy",
            "requires_capabilities"
        })
        {
            if (component.Element.TryGetProperty(field, out JsonElement value))
            {
                String displayed = Display(value);
                if (displayed.Length > 0)
                {
                    details.Add(Humanize(field) + "  " + displayed);
                }
            }
        }
        if (details.Count > 0)
        {
            String detailText = String.Join(Environment.NewLine, details);
            stack.Add(TextLabel(detailText, width, "Muted"), WrappedHeight(detailText, width));
        }
        String noun = component.Kind switch
        {
            LumuiProtocol.ComponentKinds.FilePicker => "file",
            LumuiProtocol.ComponentKinds.MediaPicker => "media",
            LumuiProtocol.ComponentKinds.ContactPicker => "contact",
            LumuiProtocol.ComponentKinds.LocationPicker => "location",
            _ => "value"
        };
        BuiltView choose = ActionButton(
            component,
            component.Label.Length > 0 ? component.Label : "Choose " + noun,
            width,
            TerminalInteraction.Edit,
            interact,
            true);
        stack.Add(choose.View, choose.Height);
        return new BuiltView(stack.View, Math.Max(1, stack.Height));
    }

    private BuiltView Dialer(
        SemanticComponent component,
        IDictionary<String, Object?> input,
        Int32 width,
        Int32 depth,
        ICollection<String> outline,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        StackBuilder stack = new StackBuilder(width);
        String title = component.Label.Length > 0 ? component.Label : "Dialer";
        stack.Add(Heading(title, width, false), WrappedHeight(title, width));
        foreach (String field in new[] { "number", "contact", "call_state", "mode" })
        {
            if (component.Element.TryGetProperty(field, out JsonElement value))
            {
                String line = Humanize(field) + "  " + Display(value);
                stack.Add(TextLabel(line, width, field == "call_state" ? "Accent" : "Base"), WrappedHeight(line, width));
            }
        }
        foreach (SemanticComponent child in component.Children)
        {
            BuiltView built = Build(child, input, Math.Max(20, width - 2), depth + 1, outline, interact);
            built.View.X = 2;
            stack.Add(built.View, built.Height);
        }
        if (component.ActionId.Length > 0)
        {
            BuiltView action = ActionButton(component, "Call", width, TerminalInteraction.Action, interact, true);
            stack.Add(action.View, action.Height);
        }
        return new BuiltView(stack.View, Math.Max(1, stack.Height));
    }

    private static BuiltView Stepper(
        SemanticComponent component,
        IDictionary<String, Object?> input,
        Int32 width,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        StackBuilder stack = new StackBuilder(width);
        if (component.Label.Length > 0)
        {
            stack.Add(TextLabel(component.Label, width, "Base"), WrappedHeight(component.Label, width));
        }
        View row = new View
        {
            Width = width,
            Height = 1,
            CanFocus = true,
            TabStop = TabBehavior.NoStop
        };
        Boolean editable = component.Enabled && !component.ReadOnly && component.Id.Length > 0;
        Button decrease = new CliButton { Text = "−", X = 0, Y = 0, Width = 5, Enabled = editable };
        TextField value = new TextField
        {
            Id = component.Id,
            Text = DisplayValue(Current(component, input), String.Empty),
            X = 6,
            Y = 0,
            Width = Math.Max(8, Math.Min(18, width - 13)),
            CanFocus = true,
            TabStop = TabBehavior.TabStop,
            Enabled = editable
        };
        Button increase = new CliButton
        {
            Text = "+",
            X = Pos.Right(value) + 1,
            Y = 0,
            Width = 5,
            Enabled = editable
        };
        Double step = Number(component.Element, "step", 1D);
        void Change(Double direction)
        {
            Double current = NumberValue(Current(component, input), 0D);
            Double minimum = Number(component.Element, LumuiProtocol.Fields.Min, Double.MinValue);
            Double maximum = Number(component.Element, LumuiProtocol.Fields.Max, Double.MaxValue);
            Double next = Math.Clamp(current + direction * step, minimum, maximum);
            input[component.Id] = next;
            value.Text = next.ToString("0.##", CultureInfo.CurrentCulture);
            if (component.ActionId.Length > 0)
            {
                _ = interact(component, TerminalInteraction.Action);
            }
        }
        decrease.Accepting += (_, _) => Change(-1D);
        increase.Accepting += (_, _) => Change(1D);
        value.TextChanged += (_, _) =>
        {
            if (Double.TryParse(value.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out Double parsed))
            {
                input[component.Id] = parsed;
            }
        };
        row.Add(decrease, value, increase);
        stack.Add(row, 1);
        AddFields(component, stack, width, "min", "max", "step", "unit", "display_value", "marks");
        return new BuiltView(stack.View, stack.Height);
    }

    private BuiltView Meter(
        SemanticComponent component,
        IDictionary<String, Object?> input,
        Int32 width)
    {
        if (component.Element.TryGetProperty("indeterminate", out JsonElement indeterminate)
            && indeterminate.ValueKind == JsonValueKind.True)
        {
            String activity = component.Label.Length > 0
                ? component.Label + Environment.NewLine + "Working…"
                : "Working…";
            return new BuiltView(TextLabel(activity, width, "Accent"), WrappedHeight(activity, width));
        }
        Double minimum = Number(component.Element, LumuiProtocol.Fields.Min, 0D);
        Double maximum = Number(component.Element, LumuiProtocol.Fields.Max, 100D);
        Double value = NumberValue(Current(component, input), Number(component.Element, LumuiProtocol.Fields.Value, minimum));
        Double ratio = maximum <= minimum ? 0D : Math.Clamp((value - minimum) / (maximum - minimum), 0D, 1D);
        Int32 barWidth = Math.Max(8, Math.Min(40, width - 12));
        Int32 filled = (Int32)Math.Round(ratio * barWidth);
        Char filledCharacter = '=';
        Char emptyCharacter = '-';
        String unit = Text(component.Element, "unit");
        String displayed = value.ToString("0.##", CultureInfo.CurrentCulture)
            + (unit.Length > 0 ? " " + unit : String.Empty);
        String bar = "[" + new String(filledCharacter, filled) + new String(emptyCharacter, barWidth - filled) + "] "
            + (ratio * 100D).ToString("0", CultureInfo.InvariantCulture) + "%  " + displayed;
        String text = component.Label.Length > 0 ? component.Label + Environment.NewLine + bar : bar;
        return new BuiltView(TextLabel(text, width, "Accent"), WrappedHeight(text, width));
    }

    private static BuiltView Media(
        SemanticComponent component,
        Int32 width,
        Func<SemanticComponent, TerminalInteraction, Task> interact,
        Boolean unicode)
    {
        Boolean video = component.Kind is LumuiProtocol.ComponentKinds.Video or LumuiProtocol.ComponentKinds.VideoPlayer;
        StackBuilder content = new StackBuilder(Math.Max(18, width - 2));
        MediaSourceDescriptor? primarySource = component.MediaSources.FirstOrDefault();
        if (video && primarySource is not null)
        {
            TerminalMediaPreviewView preview = new TerminalMediaPreviewView(
                primarySource,
                true,
                () => _ = interact(component, TerminalInteraction.Media))
            {
                Width = content.Width,
                SchemeName = "Base"
            };
            content.Add(
                preview,
                MediaPreviewHeight(component.Element, content.Width, 16D / 9D, 7, 16));
        }
        else if (!video)
        {
            Int32 cardWidth = Math.Min(content.Width, 72);
            Int32 bodyWidth = Math.Max(16, cardWidth - 2);
            String artwork = AudioArtwork(component, bodyWidth, unicode);
            Int32 artworkHeight = WrappedHeight(artwork, bodyWidth);
            Action activate = () => _ = interact(component, TerminalInteraction.Media);
            Label artworkLabel = new CliActivatableLabel(activate)
            {
                Text = artwork,
                Width = bodyWidth,
                Height = artworkHeight,
                SchemeName = "Base"
            };
            FrameView audioPreview = new CliActivatableFrame(activate)
            {
                Title = FitButtonLabel(
                    (unicode ? "♪ " : String.Empty)
                        + "Audio player  ·  "
                        + AudioState(component),
                    cardWidth),
                X = Math.Max(0, (content.Width - cardWidth) / 2),
                Width = cardWidth,
                Height = artworkHeight + 2,
                CanFocus = true,
                TabStop = TabBehavior.TabStop,
                SchemeName = "Base"
            };
            audioPreview.Add(artworkLabel);
            content.Add(audioPreview, artworkHeight + 2);
        }
        else
        {
            String artwork = VideoArtwork(content.Width, unicode);
            content.Add(TextLabel(artwork, content.Width, "Accent"), WrappedHeight(artwork, content.Width));
        }
        String description = Text(component.Element, LumuiProtocol.Fields.Description, component.Text);
        if (description.Length > 0)
        {
            content.Add(TextLabel(description, content.Width, "Muted"), WrappedHeight(description, content.Width));
        }
        AddFields(
            component,
            content,
            content.Width,
            "artist",
            "album",
            "state",
            "duration_ms",
            "position_ms",
            "preload");
        String playerLabel = video ? "Open video player" : "Open audio player";
        Button play = new CliButton
        {
            Id = component.Id,
            Text = playerLabel,
            Width = Math.Min(content.Width, playerLabel.Length + 4),
            SchemeName = "Accent",
            Enabled = component.Enabled && component.MediaSources.Count > 0
        };
        play.Accepting += (_, _) => _ = interact(component, TerminalInteraction.Media);
        content.Add(play, 1);
        Int32 resourceIndex = 0;
        foreach ((String resourceLabel, Uri address) in MediaResources(component))
        {
            SemanticComponent resource = ResourceComponent(component, resourceLabel, address, resourceIndex++);
            BuiltView link = Link(resource, resourceLabel, content.Width, interact);
            content.Add(link.View, link.Height);
        }
        foreach (SemanticComponent child in component.Children)
        {
            BuiltView action = ActionButton(
                child,
                child.Label.Length > 0 ? child.Label : Humanize(child.ActionId),
                content.Width,
                TerminalInteraction.Action,
                interact,
                false);
            content.Add(action.View, action.Height);
        }
        FrameView frame = new FrameView
        {
            Title = component.Label.Length > 0 ? component.Label : video ? "Video" : "Audio",
            Width = width,
            Height = content.Height + 2,
            CanFocus = true,
            TabStop = TabBehavior.NoStop
        };
        frame.Add(content.View);
        return new BuiltView(frame, content.Height + 2);
    }

    private static IEnumerable<(String Label, Uri Address)> MediaResources(SemanticComponent component)
    {
        List<(String Label, Uri Address)> resources = component.MediaSources
            .Select(source => ("Source", source.Uri))
            .ToList();
        Uri? baseUri = component.MediaSources.FirstOrDefault()?.Uri ?? component.Target;
        if (baseUri is not null)
        {
            foreach ((String label, String field) in new[]
            {
                ("License", "license"),
                ("Poster", "poster"),
                ("Artwork", "artwork"),
                ("Transcript", "transcript"),
                ("Captions", "captions"),
                ("Audio description", "audio_description")
            })
            {
                foreach (Uri address in MediaFieldUris(component.Element, baseUri, field))
                {
                    resources.Add((label, address));
                }
            }
        }
        return resources
            .GroupBy(resource => resource.Address.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());
    }

    private static IEnumerable<Uri> MediaFieldUris(JsonElement element, Uri baseUri, String field)
    {
        List<Uri> values = new List<Uri>();
        if (element.TryGetProperty(field, out JsonElement direct))
        {
            AddUris(direct, baseUri, values);
        }
        if (element.TryGetProperty(LumuiProtocol.Fields.Source, out JsonElement source))
        {
            if (source.ValueKind == JsonValueKind.Object && source.TryGetProperty(field, out JsonElement nested))
            {
                AddUris(nested, baseUri, values);
            }
            else if (source.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in source.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty(field, out JsonElement nestedItem))
                    {
                        AddUris(nestedItem, baseUri, values);
                    }
                }
            }
        }
        return values;
    }

    private static void AddUris(JsonElement value, Uri baseUri, ICollection<Uri> values)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                AddUris(item, baseUri, values);
            }
            return;
        }
        String address = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? String.Empty,
            JsonValueKind.Object => Text(
                value,
                LumuiProtocol.Fields.Href,
                Text(value, LumuiProtocol.Fields.Src, Text(value, "url", Text(value, "uri")))),
            _ => String.Empty
        };
        if (address.Length > 0 && Uri.TryCreate(baseUri, address, out Uri? resolved))
        {
            values.Add(resolved);
        }
    }

    private static SemanticComponent ResourceComponent(
        SemanticComponent owner,
        String label,
        Uri address,
        Int32 index) =>
        new SemanticComponent(
            owner.Element,
            owner.Id + ".resource." + index.ToString(CultureInfo.InvariantCulture),
            LumuiProtocol.ComponentKinds.Link,
            label,
            String.Empty,
            String.Empty,
            address,
            Array.Empty<SemanticComponent>(),
            Array.Empty<SemanticOption>(),
            Array.Empty<MediaSourceDescriptor>());

    private static String VideoArtwork(Int32 width, Boolean unicode)
    {
        Int32 inner = Math.Clamp(width - 2, 12, 36);
        String center = (unicode ? "▶ VIDEO" : "> VIDEO").PadLeft((inner + 7) / 2).PadRight(inner);
        Char horizontal = unicode ? '─' : '-';
        return (unicode ? "┌" : "+") + new String(horizontal, inner) + (unicode ? "┐" : "+") + Environment.NewLine
            + (unicode ? "│" : "|") + center + (unicode ? "│" : "|") + Environment.NewLine
            + (unicode ? "└" : "+") + new String(horizontal, inner) + (unicode ? "┘" : "+");
    }

    private static String AudioArtwork(
        SemanticComponent component,
        Int32 width,
        Boolean unicode)
    {
        Int32 inner = Math.Clamp(width, 16, 70);
        String title = component.Label.Length > 0 ? component.Label : "Audio";
        String artist = Text(component.Element, "artist");
        String album = Text(component.Element, "album");
        String byline = String.Join(
            "  ·  ",
            new[] { artist, album }.Where(value => value.Length > 0));
        Double durationMilliseconds = Math.Max(
            0D,
            Number(component.Element, "duration_ms", 0D));
        Double positionMilliseconds = Math.Clamp(
            Number(component.Element, "position_ms", 0D),
            0D,
            durationMilliseconds > 0D ? durationMilliseconds : Double.MaxValue);
        Double ratio = durationMilliseconds > 0D
            ? positionMilliseconds / durationMilliseconds
            : 0D;
        Int32 barWidth = Math.Clamp(Math.Max(4, inner - 16), 4, 36);
        Int32 marker = Math.Clamp(
            (Int32)Math.Round(ratio * (barWidth - 1)),
            0,
            barWidth - 1);
        Char played = unicode ? '━' : '=';
        Char remaining = unicode ? '─' : '-';
        Char head = unicode ? '●' : 'o';
        String bar = "["
            + new String(played, marker)
            + head
            + new String(remaining, barWidth - marker - 1)
            + "]";
        String wave = unicode
            ? "▁▂▄▆█▆▄▂▁  ▂▅▇▅▂  ▁▃▆▃▁"
            : "..-=#=-..  .=*#*=.  .-=-.";
        String prompt = unicode
            ? "↵  Enter or click to open playback controls"
            : "Enter or click to open playback controls";
        List<String> lines = new List<String>
        {
            CenterText(Fit(title, inner), inner)
        };
        if (byline.Length > 0)
        {
            lines.Add(CenterText(Fit(byline, inner), inner));
        }
        lines.Add(CenterText(Fit(wave, inner), inner));
        lines.Add(
            CenterText(
                MediaTime(positionMilliseconds)
                    + " "
                    + bar
                    + " "
                    + (durationMilliseconds > 0D ? MediaTime(durationMilliseconds) : "--:--"),
                inner));
        lines.Add(CenterText(Fit(prompt, inner), inner));
        return String.Join(Environment.NewLine, lines);
    }

    private static String AudioState(SemanticComponent component)
    {
        String state = Text(component.Element, "state", "ready").Trim();
        return (state.Length > 0 ? state : "ready").ToUpperInvariant();
    }

    private static String MediaTime(Double milliseconds)
    {
        TimeSpan value = TimeSpan.FromMilliseconds(Math.Max(0D, milliseconds));
        return value.TotalHours >= 1D
            ? value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
            : value.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    private static String CenterText(String value, Int32 width)
    {
        String displayed = Fit(value, width).TrimEnd();
        return displayed.PadLeft(displayed.Length + Math.Max(0, (width - displayed.Length) / 2));
    }

    private BuiltView Image(
        SemanticComponent component,
        IDictionary<String, Object?> input,
        Int32 width,
        Int32 depth,
        ICollection<String> outline,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        String alt = Text(
            component.Element,
            LumuiProtocol.Fields.Alt,
            Text(
                component.Element,
                "purpose",
                Text(component.Element, LumuiProtocol.Fields.Caption, component.Label)));
        StackBuilder stack = new StackBuilder(width);
        Uri? source = component.MediaSources.FirstOrDefault()?.Uri;
        if (source is not null)
        {
            TerminalMediaPreviewView preview = new TerminalMediaPreviewView(
                component.MediaSources[0],
                false,
                () => _ = interact(component, TerminalInteraction.Media))
            {
                Width = width,
                SchemeName = "Base"
            };
            stack.Add(
                preview,
                MediaPreviewHeight(component.Element, width, 16D / 9D, 7, 18));
        }
        else
        {
            String artwork = ImageArtwork(component.Id.Length > 0 ? component.Id : alt, width, _preferences.UseUnicode);
            stack.Add(TextLabel(artwork, width, "Accent"), WrappedHeight(artwork, width));
        }
        if (alt.Length > 0)
        {
            stack.Add(TextLabel(alt, width, "Muted"), WrappedHeight(alt, width));
        }
        AddFields(
            component,
            stack,
            width,
            "caption",
            "purpose",
            "intrinsic_aspect_ratio",
            "renderer",
            "integrity",
            "capabilities",
            "state_schema");
        if (source is not null)
        {
            String sourceText = "Source  " + source.AbsoluteUri;
            stack.Add(TextLabel(sourceText, width, "Muted"), WrappedHeight(sourceText, width));
            BuiltView preview = ActionButton(
                component,
                "View image",
                width,
                TerminalInteraction.Media,
                interact,
                true);
            stack.Add(preview.View, preview.Height);
            BuiltView open = ActionButton(
                component,
                "Open image source",
                width,
                TerminalInteraction.Resource,
                interact,
                false);
            stack.Add(open.View, open.Height);
            BuiltView download = ActionButton(
                component,
                "Download image",
                width,
                TerminalInteraction.Download,
                interact,
                false);
            stack.Add(download.View, download.Height);
        }
        else if (component.Target is not null)
        {
            BuiltView link = ActionButton(component, "Open image source", width, TerminalInteraction.Navigate, interact, false);
            stack.Add(link.View, link.Height);
        }
        foreach (SemanticComponent child in component.Children)
        {
            BuiltView built = Build(child, input, Math.Max(20, width - 2), depth + 1, outline, interact);
            built.View.X = 2;
            stack.Add(built.View, built.Height);
        }
        return new BuiltView(stack.View, stack.Height);
    }

    private BuiltView Figure(
        SemanticComponent component,
        IDictionary<String, Object?> input,
        Int32 width,
        Int32 depth,
        ICollection<String> outline,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        StackBuilder stack = new StackBuilder(width);
        if (component.Label.Length > 0)
        {
            stack.Add(Heading(component.Label, width, false), WrappedHeight(component.Label, width));
        }
        foreach (SemanticComponent child in component.Children)
        {
            BuiltView built = Build(child, input, width, depth + 1, outline, interact);
            stack.Add(built.View, built.Height);
        }
        foreach (String field in new[] { LumuiProtocol.Fields.Caption, "credit" })
        {
            String value = Text(component.Element, field);
            if (value.Length > 0)
            {
                String line = Humanize(field) + "  " + value;
                stack.Add(TextLabel(line, width, "Muted"), WrappedHeight(line, width));
            }
        }
        if (component.Target is not null)
        {
            SemanticComponent source = ResourceComponent(component, "Source", component.Target, 0);
            BuiltView link = Link(source, "Source", width, interact);
            stack.Add(link.View, link.Height);
        }
        return new BuiltView(stack.View, Math.Max(1, stack.Height));
    }

    private static BuiltView Icon(
        SemanticComponent component,
        Int32 width,
        Func<SemanticComponent, TerminalInteraction, Task> interact,
        Boolean unicode)
    {
        String symbol = Text(component.Element, LumuiProtocol.Fields.Symbol, "◆");
        String meaning = Text(component.Element, LumuiProtocol.Fields.Meaning, component.Label);
        String glyph = symbol.Length == 1 && unicode ? symbol : unicode ? "◆" : "*";
        String text = glyph + (meaning.Length > 0 ? "  " + meaning : "  " + Humanize(symbol));
        StackBuilder stack = new StackBuilder(width);
        stack.Add(TextLabel(text, width, "Accent"), WrappedHeight(text, width));
        if (component.MediaSources.Count > 0)
        {
            String source = "Source  " + component.MediaSources[0].Uri.AbsoluteUri;
            stack.Add(TextLabel(source, width, "Muted"), WrappedHeight(source, width));
            BuiltView open = ActionButton(component, "View icon", width, TerminalInteraction.Media, interact, false);
            stack.Add(open.View, open.Height);
        }
        return new BuiltView(stack.View, Math.Max(1, stack.Height));
    }

    private static String ImageArtwork(String seed, Int32 width, Boolean unicode)
    {
        Int32 inner = Math.Clamp(width - 2, 12, 34);
        Int32 hash = StringComparer.Ordinal.GetHashCode(seed);
        Char[] palette = unicode
            ? new[] { ' ', '·', '░', '▒', '▓', '█' }
            : new[] { ' ', '.', ':', '*', '#', '@' };
        StringBuilder output = new StringBuilder();
        output.Append(unicode ? '┌' : '+').Append(unicode ? '─' : '-', inner).Append(unicode ? '┐' : '+').AppendLine();
        for (Int32 row = 0; row < 4; row++)
        {
            output.Append(unicode ? '│' : '|');
            for (Int32 column = 0; column < inner; column++)
            {
                Int32 value = unchecked(hash + row * 131 + column * 37 + row * column * 11);
                output.Append(palette[Math.Abs(value % palette.Length)]);
            }
            output.Append(unicode ? '│' : '|').AppendLine();
        }
        output.Append(unicode ? '└' : '+').Append(unicode ? '─' : '-', inner).Append(unicode ? '┘' : '+');
        return output.ToString();
    }

    private static Int32 MediaPreviewHeight(
        JsonElement element,
        Int32 width,
        Double fallbackAspectRatio,
        Int32 minimum,
        Int32 maximum)
    {
        Double aspectRatio = fallbackAspectRatio;
        String ratio = Text(
            element,
            "intrinsic_aspect_ratio",
            Text(element, "aspect_ratio"));
        String[] values = ratio.Split(':', 2, StringSplitOptions.TrimEntries);
        if (values.Length == 2
            && Double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out Double ratioWidth)
            && Double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out Double ratioHeight)
            && ratioWidth > 0D
            && ratioHeight > 0D)
        {
            aspectRatio = ratioWidth / ratioHeight;
        }
        return Math.Clamp(
            (Int32)Math.Round(Math.Max(1, width) / Math.Max(0.1D, aspectRatio) / 2D),
            minimum,
            maximum);
    }

    private BuiltView Grid(
        SemanticComponent component,
        IDictionary<String, Object?> input,
        Int32 width,
        Int32 depth,
        ICollection<String> outline,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        String selectionMode = Text(component.Element, "selection_mode");
        if (selectionMode.Length > 0
            && !selectionMode.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return Collection(component, input, width, depth, outline, interact);
        }
        Int32 columns = !LinearOutput && width >= 76 ? 2 : 1;
        Int32 gap = columns > 1 ? 2 : 0;
        Int32 cellWidth = Math.Max(24, (width - gap) / columns);
        List<BuiltView> cells = component.Children
            .Where(child => child.Visible)
            .Select(child => Build(child, input, cellWidth, depth + 1, outline, interact))
            .ToList();
        View grid = new View
        {
            Width = width,
            CanFocus = true,
            TabStop = TabBehavior.NoStop
        };
        Int32 y = 0;
        if (component.Label.Length > 0)
        {
            Label heading = Heading(component.Label, width, false);
            heading.Y = y;
            grid.Add(heading);
            outline.Add(new String(' ', Math.Min(depth, 3) * 2) + component.Label);
            y += WrappedHeight(component.Label, width)
                + (ComfortableSpacing ? 1 : 0);
        }
        String caption = Text(component.Element, "caption");
        if (caption.Length > 0)
        {
            Label captionLabel = TextLabel(caption, width, "Muted");
            captionLabel.Y = y;
            grid.Add(captionLabel);
            y += WrappedHeight(caption, width);
        }
        if (component.Element.TryGetProperty("current_index", out JsonElement currentIndex))
        {
            String current = "Current item  " + Display(currentIndex);
            Label currentLabel = TextLabel(current, width, "Muted");
            currentLabel.Y = y;
            grid.Add(currentLabel);
            y += WrappedHeight(current, width);
        }
        if (cells.Count == 0)
        {
            Label empty = TextLabel("No items", width, "Muted");
            empty.Y = y;
            grid.Add(empty);
            y++;
        }
        for (Int32 index = 0; index < cells.Count; index += columns)
        {
            Int32 rowHeight = cells.Skip(index).Take(columns).Max(cell => cell.Height);
            for (Int32 column = 0; column < columns && index + column < cells.Count; column++)
            {
                BuiltView cell = cells[index + column];
                cell.View.X = column * (cellWidth + gap);
                cell.View.Y = y;
                cell.View.Width = cellWidth;
                cell.View.Height = rowHeight;
                grid.Add(cell.View);
            }
            y += rowHeight + (ComfortableSpacing ? 1 : 0);
        }
        grid.Height = Math.Max(1, y);
        return new BuiltView(grid, Math.Max(1, y));
    }

    private BuiltView TabSet(
        SemanticComponent component,
        IDictionary<String, Object?> input,
        Int32 width,
        Int32 depth,
        ICollection<String> outline,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        SemanticComponent[] children = component.Children.Where(child => child.Visible).ToArray();
        if (children.Length == 0)
        {
            return new BuiltView(TextLabel(component.Label, width, "Muted"), 1);
        }
        List<(SemanticComponent Component, View Page, Int32 Height)> pages =
            new List<(SemanticComponent, View, Int32)>();
        foreach (SemanticComponent child in children)
        {
            BuiltView built = Build(child, input, Math.Max(20, width - 2), depth + 1, outline, interact);
            built.View.Title = child.Label.Length > 0 ? child.Label : Humanize(child.Kind);
            pages.Add((child, built.View, built.Height));
        }
        Int32 height = Math.Max(5, pages.Max(page => page.Height) + 2);
        Tabs tabs = new Tabs { Width = width, Height = height, TabDepth = 1 };
        tabs.Add(pages.Select(page => page.Page).ToArray());
        if (component.Element.TryGetProperty("selected", out JsonElement selectedValue))
        {
            String selected = Display(selectedValue);
            if (Int32.TryParse(selected, NumberStyles.Integer, CultureInfo.InvariantCulture, out Int32 index)
                && index >= 0
                && index < pages.Count)
            {
                tabs.Value = pages[index].Page;
            }
            else
            {
                View? match = pages
                    .Where(page =>
                        page.Component.Id.Equals(selected, StringComparison.Ordinal)
                        || page.Component.Label.Equals(selected, StringComparison.CurrentCultureIgnoreCase))
                    .Select(page => page.Page)
                    .FirstOrDefault();
                if (match is not null)
                {
                    tabs.Value = match;
                }
            }
        }
        return new BuiltView(tabs, height);
    }

    private BuiltView Collection(
        SemanticComponent component,
        IDictionary<String, Object?> input,
        Int32 width,
        Int32 depth,
        ICollection<String> outline,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        StackBuilder stack = new StackBuilder(width);
        if (component.Label.Length > 0)
        {
            stack.Add(Heading(component.Label, width, false), WrappedHeight(component.Label, width));
            outline.Add(new String(' ', Math.Min(depth, 3) * 2) + component.Label);
        }
        AddDescription(component, stack, width);
        String selectionMode = Text(component.Element, "selection_mode");
        Boolean selectable = component.Id.Length > 0
            && component.Enabled
            && !component.ReadOnly
            && selectionMode.Length > 0
            && !selectionMode.Equals("none", StringComparison.OrdinalIgnoreCase);
        Boolean multiple = selectionMode.Contains("multi", StringComparison.OrdinalIgnoreCase);
        HashSet<String> selected = SelectedValues(Current(component, input));
        List<CheckBox> selectors = new List<CheckBox>();
        Boolean synchronizing = false;
        for (Int32 index = 0; index < component.Children.Count; index++)
        {
            SemanticComponent child = component.Children[index];
            Object? childValue = component.Kind == LumuiProtocol.ComponentKinds.ImageCollection
                ? index
                : child.Element.TryGetProperty(LumuiProtocol.Fields.Value, out JsonElement value)
                    ? JsonValue(value)
                    : child.Id.Length > 0
                        ? child.Id
                        : child.Label;
            if (selectable)
            {
                String identity = ValueIdentity(childValue);
                CheckBox selector = new CheckBox
                {
                    Id = component.Id + "-item-" + index.ToString(CultureInfo.InvariantCulture),
                    Text = child.Label.Length > 0 ? child.Label : "Item " + (index + 1).ToString(CultureInfo.InvariantCulture),
                    Value = selected.Contains(identity) ? CheckState.Checked : CheckState.UnChecked,
                    RadioStyle = !multiple,
                    Width = width,
                    CanFocus = true,
                    TabStop = TabBehavior.TabStop
                };
                selectors.Add(selector);
                selector.ValueChanged += (_, _) =>
                {
                    if (synchronizing)
                    {
                        return;
                    }
                    synchronizing = true;
                    if (multiple)
                    {
                        List<Object?> values = new List<Object?>();
                        for (Int32 optionIndex = 0; optionIndex < selectors.Count; optionIndex++)
                        {
                            if (selectors[optionIndex].Value == CheckState.Checked)
                            {
                                SemanticComponent option = component.Children[optionIndex];
                                values.Add(component.Kind == LumuiProtocol.ComponentKinds.ImageCollection
                                    ? optionIndex
                                    : option.Element.TryGetProperty(LumuiProtocol.Fields.Value, out JsonElement optionValue)
                                        ? JsonValue(optionValue)
                                        : option.Id.Length > 0 ? option.Id : option.Label);
                            }
                        }
                        input[component.Id] = values;
                    }
                    else if (selector.Value == CheckState.Checked)
                    {
                        foreach (CheckBox other in selectors)
                        {
                            if (!ReferenceEquals(other, selector))
                            {
                                other.Value = CheckState.UnChecked;
                            }
                        }
                        input[component.Id] = childValue;
                    }
                    synchronizing = false;
                    if (component.ActionId.Length > 0)
                    {
                        _ = interact(component, TerminalInteraction.Action);
                    }
                };
                stack.Add(selector, WrappedHeight(selector.Text, Math.Max(8, width - 4)));
            }
            BuiltView built = Build(child, input, Math.Max(20, width - 2), depth + 1, outline, interact);
            built.View.X = 2;
            built.View.Width = Math.Max(20, width - 2);
            stack.Add(built.View, built.Height);
        }
        return new BuiltView(stack.View, Math.Max(1, stack.Height));
    }

    private BuiltView Table(
        SemanticComponent component,
        IDictionary<String, Object?> input,
        Int32 width,
        Int32 depth,
        ICollection<String> outline,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        List<(String Key, String Label)> columns = new List<(String, String)>();
        if (component.Element.TryGetProperty(LumuiProtocol.Fields.Columns, out JsonElement columnValues)
            && columnValues.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement column in columnValues.EnumerateArray())
            {
                if (column.ValueKind == JsonValueKind.String)
                {
                    String key = column.GetString() ?? String.Empty;
                    columns.Add((key, Humanize(key)));
                }
                else if (column.ValueKind == JsonValueKind.Object)
                {
                    String key = Text(column, "key", Text(column, LumuiProtocol.Fields.Id));
                    columns.Add((key, Text(column, LumuiProtocol.Fields.Label, Humanize(key))));
                }
            }
        }
        List<JsonElement> rows = new List<JsonElement>();
        if (component.Element.TryGetProperty(LumuiProtocol.Fields.Rows, out JsonElement rowValues)
            && rowValues.ValueKind == JsonValueKind.Array)
        {
            rows.AddRange(rowValues.EnumerateArray().Select(row => row.Clone()));
        }
        StackBuilder stack = new StackBuilder(width);
        if (component.Label.Length > 0)
        {
            stack.Add(Heading(component.Label, width, false), WrappedHeight(component.Label, width));
        }
        String caption = Text(component.Element, "caption");
        if (caption.Length > 0)
        {
            stack.Add(TextLabel(caption, width, "Muted"), WrappedHeight(caption, width));
        }
        AddFields(component, stack, width, "sortable", "filterable", "selection_mode", "pagination");
        if (columns.Count == 0 || rows.Count == 0)
        {
            String fallback = component.Text.Length > 0
                ? component.Text
                : rows.Count == 0
                    ? "No table rows"
                    : "No table columns";
            stack.Add(TextLabel(fallback, width, "Muted"), WrappedHeight(fallback, width));
            foreach (SemanticComponent child in component.Children)
            {
                BuiltView built = Build(child, input, Math.Max(20, width - 2), depth + 1, outline, interact);
                built.View.X = 2;
                stack.Add(built.View, built.Height);
            }
            return new BuiltView(stack.View, stack.Height);
        }
        Boolean tabular = width >= 72
            && columns.Count <= Math.Max(1, (width + 1) / 9);
        String selectionMode = Text(component.Element, "selection_mode");
        Boolean multiple = selectionMode.Contains("multi", StringComparison.OrdinalIgnoreCase);
        Boolean selectable = component.Enabled
            && !component.ReadOnly
            && component.Id.Length > 0
            && ((selectionMode.Length > 0
                    && !selectionMode.Equals("none", StringComparison.OrdinalIgnoreCase))
                || component.ActionId.Length > 0);
        HashSet<String> selected = SelectedValues(Current(component, input));
        List<Button> selectionButtons = new List<Button>();
        void SelectRow(Button button, JsonElement row)
        {
            String identity = ValueIdentity(row);
            if (multiple)
            {
                List<Object?> values = Current(component, input) switch
                {
                    JsonElement element when element.ValueKind == JsonValueKind.Array =>
                        element.EnumerateArray().Select(item => (Object?)item.Clone()).ToList(),
                    String value => new List<Object?> { value },
                    System.Collections.IEnumerable items =>
                        items.Cast<Object?>().ToList(),
                    Object value => new List<Object?> { value },
                    _ => new List<Object?>()
                };
                Int32 existing = values.FindIndex(value => ValueIdentity(value) == identity);
                if (existing >= 0)
                {
                    values.RemoveAt(existing);
                    button.SchemeName = "Base";
                }
                else
                {
                    values.Add(row.Clone());
                    button.SchemeName = "Accent";
                }
                input[component.Id] = values;
            }
            else
            {
                input[component.Id] = row.Clone();
                foreach (Button candidate in selectionButtons)
                {
                    candidate.SchemeName = "Base";
                    candidate.SetNeedsDraw();
                }
                button.SchemeName = "Accent";
            }
            button.SetNeedsDraw();
            if (component.ActionId.Length > 0)
            {
                _ = interact(component, TerminalInteraction.Action);
            }
        }
        Button SelectionButton(String text, JsonElement row, Int32 index)
        {
            Button button = new CliButton
            {
                Id = component.Id + "-row-" + index.ToString(CultureInfo.InvariantCulture),
                Text = text,
                Width = Math.Min(width, Math.Max(12, text.Length + 4)),
                SchemeName = selected.Contains(ValueIdentity(row)) ? "Accent" : "Base"
            };
            selectionButtons.Add(button);
            button.Accepting += (_, _) => SelectRow(button, row);
            return button;
        }
        if (tabular)
        {
            Int32 usableWidth = selectable ? Math.Max(24, width - 4) : width;
            Int32 cellWidth = Math.Max(8, (usableWidth - columns.Count + 1) / columns.Count);
            String separator = _preferences.UseUnicode ? "│" : "|";
            String header = String.Join(separator, columns.Select(column => Fit(column.Label, cellWidth)));
            stack.Add(TextLabel(header, width, "Accent"), 1);
            for (Int32 index = 0; index < rows.Count; index++)
            {
                JsonElement row = rows[index];
                String line = String.Join(separator, columns.Select(column => Fit(RowValue(row, column.Key), cellWidth)));
                if (selectable)
                {
                    stack.Add(SelectionButton(line, row, index), 1);
                }
                else
                {
                    stack.Add(TextLabel(line, width, "Base"), 1);
                }
            }
        }
        else
        {
            for (Int32 index = 0; index < rows.Count; index++)
            {
                JsonElement row = rows[index];
                String value = String.Join(
                    Environment.NewLine,
                    columns.Select(column => column.Label + "  " + RowValue(row, column.Key)));
                stack.Add(TextLabel(value, width, "Base"), WrappedHeight(value, width));
                if (selectable)
                {
                    stack.Add(
                        SelectionButton(
                            "Select row " + (index + 1).ToString(CultureInfo.InvariantCulture),
                            row,
                            index),
                        1);
                }
                if (index + 1 < rows.Count)
                {
                    stack.Add(TextLabel(new String('-', Math.Max(1, width - 1)), width, "Muted"), 1);
                }
            }
        }
        foreach (SemanticComponent child in component.Children)
        {
            BuiltView built = Build(child, input, Math.Max(20, width - 2), depth + 1, outline, interact);
            built.View.X = 2;
            stack.Add(built.View, built.Height);
        }
        return new BuiltView(stack.View, Math.Max(1, stack.Height));
    }

    private BuiltView Chart(
        SemanticComponent component,
        IDictionary<String, Object?> input,
        Int32 width,
        Int32 depth,
        ICollection<String> outline,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        StackBuilder stack = new StackBuilder(width);
        if (component.Label.Length > 0)
        {
            stack.Add(Heading(component.Label, width, false), WrappedHeight(component.Label, width));
        }
        String summary = Text(component.Element, "summary");
        if (summary.Length > 0)
        {
            stack.Add(TextLabel(summary, width, "Muted"), WrappedHeight(summary, width));
        }
        AddFields(component, stack, width, "chart_type", "axes");
        JsonElement values = component.Element.TryGetProperty("data", out JsonElement data)
            ? data
            : component.Element.TryGetProperty(LumuiProtocol.Fields.Values, out JsonElement chartValues)
                ? chartValues
                : default;
        List<(String Label, Double Value)> points = new List<(String, Double)>();
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
        else if (values.ValueKind == JsonValueKind.Object)
        {
            List<String> labels = values.TryGetProperty("labels", out JsonElement labelValues)
                && labelValues.ValueKind == JsonValueKind.Array
                    ? labelValues.EnumerateArray().Select(Display).ToList()
                    : new List<String>();
            if (values.TryGetProperty(LumuiProtocol.Fields.Values, out JsonElement numericValues)
                && numericValues.ValueKind == JsonValueKind.Array)
            {
                Int32 index = 0;
                foreach (JsonElement point in numericValues.EnumerateArray())
                {
                    if (point.TryGetDouble(out Double number))
                    {
                        points.Add((
                            index < labels.Count ? labels[index] : (index + 1).ToString(CultureInfo.InvariantCulture),
                            number));
                    }
                    index++;
                }
            }
            if (points.Count == 0 && values.TryGetProperty("series", out JsonElement series)
                && series.ValueKind == JsonValueKind.Array)
            {
                Int32 seriesIndex = 1;
                foreach (JsonElement item in series.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }
                    String seriesLabel = Text(item, LumuiProtocol.Fields.Label, "Series " + seriesIndex);
                    if (item.TryGetProperty(LumuiProtocol.Fields.Values, out JsonElement seriesValues)
                        && seriesValues.ValueKind == JsonValueKind.Array)
                    {
                        Int32 pointIndex = 0;
                        foreach (JsonElement point in seriesValues.EnumerateArray())
                        {
                            if (point.TryGetDouble(out Double number))
                            {
                                String pointLabel = pointIndex < labels.Count
                                    ? labels[pointIndex]
                                    : (pointIndex + 1).ToString(CultureInfo.InvariantCulture);
                                points.Add((seriesLabel + " · " + pointLabel, number));
                            }
                            pointIndex++;
                        }
                    }
                    seriesIndex++;
                }
            }
        }
        Double maximum = points.Count == 0 ? 1D : Math.Max(1D, points.Max(point => Math.Abs(point.Value)));
        Int32 barWidth = Math.Max(4, Math.Min(32, width - 20));
        foreach ((String pointLabel, Double pointValue) in points)
        {
            Int32 size = Math.Clamp((Int32)Math.Round(Math.Abs(pointValue) / maximum * barWidth), 1, barWidth);
            String line = Fit(pointLabel, 12)
                + new String('=', size)
                + " "
                + pointValue.ToString("0.##", CultureInfo.InvariantCulture);
            stack.Add(TextLabel(line, width, "Base"), 1);
        }
        if (points.Count == 0)
        {
            stack.Add(TextLabel("No chart data", width, "Muted"), 1);
        }
        foreach (SemanticComponent child in component.Children)
        {
            BuiltView built = Build(child, input, Math.Max(20, width - 2), depth + 1, outline, interact);
            built.View.X = 2;
            stack.Add(built.View, built.Height);
        }
        return new BuiltView(stack.View, Math.Max(1, stack.Height));
    }

    private BuiltView Message(
        SemanticComponent component,
        IDictionary<String, Object?> input,
        Int32 width,
        Int32 depth,
        ICollection<String> outline,
        Func<SemanticComponent, TerminalInteraction, Task> interact,
        Boolean error)
    {
        Int32 innerWidth = Math.Max(18, width - 2);
        StackBuilder stack = new StackBuilder(innerWidth);
        String message = Text(
            component.Element,
            LumuiProtocol.Fields.Message,
            Text(
                component.Element,
                LumuiProtocol.Fields.Body,
                Text(
                    component.Element,
                    LumuiProtocol.Fields.StateDescription,
                    Text(component.Element, LumuiProtocol.Fields.Description, component.Text))));
        if (message.Length > 0)
        {
            stack.Add(TextLabel(message, innerWidth, error ? "Error" : "Accent"), WrappedHeight(message, innerWidth));
        }
        List<String> metadata = new List<String>();
        foreach (String field in new[]
        {
            "state",
            "value",
            "tone",
            "severity",
            "category",
            "priority",
            "expires_at",
            "correlation_id"
        })
        {
            if (component.Element.TryGetProperty(field, out JsonElement value))
            {
                String displayed = Display(value);
                if (displayed.Length > 0)
                {
                    metadata.Add(Humanize(field) + "  " + displayed);
                }
            }
        }
        if (metadata.Count > 0)
        {
            String details = String.Join(Environment.NewLine, metadata);
            stack.Add(TextLabel(details, innerWidth, "Muted"), WrappedHeight(details, innerWidth));
        }
        foreach (SemanticComponent child in component.Children)
        {
            BuiltView built = Build(child, input, innerWidth, depth + 1, outline, interact);
            stack.Add(built.View, built.Height);
        }
        FrameView frame = new FrameView
        {
            Title = component.Label.Length > 0 ? component.Label : error ? "Problem" : Humanize(component.Kind),
            SchemeName = error ? "Error" : "Accent",
            Width = width,
            Height = Math.Max(3, stack.Height + 2),
            CanFocus = true,
            TabStop = TabBehavior.NoStop
        };
        frame.Add(stack.View);
        return new BuiltView(frame, Math.Max(3, stack.Height + 2));
    }

    private BuiltView Data(
        SemanticComponent component,
        IDictionary<String, Object?> input,
        Int32 width,
        Int32 depth,
        ICollection<String> outline,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        StackBuilder stack = new StackBuilder(width);
        Object? sourceValue = Property(component.Element, LumuiProtocol.Fields.Value);
        String value = DisplayValue(sourceValue, component.Kind);
        if (value.Length == 0)
        {
            value = component.Text.Length > 0 ? component.Text : Describe(component.Element);
        }
        String unit = Text(component.Element, "unit");
        String text = component.Label.Length > 0 ? component.Label + "  " + value : value;
        if (unit.Length > 0)
        {
            text += " " + unit;
        }
        stack.Add(TextLabel(text, width, "Base"), WrappedHeight(text, width));
        String description = Text(component.Element, LumuiProtocol.Fields.Description);
        if (description.Length > 0)
        {
            stack.Add(TextLabel(description, width, "Muted"), WrappedHeight(description, width));
        }
        foreach (String field in new[] { "source", "format" })
        {
            if (component.Element.TryGetProperty(field, out JsonElement fieldValue))
            {
                String line = Humanize(field) + "  " + Display(fieldValue);
                stack.Add(TextLabel(line, width, "Muted"), WrappedHeight(line, width));
            }
        }
        foreach (SemanticComponent child in component.Children)
        {
            BuiltView built = Build(child, input, Math.Max(20, width - 2), depth + 1, outline, interact);
            built.View.X = 2;
            stack.Add(built.View, built.Height);
        }
        return new BuiltView(stack.View, stack.Height);
    }

    private BuiltView Calendar(
        SemanticComponent component,
        IDictionary<String, Object?> input,
        Int32 width,
        Int32 depth,
        ICollection<String> outline,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        StackBuilder stack = new StackBuilder(width);
        if (component.Label.Length > 0)
        {
            stack.Add(Heading(component.Label, width, false), WrappedHeight(component.Label, width));
            outline.Add(new String(' ', Math.Min(depth, 3) * 2) + component.Label);
        }
        AddDescription(component, stack, width);
        DateTime selected = CalendarDate(component, input);
        String calendar = CalendarText(selected, width);
        stack.Add(TextLabel(calendar, width, "Base"), WrappedHeight(calendar, width));
        if (component.Options.Count > 0)
        {
            BuiltView options = Options(component, input, width, false, interact, false);
            stack.Add(options.View, options.Height);
        }
        foreach (SemanticComponent child in component.Children)
        {
            BuiltView built = Build(child, input, Math.Max(20, width - 2), depth + 1, outline, interact);
            built.View.X = 2;
            stack.Add(built.View, built.Height);
        }
        if (component.ActionId.Length > 0 || component.Target is not null)
        {
            BuiltView action = ActionButton(
                component,
                "Choose date",
                width,
                component.Id.Length > 0 && component.ActionId.Length > 0
                    ? TerminalInteraction.Edit
                    : component.ActionId.Length > 0
                        ? TerminalInteraction.Action
                        : TerminalInteraction.Navigate,
                interact,
                false);
            stack.Add(action.View, action.Height);
        }
        return new BuiltView(stack.View, Math.Max(1, stack.Height));
    }

    private BuiltView Clock(
        SemanticComponent component,
        IDictionary<String, Object?> input,
        Int32 width,
        Int32 depth,
        ICollection<String> outline,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        StackBuilder stack = new StackBuilder(width);
        if (component.Label.Length > 0)
        {
            stack.Add(Heading(component.Label, width, false), WrappedHeight(component.Label, width));
            outline.Add(new String(' ', Math.Min(depth, 3) * 2) + component.Label);
        }
        AddDescription(component, stack, width);
        List<String> values = new List<String>();
        String current = DisplayValue(Current(component, input), component.Kind);
        if (current.Length == 0
            && component.Element.TryGetProperty(LumuiProtocol.Fields.Fallback, out JsonElement fallback)
            && fallback.ValueKind == JsonValueKind.Object)
        {
            current = Text(fallback, LumuiProtocol.Fields.Text);
            if (current.Length == 0
                && fallback.TryGetProperty(LumuiProtocol.Fields.Value, out JsonElement fallbackValue))
            {
                current = Display(fallbackValue);
            }
        }
        String timezone = Text(component.Element, "timezone");
        if (current.Length == 0)
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
            current = displayedAt.ToString("HH:mm", CultureInfo.CurrentCulture);
        }
        values.Add(current);
        if (timezone.Length > 0)
        {
            values.Add(timezone);
        }
        String text = String.Join(Environment.NewLine, values);
        stack.Add(TextLabel(text, width, "Base"), WrappedHeight(text, width));
        foreach (SemanticComponent child in component.Children)
        {
            BuiltView built = Build(child, input, Math.Max(20, width - 2), depth + 1, outline, interact);
            built.View.X = 2;
            stack.Add(built.View, built.Height);
        }
        if (component.ActionId.Length > 0 || component.Target is not null)
        {
            BuiltView action = ActionButton(
                component,
                "Open clock",
                width,
                component.ActionId.Length > 0 ? TerminalInteraction.Action : TerminalInteraction.Navigate,
                interact,
                false);
            stack.Add(action.View, action.Height);
        }
        return new BuiltView(stack.View, Math.Max(1, stack.Height));
    }

    private BuiltView Map(
        SemanticComponent component,
        IDictionary<String, Object?> input,
        Int32 width,
        Int32 depth,
        ICollection<String> outline,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        StackBuilder stack = new StackBuilder(width);
        if (component.Label.Length > 0)
        {
            stack.Add(Heading(component.Label, width, false), WrappedHeight(component.Label, width));
            outline.Add(new String(' ', Math.Min(depth, 3) * 2) + component.Label);
        }
        AddDescription(component, stack, width);
        List<(Double Latitude, Double Longitude, String Label)> markers =
            new List<(Double, Double, String)>();
        List<(Double Latitude, Double Longitude, String Label)> route =
            new List<(Double, Double, String)>();
        (Double Latitude, Double Longitude, String Label)? center = null;
        if (component.Element.TryGetProperty("center", out JsonElement centerValue))
        {
            center = Location(centerValue, "Center");
        }
        if (component.Element.TryGetProperty("markers", out JsonElement markerValues)
            && markerValues.ValueKind == JsonValueKind.Array)
        {
            Int32 markerIndex = 1;
            foreach (JsonElement marker in markerValues.EnumerateArray())
            {
                String label = marker.ValueKind == JsonValueKind.Object
                    ? Text(marker, LumuiProtocol.Fields.Label, Text(marker, LumuiProtocol.Fields.Title, "Marker " + markerIndex))
                    : "Marker " + markerIndex;
                (Double Latitude, Double Longitude, String Label)? point = Location(marker, label);
                if (point is not null)
                {
                    markers.Add(point.Value);
                    markerIndex++;
                }
            }
        }
        Boolean currentLocationRequested = false;
        if (component.Element.TryGetProperty("current_location", out JsonElement currentLocation))
        {
            currentLocationRequested = currentLocation.ValueKind == JsonValueKind.True;
            (Double Latitude, Double Longitude, String Label)? point =
                Location(currentLocation, "Current location");
            if (point is not null)
            {
                markers.Add(point.Value);
            }
        }
        if (component.Element.TryGetProperty("route", out JsonElement routeValue))
        {
            AddLocations(routeValue, "Route", route);
        }
        if (center is null && markers.Count > 0)
        {
            center = (
                markers.Average(point => point.Latitude),
                markers.Average(point => point.Longitude),
                "Center");
        }
        List<(Double Latitude, Double Longitude, String Label)> points =
            new List<(Double, Double, String)>();
        if (center is not null)
        {
            points.Add(center.Value);
        }
        points.AddRange(markers);
        points.AddRange(route);
        if (points.Count > 0)
        {
            String map = MapText(center, markers, route, width);
            stack.Add(TextLabel(map, width, "Base"), WrappedHeight(map, width));
        }
        else
        {
            String mode = Text(component.Element, "mode", "display");
            stack.Add(TextLabel("Map  ·  " + Humanize(mode), width, "Muted"), 1);
        }
        if (currentLocationRequested)
        {
            stack.Add(TextLabel("Current location enabled", width, "Muted"), 1);
        }
        String selected = DisplayValue(Current(component, input), component.Kind);
        if (selected.Length > 0)
        {
            String text = "Selected  " + selected;
            stack.Add(TextLabel(text, width, "Accent"), WrappedHeight(text, width));
        }
        foreach (SemanticComponent child in component.Children)
        {
            BuiltView built = Build(child, input, Math.Max(20, width - 2), depth + 1, outline, interact);
            built.View.X = 2;
            stack.Add(built.View, built.Height);
        }
        String mapMode = Text(component.Element, "mode", "display");
        if (mapMode.Contains("select", StringComparison.OrdinalIgnoreCase) && component.Id.Length > 0)
        {
            BuiltView select = ActionButton(
                component,
                "Choose location",
                width,
                TerminalInteraction.Edit,
                interact,
                true);
            stack.Add(select.View, select.Height);
        }
        else if (component.ActionId.Length > 0 || component.Target is not null)
        {
            BuiltView action = ActionButton(
                component,
                "Open map",
                width,
                component.ActionId.Length > 0 ? TerminalInteraction.Action : TerminalInteraction.Navigate,
                interact,
                false);
            stack.Add(action.View, action.Height);
        }
        return new BuiltView(stack.View, Math.Max(1, stack.Height));
    }

    private static DateTime CalendarDate(
        SemanticComponent component,
        IDictionary<String, Object?> input)
    {
        List<String> candidates = new List<String>();
        Object? current = component.Id.Length > 0 && input.TryGetValue(component.Id, out Object? value)
            ? value
            : Property(component.Element, LumuiProtocol.Fields.Value);
        String currentText = DisplayValue(current, component.Kind);
        if (currentText.Length > 0)
        {
            candidates.Add(currentText);
        }
        foreach (String field in new[] { "selected", "date", "start" })
        {
            if (component.Element.TryGetProperty(field, out JsonElement fieldValue))
            {
                candidates.Add(Display(fieldValue));
            }
        }
        foreach (String candidate in candidates)
        {
            if (DateTime.TryParse(candidate, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime parsed)
                || DateTime.TryParse(candidate, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsed))
            {
                return parsed;
            }
        }
        Int32 year = (Int32)Number(component.Element, "year", DateTime.Today.Year);
        Int32 month = (Int32)Number(component.Element, "month", DateTime.Today.Month);
        return new DateTime(Math.Clamp(year, 1, 9999), Math.Clamp(month, 1, 12), 1);
    }

    private static String CalendarText(DateTime selected, Int32 width)
    {
        DateTime month = new DateTime(selected.Year, selected.Month, 1);
        Boolean compact = width < 30;
        StringBuilder output = new StringBuilder();
        output.AppendLine(month.ToString("MMMM yyyy", CultureInfo.CurrentCulture));
        output.AppendLine(String.Join(
            " ",
            CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames.Select(day => Fit(day, compact ? 2 : 3))));
        Int32 offset = (Int32)month.DayOfWeek;
        for (Int32 index = 0; index < offset; index++)
        {
            output.Append(compact ? "   " : "    ");
        }
        Int32 days = DateTime.DaysInMonth(month.Year, month.Month);
        for (Int32 day = 1; day <= days; day++)
        {
            Boolean chosen = selected.Year == month.Year
                && selected.Month == month.Month
                && selected.Day == day;
            output.Append(day.ToString("00", CultureInfo.InvariantCulture));
            output.Append(chosen ? '*' : ' ');
            if ((offset + day) % 7 == 0)
            {
                output.AppendLine();
            }
            else
            {
                if (!compact)
                {
                    output.Append(' ');
                }
            }
        }
        return output.ToString().TrimEnd();
    }

    private static String MapText(
        (Double Latitude, Double Longitude, String Label)? center,
        IReadOnlyList<(Double Latitude, Double Longitude, String Label)> markers,
        IReadOnlyList<(Double Latitude, Double Longitude, String Label)> route,
        Int32 availableWidth)
    {
        List<(Double Latitude, Double Longitude, String Label)> points =
            new List<(Double, Double, String)>();
        if (center is not null)
        {
            points.Add(center.Value);
        }
        points.AddRange(markers);
        points.AddRange(route);
        Double minimumLatitude = points.Min(point => point.Latitude);
        Double maximumLatitude = points.Max(point => point.Latitude);
        Double minimumLongitude = points.Min(point => point.Longitude);
        Double maximumLongitude = points.Max(point => point.Longitude);
        if (Math.Abs(maximumLatitude - minimumLatitude) < 0.000001D)
        {
            minimumLatitude -= 0.01D;
            maximumLatitude += 0.01D;
        }
        if (Math.Abs(maximumLongitude - minimumLongitude) < 0.000001D)
        {
            minimumLongitude -= 0.01D;
            maximumLongitude += 0.01D;
        }
        Int32 mapWidth = Math.Clamp(availableWidth - 2, 20, 60);
        Int32 mapHeight = Math.Clamp(points.Count + 5, 7, 12);
        Char[][] canvas = Enumerable.Range(0, mapHeight)
            .Select(_ => Enumerable.Repeat(' ', mapWidth).ToArray())
            .ToArray();
        for (Int32 x = 0; x < mapWidth; x++)
        {
            Char border = x == 0 || x == mapWidth - 1 ? '+' : '-';
            canvas[0][x] = border;
            canvas[mapHeight - 1][x] = border;
        }
        for (Int32 y = 1; y < mapHeight - 1; y++)
        {
            canvas[y][0] = '|';
            canvas[y][mapWidth - 1] = '|';
        }
        void Plot(Double latitude, Double longitude, Char symbol)
        {
            Int32 x = 1 + (Int32)Math.Round(
                (longitude - minimumLongitude) / (maximumLongitude - minimumLongitude) * (mapWidth - 3));
            Int32 y = 1 + (Int32)Math.Round(
                (maximumLatitude - latitude) / (maximumLatitude - minimumLatitude) * (mapHeight - 3));
            canvas[Math.Clamp(y, 1, mapHeight - 2)][Math.Clamp(x, 1, mapWidth - 2)] = symbol;
        }
        foreach ((Double latitude, Double longitude, String _) in route)
        {
            Plot(latitude, longitude, '.');
        }
        if (center is not null)
        {
            Plot(center.Value.Latitude, center.Value.Longitude, '+');
        }
        for (Int32 index = 0; index < markers.Count; index++)
        {
            Plot(
                markers[index].Latitude,
                markers[index].Longitude,
                index < 9 ? (Char)('1' + index) : '*');
        }
        StringBuilder output = new StringBuilder();
        foreach (Char[] row in canvas)
        {
            output.AppendLine(new String(row));
        }
        if (center is not null)
        {
            output.AppendLine("Center  " + Coordinate(center.Value.Latitude, center.Value.Longitude));
        }
        for (Int32 index = 0; index < markers.Count; index++)
        {
            output.Append(index < 9 ? (index + 1).ToString(CultureInfo.InvariantCulture) : "*");
            output.Append("  ");
            output.Append(markers[index].Label);
            output.Append("  ");
            output.AppendLine(Coordinate(markers[index].Latitude, markers[index].Longitude));
        }
        return output.ToString().TrimEnd();
    }

    private static void AddLocations(
        JsonElement value,
        String label,
        ICollection<(Double Latitude, Double Longitude, String Label)> destination)
    {
        (Double Latitude, Double Longitude, String Label)? point = Location(value, label);
        if (point is not null)
        {
            destination.Add(point.Value);
            return;
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                AddLocations(item, label, destination);
            }
        }
        else if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (String field in new[] { "points", "path", "coordinates" })
            {
                if (value.TryGetProperty(field, out JsonElement nested))
                {
                    AddLocations(nested, label, destination);
                }
            }
        }
    }

    private static (Double Latitude, Double Longitude, String Label)? Location(
        JsonElement value,
        String label)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            JsonElement[] coordinates = value.EnumerateArray().Take(2).ToArray();
            if (coordinates.Length == 2
                && coordinates[0].TryGetDouble(out Double longitude)
                && coordinates[1].TryGetDouble(out Double latitude))
            {
                return (latitude, longitude, label);
            }
            return null;
        }
        if (value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        if (TryNumber(value, new[] { "latitude", "lat" }, out Double directLatitude)
            && TryNumber(value, new[] { "longitude", "longitude_degrees", "lon", "lng" }, out Double directLongitude))
        {
            return (directLatitude, directLongitude, label);
        }
        foreach (String field in new[] { "position", "location", "coordinate", "coordinates", "center" })
        {
            if (value.TryGetProperty(field, out JsonElement nested))
            {
                (Double Latitude, Double Longitude, String Label)? point = Location(nested, label);
                if (point is not null)
                {
                    return point;
                }
            }
        }
        return null;
    }

    private static Boolean TryNumber(
        JsonElement element,
        IEnumerable<String> fields,
        out Double value)
    {
        foreach (String field in fields)
        {
            if (element.TryGetProperty(field, out JsonElement candidate))
            {
                if (candidate.TryGetDouble(out value))
                {
                    return true;
                }
                if (candidate.ValueKind == JsonValueKind.String
                    && Double.TryParse(
                        candidate.GetString(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out value))
                {
                    return true;
                }
            }
        }
        value = 0D;
        return false;
    }

    private static String Coordinate(Double latitude, Double longitude) =>
        latitude.ToString("0.#####", CultureInfo.InvariantCulture)
        + ", "
        + longitude.ToString("0.#####", CultureInfo.InvariantCulture);

    private BuiltView Fallback(
        SemanticComponent component,
        IDictionary<String, Object?> input,
        Int32 width,
        Int32 depth,
        ICollection<String> outline,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        StackBuilder stack = new StackBuilder(width);
        String value = component.Text.Length > 0
            ? component.Text
            : component.Label.Length > 0
                ? component.Label
                : "Unsupported component  ·  " + Humanize(component.Kind);
        stack.Add(TextLabel(value, width, "Muted"), WrappedHeight(value, width));
        AddFields(
            component,
            stack,
            width,
            "component_type",
            "version",
            "renderer",
            "integrity",
            "capabilities",
            "state_schema");
        if (component.ActionId.Length > 0 || component.Target is not null)
        {
            BuiltView action = ActionButton(
                component,
                component.ActionId.Length > 0 ? "Open " + Humanize(component.Kind) : "Open reference",
                width,
                component.ActionId.Length > 0 ? TerminalInteraction.Action : TerminalInteraction.Navigate,
                interact,
                false);
            stack.Add(action.View, action.Height);
        }
        foreach (SemanticComponent child in component.Children)
        {
            BuiltView built = Build(child, input, Math.Max(20, width - 2), depth + 1, outline, interact);
            built.View.X = 2;
            stack.Add(built.View, built.Height);
        }
        return new BuiltView(stack.View, Math.Max(1, stack.Height));
    }

    private BuiltView Preview(
        SemanticComponent component,
        IDictionary<String, Object?> input,
        Int32 width,
        Int32 depth,
        ICollection<String> outline,
        Func<SemanticComponent, TerminalInteraction, Task> interact)
    {
        Int32 innerWidth = Math.Max(8, width - 2);
        StackBuilder stack = new StackBuilder(innerWidth);
        String title = component.Label.Length > 0
            ? component.Label
            : "Preview";
        AddDescription(component, stack, innerWidth);
        foreach (SemanticComponent child in component.Children)
        {
            BuiltView built = Build(
                child,
                input,
                innerWidth,
                depth + 1,
                outline,
                interact);
            stack.Add(built.View, built.Height);
            if (ComfortableSpacing)
            {
                stack.Space();
            }
        }
        if (component.Children.Count == 0)
        {
            const String unavailable = "Preview unavailable";
            stack.Add(
                TextLabel(unavailable, innerWidth, "Muted"),
                WrappedHeight(unavailable, innerWidth));
        }
        FrameView frame = new FrameView
        {
            Title = title,
            Width = width,
            Height = Math.Max(3, stack.Height + 2),
            CanFocus = true,
            TabStop = TabBehavior.NoStop
        };
        stack.View.Width = Dim.Fill();
        stack.View.Height = Math.Max(1, stack.Height);
        frame.Add(stack.View);
        return new BuiltView(frame, Math.Max(3, stack.Height + 2));
    }

    private static Boolean IsRedundantSectionText(
        SemanticComponent parent,
        SemanticComponent child)
    {
        if (child.Kind is not LumuiProtocol.ComponentKinds.Text
            and not LumuiProtocol.ComponentKinds.RichText)
        {
            return false;
        }
        String value = PlainText(child.Text.Length > 0 ? child.Text : child.Label).Trim();
        if (value.Length == 0)
        {
            return true;
        }
        if (parent.Label.Length > 0
            && String.Equals(value, PlainText(parent.Label).Trim(), StringComparison.CurrentCultureIgnoreCase))
        {
            return true;
        }
        String description = PlainText(Text(parent.Element, LumuiProtocol.Fields.Description)).Trim();
        return description.Length > 0
            && String.Equals(value, description, StringComparison.CurrentCultureIgnoreCase);
    }

    private static Label Heading(String value, Int32 width, Boolean page)
    {
        String heading = page ? value.ToUpperInvariant() : value;
        return new Label
        {
            Text = Wrap(heading, width),
            Width = width,
            Height = WrappedHeight(heading, width),
            SchemeName = "Accent"
        };
    }

    private static Label TextLabel(String value, Int32 width, String scheme)
    {
        String wrapped = Wrap(value, width);
        return new Label
        {
            Text = wrapped,
            Width = width,
            Height = Math.Max(1, NormalizeLines(wrapped).Count),
            SchemeName = scheme
        };
    }

    private static void AddDescription(SemanticComponent component, StackBuilder stack, Int32 width)
    {
        String description = Text(component.Element, LumuiProtocol.Fields.Description);
        if (description.Length > 0)
        {
            stack.Add(TextLabel(description, width, "Muted"), WrappedHeight(description, width));
        }
    }

    private static void AddFields(
        SemanticComponent component,
        StackBuilder stack,
        Int32 width,
        params String[] fields)
    {
        List<String> details = new List<String>();
        foreach (String field in fields)
        {
            if (component.Element.TryGetProperty(field, out JsonElement value))
            {
                String displayed = Display(value);
                if (displayed.Length > 0)
                {
                    details.Add(Humanize(field) + "  " + displayed);
                }
            }
        }
        if (details.Count > 0)
        {
            String text = String.Join(Environment.NewLine, details);
            stack.Add(TextLabel(text, width, "Muted"), WrappedHeight(text, width));
        }
    }

    private static Object? Current(SemanticComponent component, IDictionary<String, Object?> input) =>
        component.Id.Length > 0 && input.TryGetValue(component.Id, out Object? value)
            ? value
            : Property(component.Element, LumuiProtocol.Fields.Value);

    private static Object? Property(JsonElement element, String name) =>
        element.TryGetProperty(name, out JsonElement value) ? JsonValue(value) : null;

    private static Object? JsonValue(JsonElement value) => value.ValueKind switch
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

    private static Object InputValue(String kind, String value)
    {
        if (kind == LumuiProtocol.ComponentKinds.NumberField
            && Double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out Double number))
        {
            return number;
        }
        return value;
    }

    private static String DisplayValue(Object? value, String kind)
    {
        if (value is null)
        {
            return String.Empty;
        }
        if (kind == LumuiProtocol.ComponentKinds.PasswordField)
        {
            return Convert.ToString(value, CultureInfo.CurrentCulture) ?? String.Empty;
        }
        if (value is JsonElement element)
        {
            return Display(element);
        }
        if (value is System.Collections.IEnumerable values && value is not String)
        {
            List<String> items = new List<String>();
            foreach (Object? item in values)
            {
                items.Add(Convert.ToString(item, CultureInfo.CurrentCulture) ?? String.Empty);
            }
            return String.Join(", ", items);
        }
        return Convert.ToString(value, CultureInfo.CurrentCulture) ?? String.Empty;
    }

    private static HashSet<String> SelectedValues(Object? value)
    {
        HashSet<String> result = new HashSet<String>(StringComparer.Ordinal);
        if (value is System.Collections.IEnumerable values && value is not String)
        {
            foreach (Object? item in values)
            {
                result.Add(ValueIdentity(item));
            }
        }
        else if (value is not null)
        {
            result.Add(ValueIdentity(value));
        }
        return result;
    }

    private static String ValueIdentity(Object? value) => value switch
    {
        null => String.Empty,
        JsonElement element => element.GetRawText(),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? String.Empty
    };

    private static Boolean BooleanValue(Object? value) => value switch
    {
        Boolean boolean => boolean,
        String text when Boolean.TryParse(text, out Boolean parsed) => parsed,
        JsonElement element when element.ValueKind == JsonValueKind.True => true,
        _ => false
    };

    private static Double NumberValue(Object? value, Double fallback) => value switch
    {
        Double number => number,
        Single number => number,
        Decimal number => (Double)number,
        Int32 number => number,
        Int64 number => number,
        String text when Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out Double parsed) => parsed,
        _ => fallback
    };

    private static Double Number(JsonElement element, String field, Double fallback) =>
        element.TryGetProperty(field, out JsonElement value) && value.TryGetDouble(out Double number)
            ? number
            : fallback;

    private static String RowValue(JsonElement row, String key)
    {
        if (row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out JsonElement value))
        {
            return Display(value);
        }
        return String.Empty;
    }

    private static String Fit(String value, Int32 width)
    {
        String text = value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        if (text.Length > width)
        {
            return width <= 1 ? "…" : text[..(width - 1)] + "…";
        }
        return text.PadRight(width);
    }

    private static String Describe(JsonElement element)
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
                .Take(8)
                .Select(property => Humanize(property.Name) + ": " + Display(property.Value)));
    }

    private static String Display(JsonElement value) => value.ValueKind switch
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

    private static Int32 WrappedHeight(String value, Int32 width) => Math.Max(1, NormalizeLines(Wrap(value, width)).Count);

    private static String Wrap(String value, Int32 width)
    {
        Int32 lineWidth = Math.Max(8, width);
        List<String> output = new List<String>();
        foreach (String sourceLine in NormalizeLines(value))
        {
            if (sourceLine.Length == 0)
            {
                output.Add(String.Empty);
                continue;
            }
            String remaining = sourceLine;
            while (remaining.Length > lineWidth)
            {
                Int32 split = remaining.LastIndexOf(' ', lineWidth);
                if (split < lineWidth / 3)
                {
                    split = lineWidth;
                }
                output.Add(remaining[..split].TrimEnd());
                remaining = remaining[split..].TrimStart();
            }
            output.Add(remaining);
        }
        return String.Join(Environment.NewLine, output);
    }

    private static List<String> NormalizeLines(String value) => value
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n')
        .Split('\n')
        .Select(line => line.TrimEnd())
        .ToList();

    private static String PlainText(String value)
    {
        StringBuilder output = new StringBuilder(value.Length);
        StringBuilder tagValue = new StringBuilder();
        Boolean tag = false;
        foreach (Char character in value)
        {
            if (character == '<')
            {
                tag = true;
                tagValue.Clear();
            }
            else if (tag && character == '>')
            {
                tag = false;
                String name = tagValue.ToString().Trim().ToLowerInvariant();
                if (name.StartsWith("li", StringComparison.Ordinal))
                {
                    AppendLineBreak(output);
                    output.Append("• ");
                }
                else if (name.StartsWith("br", StringComparison.Ordinal)
                    || name.StartsWith("/p", StringComparison.Ordinal)
                    || name.StartsWith("/div", StringComparison.Ordinal)
                    || name.StartsWith("/li", StringComparison.Ordinal)
                    || name.StartsWith("/h", StringComparison.Ordinal)
                    || name.StartsWith("/blockquote", StringComparison.Ordinal)
                    || name.StartsWith("/section", StringComparison.Ordinal)
                    || name.StartsWith("/tr", StringComparison.Ordinal))
                {
                    AppendLineBreak(output);
                }
                else if (name.StartsWith("/td", StringComparison.Ordinal)
                    || name.StartsWith("/th", StringComparison.Ordinal))
                {
                    output.Append("  ");
                }
            }
            else if (tag)
            {
                tagValue.Append(character);
            }
            else
            {
                output.Append(character);
            }
        }
        String decoded = System.Net.WebUtility.HtmlDecode(output.ToString());
        List<String> lines = NormalizeLines(decoded);
        List<String> compact = new List<String>();
        Boolean empty = true;
        foreach (String line in lines)
        {
            Boolean currentEmpty = String.IsNullOrWhiteSpace(line);
            if (!currentEmpty || !empty)
            {
                compact.Add(currentEmpty ? String.Empty : line.Trim());
            }
            empty = currentEmpty;
        }
        while (compact.Count > 0 && compact[^1].Length == 0)
        {
            compact.RemoveAt(compact.Count - 1);
        }
        return String.Join(Environment.NewLine, compact).Trim();
    }

    private static void AppendLineBreak(StringBuilder output)
    {
            if (output.Length > 0 && output[output.Length - 1] != '\n')
        {
            output.AppendLine();
        }
    }

    private static String Bionic(String value)
    {
        StringBuilder output = new StringBuilder(value.Length);
        Int32 position = 0;
        foreach (Char character in value)
        {
            if (!Char.IsLetterOrDigit(character))
            {
                output.Append(character);
                position = 0;
            }
            else
            {
                output.Append(position < 2 ? Char.ToUpperInvariant(character) : character);
                position++;
            }
        }
        return output.ToString();
    }

    private static String Humanize(String value)
    {
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
