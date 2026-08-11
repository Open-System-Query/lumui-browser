using System.Text.Json;
using LumuiProtocol = Lumui.Client.LumuiProtocol;

namespace Lumui.Client;

public sealed class ActionResult : IDisposable
{
    public ActionResult(JsonDocument document, Uri responseUri)
    {
        Document = document;
        ResponseUri = responseUri;
    }

    public JsonDocument Document { get; }

    public Uri ResponseUri { get; }

    public String Status =>
        Document.RootElement.TryGetProperty(LumuiProtocol.Fields.Status, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? LumuiProtocol.ActionStatuses.Failed
            : LumuiProtocol.ActionStatuses.Failed;

    public String CorrelationId =>
        Document.RootElement.TryGetProperty(LumuiProtocol.Fields.CorrelationId, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? String.Empty
            : String.Empty;

    public String? ConfirmationToken()
    {
        if (Document.RootElement.TryGetProperty(LumuiProtocol.Fields.Confirmation, out JsonElement confirmation)
            && confirmation.ValueKind == JsonValueKind.Object
            && confirmation.TryGetProperty(LumuiProtocol.Fields.Token, out JsonElement token)
            && token.ValueKind == JsonValueKind.String)
        {
            return token.GetString();
        }
        return null;
    }

    public Uri? SurfaceUri(Uri baseUri)
    {
        if (!Document.RootElement.TryGetProperty(LumuiProtocol.Fields.Surface, out JsonElement surface)
            || surface.ValueKind != JsonValueKind.Object
            || !surface.TryGetProperty(LumuiProtocol.Fields.Href, out JsonElement href))
        {
            return null;
        }
        String? value = href.ValueKind == JsonValueKind.String ? href.GetString() : null;
        return String.IsNullOrWhiteSpace(value) ? null : new Uri(baseUri, value);
    }

    public Uri? RedirectUri(Uri baseUri)
    {
        if (!Document.RootElement.TryGetProperty(LumuiProtocol.Fields.Redirect, out JsonElement redirect))
        {
            return null;
        }
        String? value = redirect.ValueKind == JsonValueKind.String ? redirect.GetString() : null;
        return String.IsNullOrWhiteSpace(value) ? null : new Uri(baseUri, value);
    }

    public Uri? StatusUri()
    {
        if (!Document.RootElement.TryGetProperty(LumuiProtocol.Fields.StatusResource, out JsonElement statusResource)
            || statusResource.ValueKind != JsonValueKind.Object
            || !statusResource.TryGetProperty(LumuiProtocol.Fields.Href, out JsonElement href)
            || href.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        String? value = href.GetString();
        return String.IsNullOrWhiteSpace(value) ? null : new Uri(ResponseUri, value);
    }

    public TimeSpan PollDelay()
    {
        if (Document.RootElement.TryGetProperty(LumuiProtocol.Fields.StatusResource, out JsonElement statusResource)
            && statusResource.ValueKind == JsonValueKind.Object
            && statusResource.TryGetProperty(LumuiProtocol.Fields.PollAfterMilliseconds, out JsonElement delay)
            && delay.TryGetInt32(out Int32 milliseconds))
        {
            return TimeSpan.FromMilliseconds(Math.Clamp(milliseconds, 250, 60_000));
        }
        return TimeSpan.FromSeconds(1);
    }

    public DateTimeOffset? StatusExpiration()
    {
        if (Document.RootElement.TryGetProperty(LumuiProtocol.Fields.StatusResource, out JsonElement statusResource)
            && statusResource.ValueKind == JsonValueKind.Object
            && statusResource.TryGetProperty(LumuiProtocol.Fields.ExpiresAt, out JsonElement expires)
            && expires.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                expires.GetString(),
                null,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out DateTimeOffset value))
        {
            return value.ToUniversalTime();
        }
        return null;
    }

    public String? Message()
    {
        if (Document.RootElement.TryGetProperty(LumuiProtocol.Fields.Result, out JsonElement result)
            && result.ValueKind == JsonValueKind.Object
            && result.TryGetProperty(LumuiProtocol.Fields.Message, out JsonElement message)
            && message.ValueKind == JsonValueKind.String)
        {
            return message.GetString();
        }
        return null;
    }

    public void Dispose() => Document.Dispose();
}
