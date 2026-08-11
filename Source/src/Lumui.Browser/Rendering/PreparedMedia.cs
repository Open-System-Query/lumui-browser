namespace Lumui.Browser.Rendering;

internal sealed record PreparedMedia(
    Boolean HasVideo,
    IReadOnlyList<String> Frames,
    Double FrameRate,
    String? AudioPath,
    TimeSpan Duration);

internal sealed class PreparedMediaManifest
{
    public Boolean HasVideo { get; init; }

    public String[] Frames { get; init; } = Array.Empty<String>();

    public Double FrameRate { get; init; }

    public String? AudioFile { get; init; }

    public Int64 DurationTicks { get; init; }
}

internal sealed record MediaPreparationProgress(
    String Stage,
    Int32? Percentage);
