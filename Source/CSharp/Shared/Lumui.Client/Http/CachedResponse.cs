using System.Net.Http.Headers;

namespace Lumui.Client;

internal sealed record CachedResponse(
    Byte[] Bytes,
    String ContentType,
    Uri FinalUri,
    EntityTagHeaderValue? EntityTag,
    Boolean Validated = false);
