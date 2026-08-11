using Lumui.Cli.Configuration;
using Lumui.Cli.Views;

namespace Lumui.Cli;

public static class Program
{
    public static Int32 Main(String[] args)
    {
        if (args.Any(value => value is "--help" or "-h" or "/?"))
        {
            Console.WriteLine("Lumi CLI");
            Console.WriteLine("Usage: lumi [--cli] [address] [--private]");
            Console.WriteLine("Standalone development host: lumi-cli [address] [--private]");
            Console.WriteLine("Ctrl+L address  Ctrl+T new tab  Ctrl+W close  Alt+Left/Right history");
            Console.WriteLine("Tab/Shift+Tab and arrows move focus  PageUp/PageDown scroll");
            Console.WriteLine("Enter/Space activates  F1 help  F12 tools  Ctrl+Q quit");
            return 0;
        }

        Boolean privateMode = args.Any(value => value.Equals("--private", StringComparison.OrdinalIgnoreCase));
        String? address = args.FirstOrDefault(value => !value.StartsWith("-", StringComparison.Ordinal));

        using CliBrowserServices services = new CliBrowserServices(privateMode);
        using IApplication app = Application.Create();
        if (UseTrueColorAnsiDriver())
        {
            app.Init(Terminal.Gui.Drivers.DriverRegistry.Names.ANSI);
        }
        else
        {
            app.Init();
        }
        CliTheme.Apply(services.Preferences);
        using CliMainWindow window = new CliMainWindow(services, address);
        app.Run(window);
        window.SaveState();
        return 0;
    }

    private static Boolean UseTrueColorAnsiDriver() => OperatingSystem.IsWindows()
        && (!String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WT_SESSION"))
            || !String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TERM_PROGRAM"))
            || !String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANSICON"))
            || Environment.GetEnvironmentVariable("ConEmuANSI")?.Equals(
                "ON",
                StringComparison.OrdinalIgnoreCase) == true);
}
