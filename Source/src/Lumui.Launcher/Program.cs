using System.Runtime.InteropServices;

namespace Lumui.Launcher;

internal static class Program
{
    private const String CliArgument = "--cli";
    private const String DesktopArgument = "--desktop";
    private const Int32 HideWindow = 0;

    [STAThread]
    private static Int32 Main(String[] args)
    {
        Boolean forceCli = HasArgument(args, CliArgument);
        Boolean forceDesktop = HasArgument(args, DesktopArgument);
        String[] forwarded = args
            .Where(value => !value.Equals(CliArgument, StringComparison.OrdinalIgnoreCase)
                && !value.Equals(DesktopArgument, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (forceCli || (!forceDesktop && HasCallingTerminal()))
        {
            return Lumui.Cli.Program.Main(forwarded);
        }

        DetachStartupConsole();
        return Lumui.Browser.Program.Run(forwarded);
    }

    private static Boolean HasArgument(IEnumerable<String> args, String expected) =>
        args.Any(value => value.Equals(expected, StringComparison.OrdinalIgnoreCase));

    private static Boolean HasCallingTerminal()
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected || Console.IsErrorRedirected)
        {
            return true;
        }
        UInt32[] processes = new UInt32[4];
        return GetConsoleProcessList(processes, (UInt32)processes.Length) > 1;
    }

    private static void DetachStartupConsole()
    {
        IntPtr window = GetConsoleWindow();
        if (window != IntPtr.Zero)
        {
            _ = ShowWindow(window, HideWindow);
        }
        _ = FreeConsole();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UInt32 GetConsoleProcessList(
        [Out] UInt32[] processList,
        UInt32 processCount);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern Boolean FreeConsole();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern Boolean ShowWindow(IntPtr window, Int32 command);
}
