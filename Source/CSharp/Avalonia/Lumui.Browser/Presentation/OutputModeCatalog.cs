using LumuiProtocol = Lumui.Client.LumuiProtocol;

namespace Lumui.Browser.Presentation;

public static class OutputModeCatalog
{
    public static readonly OutputModeDefinition Visual =
        new OutputModeDefinition(
            LumuiProtocol.OutputModes.Visual,
            "Visual",
            OutputMode.Visual);

    public static readonly OutputModeDefinition ScreenReader =
        new OutputModeDefinition(
            LumuiProtocol.OutputModes.ScreenReader,
            "Screen reader",
            OutputMode.ScreenReader);

    public static IReadOnlyList<OutputModeDefinition> All { get; } =
        Array.AsReadOnly(new OutputModeDefinition[]
        {
            Visual,
            ScreenReader
        });
}
