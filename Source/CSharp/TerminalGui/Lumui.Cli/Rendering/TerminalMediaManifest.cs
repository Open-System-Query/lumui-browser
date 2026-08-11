namespace Lumui.Cli.Rendering;

internal sealed class TerminalMediaManifest
{
    public Boolean HasVideo { get; init; }

    public String[] Frames { get; init; } = Array.Empty<String>();

    public Double FrameRate { get; init; }

    public String? AudioFile { get; init; }

    public Int64 DurationTicks { get; init; }
}
