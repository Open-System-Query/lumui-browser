namespace Lumui.Cli.Rendering;

public sealed record PreparedTerminalMedia(
    Boolean HasVideo,
    IReadOnlyList<String> Frames,
    Double FrameRate,
    String? AudioPath,
    TimeSpan Duration);
