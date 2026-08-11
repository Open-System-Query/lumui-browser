using System.Text.Json;
using LumuiProtocol = Lumui.Client.LumuiProtocol;

namespace Lumui.Client;

public sealed class SurfaceActionPolicy
{
    private SurfaceActionPolicy(String confirmation)
    {
        Confirmation = confirmation;
    }

    public String Confirmation { get; }

    public static SurfaceActionPolicy FromSurface(
        JsonElement surface,
        String actionId)
    {
        if (surface.ValueKind != JsonValueKind.Object
            || !surface.TryGetProperty(
                LumuiProtocol.Fields.Actions,
                out JsonElement actions)
            || actions.ValueKind != JsonValueKind.Object
            || !actions.TryGetProperty(actionId, out JsonElement action)
            || action.ValueKind != JsonValueKind.Object
            || !action.TryGetProperty(
                LumuiProtocol.Fields.Confirmation,
                out JsonElement confirmation)
            || confirmation.ValueKind != JsonValueKind.String)
        {
            return new SurfaceActionPolicy(
                LumuiProtocol.ConfirmationPolicies.None);
        }

        return new SurfaceActionPolicy(
            confirmation.GetString()
                ?? LumuiProtocol.ConfirmationPolicies.None);
    }
}
