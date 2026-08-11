using LumuiProtocol = Lumui.Client.LumuiProtocol;

namespace Lumui.Browser.Presentation;

public static class InteractionModeCatalog
{
    public static readonly InteractionModeDefinition Standard =
        new InteractionModeDefinition(
            LumuiProtocol.InteractionModes.Standard,
            "Standard",
            InteractionMode.Standard);

    public static readonly InteractionModeDefinition Guided =
        new InteractionModeDefinition(
            LumuiProtocol.InteractionModes.Guided,
            "Guided",
            InteractionMode.Guided);

    public static IReadOnlyList<InteractionModeDefinition> All { get; } =
        Array.AsReadOnly(new InteractionModeDefinition[]
        {
            Standard,
            Guided
        });
}
