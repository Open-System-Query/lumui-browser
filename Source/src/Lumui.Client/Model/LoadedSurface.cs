using System.Text.Json;
using System.Net.Http.Headers;

namespace Lumui.Client;

public sealed class LoadedSurface : IDisposable
{
    public LoadedSurface(
        Uri address,
        Uri surfaceUri,
        JsonDocument document,
        String source,
        Uri? descriptorUri,
        Uri? actionUri,
        EntityTagHeaderValue? entityTag)
    {
        Address = address;
        SurfaceUri = surfaceUri;
        Document = document;
        Source = source;
        DescriptorUri = descriptorUri;
        ActionUri = actionUri;
        EntityTag = entityTag;
    }

    public Uri Address { get; }

    public Uri SurfaceUri { get; }

    public JsonDocument Document { get; }

    public String Source { get; }

    public Uri? DescriptorUri { get; }

    public Uri? ActionUri { get; }

    public EntityTagHeaderValue? EntityTag { get; }

    public void Dispose() => Document.Dispose();
}
