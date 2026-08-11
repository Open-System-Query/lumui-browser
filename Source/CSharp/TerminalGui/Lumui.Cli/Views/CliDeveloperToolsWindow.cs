using Lumui.Cli.Configuration;
using Lumui.Cli.Diagnostics;
using Lumui.Cli.Navigation;

namespace Lumui.Cli.Views;

public sealed class CliDeveloperToolsWindow : Dialog
{
    private readonly CliBrowserServices _services;
    private readonly CliTabSession _session;
    private readonly ReadOnlyTextPane _source;
    private readonly ReadOnlyTextPane _network;

    public CliDeveloperToolsWindow(
        CliBrowserServices services,
        CliTabSession session,
        String initialSection = "Overview")
    {
        _services = services;
        _session = session;
        Title = "Tools | LUMUI Browser";
        Width = Dim.Percent(94);
        Height = Dim.Percent(92);

        Tabs tabs = new Tabs
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(3)
        };
        LoadedSurface? loaded = session.Loaded;
        if (loaded is null)
        {
            _source = TextPage("Source", "No surface is loaded.");
            _network = TextPage("Network", SurfaceDiagnostics.Network(services.RequestMonitor.Snapshot()));
            tabs.Add(
                TextPage("Overview", "No surface is loaded."),
                _source,
                _network,
                TextPage("Diagnostics", RuntimeDiagnostics(services.IsPrivate)));
        }
        else
        {
            JsonElement root = loaded.Document.RootElement;
            _source = TextPage("Source", SurfaceDiagnostics.FormattedSource(loaded));
            _network = TextPage("Network", SurfaceDiagnostics.Network(services.RequestMonitor.Snapshot()));
            tabs.Add(
                TextPage("Overview", SurfaceDiagnostics.Overview(loaded, session.LoadDuration)),
                _source,
                TextPage("Structure", SurfaceDiagnostics.Structure(root)),
                _network,
                TextPage("Problems", SurfaceDiagnostics.Problems(root)),
                TextPage("Accessibility", SurfaceDiagnostics.Accessibility(root)),
                TextPage("Actions", SurfaceDiagnostics.Actions(root)),
                TextPage("Diagnostics", RuntimeDiagnostics(services.IsPrivate)));
        }
        View? initial = tabs.TabCollection.FirstOrDefault(
            page => page.Title.Equals(initialSection, StringComparison.OrdinalIgnoreCase));
        if (initial is not null)
        {
            tabs.Value = initial;
        }

        View commandBar = new View
        {
            X = 1,
            Y = Pos.AnchorEnd(3),
            Width = Dim.Fill(2),
            Height = 2,
            SchemeName = "Menu",
            CanFocus = true,
            TabStop = TabBehavior.NoStop
        };
        Button find = new CliButton
        {
            Text = "Find",
            Width = 8
        };
        Button raw = new CliButton
        {
            Text = "Raw",
            Width = 7
        };
        Button format = new CliButton
        {
            Text = "Format",
            Width = 10
        };
        Button save = new CliButton
        {
            Text = "Save source",
            Width = 15
        };
        Button refresh = new CliButton
        {
            Text = "Refresh",
            Width = 11
        };
        Button close = new CliButton
        {
            Text = "Close",
            Width = 9,
            IsDefault = true,
            SchemeName = "Accent"
        };
        find.Accepting += (_, _) => FindSource();
        raw.Accepting += (_, _) =>
        {
            if (_session.Loaded is not null)
            {
                _source.SetContent(_session.Loaded.Source);
            }
        };
        format.Accepting += (_, _) =>
        {
            if (_session.Loaded is not null)
            {
                _source.SetContent(SurfaceDiagnostics.FormattedSource(_session.Loaded));
            }
        };
        save.Accepting += (_, _) => SaveSource();
        refresh.Accepting += (_, _) =>
        {
            _network.SetContent(SurfaceDiagnostics.Network(_services.RequestMonitor.Snapshot()));
        };
        close.Accepting += (_, _) => App?.RequestStop(this);
        commandBar.Add(find, raw, format, save, refresh, close);
        commandBar.FrameChanged += (_, _) => LayoutCommands(commandBar);
        Add(tabs, commandBar);
        LayoutCommands(commandBar);
    }

    private void FindSource()
    {
        if (App is null)
        {
            return;
        }
        String? query = CliDialogs.Prompt(App, "Find in source", "Text");
        if (!String.IsNullOrWhiteSpace(query) && !_source.Find(query))
        {
            CliDialogs.Show(App, "Find in source", "No matching text was found.");
        }
    }

    private static void LayoutCommands(View bar)
    {
        Int32 available = Math.Max(1, bar.Viewport.Width);
        Int32 x = 0;
        Int32 y = 0;
        foreach (View button in bar.SubViews)
        {
            Int32 width = button.Text.Length + 4;
            if (x > 0 && x + width > available)
            {
                x = 0;
                y++;
            }
            button.Visible = y < 2;
            button.X = x;
            button.Y = y;
            button.Width = Math.Min(width, available);
            x += width + 1;
        }
    }

    private void SaveSource()
    {
        if (App is null || _session.Loaded is null)
        {
            return;
        }
        String proposed = Path.Combine(
            _services.Preferences.DownloadFolder,
            "lumui-source-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json");
        String? path = CliDialogs.Prompt(App, "Save source", "File path", proposed);
        if (String.IsNullOrWhiteSpace(path))
        {
            return;
        }
        try
        {
            String? folder = Path.GetDirectoryName(path);
            if (!String.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }
            File.WriteAllText(path, _source.Content);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            CliDialogs.Show(App, "Save source", exception.Message);
        }
    }

    private static ReadOnlyTextPane TextPage(String title, String text)
    {
        ReadOnlyTextPane pane = new ReadOnlyTextPane
        {
            Title = title
        };
        pane.SetContent(text);
        return pane;
    }

    private static String RuntimeDiagnostics(Boolean privateMode)
    {
        StringBuilder output = new StringBuilder();
        output.AppendLine("LUMUI Browser for Terminal.Gui");
        output.AppendLine(DiagnosticRow("Runtime", Environment.Version));
        output.AppendLine(DiagnosticRow("Operating system", Environment.OSVersion));
        output.AppendLine(DiagnosticRow("Architecture", RuntimeInformation.ProcessArchitecture));
        output.AppendLine(DiagnosticRow(
            "Terminal",
            Environment.GetEnvironmentVariable("TERM_PROGRAM")
                ?? Environment.GetEnvironmentVariable("TERM")
                ?? "Windows Console"));
        output.AppendLine(DiagnosticRow("Private mode", privateMode ? "enabled" : "disabled"));
        output.AppendLine(DiagnosticRow("Renderer", "LUMUI semantic terminal renderer"));
        output.AppendLine(DiagnosticRow("Media", "cached PGM frames and PCM audio"));
        return output.ToString();
    }

    private static String DiagnosticRow(String label, Object value) =>
        label.PadRight(22) + Convert.ToString(value, CultureInfo.CurrentCulture);
}
