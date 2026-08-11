using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LumuiProtocol = Lumui.Client.LumuiProtocol;

namespace Lumui.Client;

public sealed class LumuiClient : IDisposable
{
    public const String MediaType = LumuiProtocol.MediaTypes.LumuiJson;
    public const Int32 MaximumDocumentBytes = 1_048_576;
    public const Int32 MaximumAssetBytes = 10_485_760;
    private const Int32 MaximumDiscoveryHops = 8;

    private readonly HttpClient _http;
    private readonly CookieContainer _cookies;
    private readonly Lazy<LumuiDocumentValidator> _validator;
    private readonly Lazy<Task> _validatorWarmup;
    private readonly ILumuiClientObserver _observer;
    private readonly ConcurrentDictionary<String, CachedResponse> _cache =
        new ConcurrentDictionary<String, CachedResponse>(StringComparer.Ordinal);
    private Boolean _disposed;

    public LumuiClient(
        LumuiDocumentValidator? validator = null,
        ILumuiClientObserver? observer = null,
        CookieContainer? cookies = null)
    {
        _validator = new Lazy<LumuiDocumentValidator>(
            () => validator ?? LumuiDocumentValidator.CreateDefault(),
            true);
        _validatorWarmup = new Lazy<Task>(
            () => Task.Run(() =>
            {
                _ = _validator.Value;
            }),
            true);
        _observer = observer ?? NullLumuiClientObserver.Instance;
        _cookies = cookies ?? new CookieContainer();
        SocketsHttpHandler handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            CookieContainer = _cookies,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            UseCookies = true
        };
        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        Version? assemblyVersion = typeof(LumuiClient).Assembly.GetName().Version;
        String productVersion = assemblyVersion is null
            ? LumuiProtocol.Versions.Web
            : assemblyVersion.ToString(3);
        String productName = typeof(LumuiClient).Assembly.GetName().Name
            ?? nameof(LumuiClient);
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(productName, productVersion));
    }

    public void SetDoNotTrack(Boolean enabled)
    {
        _http.DefaultRequestHeaders.Remove("DNT");
        if (enabled)
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("DNT", "1");
        }
    }

    public Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        Task warmup = _validatorWarmup.Value;
        return cancellationToken.CanBeCanceled
            ? warmup.WaitAsync(cancellationToken)
            : warmup;
    }

    public void ClearBrowsingData()
    {
        _cache.Clear();
        foreach (Cookie cookie in _cookies.GetAllCookies())
        {
            cookie.Expired = true;
        }
    }

    public static Uri NormalizeAddress(String value)
    {
        String text = value.Trim();
        if (text.Length == 0)
        {
            throw new LumuiProtocolException("Enter a website address.");
        }
        if (!text.Contains(Uri.SchemeDelimiter, StringComparison.Ordinal))
        {
            text = Uri.UriSchemeHttps + Uri.SchemeDelimiter + text;
        }
        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri))
        {
            throw new LumuiProtocolException("The address is not valid.");
        }
        EnsureTransport(uri);
        return uri;
    }

    public Task<LoadedSurface> LoadAsync(Uri address, CancellationToken cancellationToken = default)
    {
        EnsureTransport(address);
        return LoadCoreAsync(
            address,
            address,
            null,
            null,
            null,
            new HashSet<String>(StringComparer.Ordinal),
            0,
            cancellationToken);
    }

    public Task<LoadedSurface> LoadRepresentationAsync(
        Uri representation,
        Uri logicalAddress,
        CancellationToken cancellationToken = default)
    {
        EnsureTransport(representation);
        EnsureTransport(logicalAddress);
        EnsureSameOrigin(
            logicalAddress,
            representation,
            LumuiProtocol.Fields.Surface);
        return LoadCoreAsync(
            representation,
            logicalAddress,
            null,
            null,
            null,
            new HashSet<String>(StringComparer.Ordinal),
            0,
            cancellationToken);
    }

    public async Task<ActionResult> InvokeAsync(
        LoadedSurface loaded,
        String componentId,
        String actionId,
        IReadOnlyDictionary<String, Object?> input,
        String renderProfile,
        String inputMethod,
        Boolean confirmed = false,
        String? messageId = null,
        String? confirmationToken = null,
        CancellationToken cancellationToken = default)
    {
        Uri? endpoint = SurfaceRelation(
            loaded.Document.RootElement,
            loaded.SurfaceUri,
            LumuiProtocol.Relations.Actions) ?? loaded.ActionUri;
        if (endpoint is null)
        {
            throw new LumuiProtocolException("This surface does not advertise an action endpoint.");
        }
        EnsureSameOrigin(loaded.SurfaceUri, endpoint, "action endpoint");

        JsonElement root = loaded.Document.RootElement;
        String surfaceId = RequiredString(root, LumuiProtocol.Fields.SurfaceId);
        if (!root.TryGetProperty(LumuiProtocol.Fields.Revision, out JsonElement revision)
            || !revision.TryGetInt32(out Int32 revisionValue))
        {
            throw new LumuiProtocolException("The surface revision is missing.");
        }

        messageId ??= Guid.NewGuid().ToString();
        if (confirmed && String.IsNullOrWhiteSpace(confirmationToken))
        {
            throw new LumuiProtocolException("The confirmation challenge is missing.");
        }
        ArrayBufferWriter<Byte> buffer = new ArrayBufferWriter<Byte>();
        using (Utf8JsonWriter writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString(LumuiProtocol.Fields.LumuiMessage, LumuiProtocol.Versions.Message);
            writer.WriteString(LumuiProtocol.Fields.MessageId, messageId);
            writer.WriteString(LumuiProtocol.Fields.MessageType, LumuiProtocol.MessageTypes.ActionInvoke);
            writer.WriteString(LumuiProtocol.Fields.SurfaceId, surfaceId);
            writer.WriteNumber(LumuiProtocol.Fields.Revision, revisionValue);
            writer.WriteString(LumuiProtocol.Fields.ComponentId, componentId);
            writer.WriteString(LumuiProtocol.Fields.ActionId, actionId);
            writer.WritePropertyName(LumuiProtocol.Fields.Input);
            WriteDictionary(writer, input);
            if (confirmed)
            {
                writer.WriteBoolean(LumuiProtocol.Fields.Confirmed, true);
                writer.WriteString(LumuiProtocol.Fields.ConfirmationToken, confirmationToken);
            }
            writer.WriteStartObject(LumuiProtocol.Fields.Source);
            writer.WriteString(LumuiProtocol.Fields.Kind, LumuiProtocol.Sources.User);
            writer.WriteString(
                LumuiProtocol.Fields.RenderProfile,
                renderProfile);
            writer.WriteString(
                LumuiProtocol.Fields.InputMethod,
                inputMethod);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaType));
        request.Headers.TryAddWithoutValidation(HttpHeaderNames.IdempotencyKey, messageId);
        request.Headers.TryAddWithoutValidation(
            HttpHeaderNames.Origin,
            Origin(loaded.SurfaceUri));
        if (loaded.EntityTag is { IsWeak: false })
        {
            request.Headers.IfMatch.Add(loaded.EntityTag);
        }
        request.Content = new ByteArrayContent(buffer.WrittenSpan.ToArray());
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(MediaType)
        {
            CharSet = Encoding.UTF8.WebName
        };

        using HttpResponseMessage response = await SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (IsRedirect(response.StatusCode))
        {
            throw new LumuiProtocolException(
                "The action endpoint redirected the request. The publisher must advertise its final same-origin endpoint.");
        }
        Byte[] bytes = await ReadLimitedAsync(response.Content, MaximumDocumentBytes, cancellationToken).ConfigureAwait(false);
        Uri responseUri = response.RequestMessage?.RequestUri ?? endpoint;
        EnsureSameOrigin(endpoint, responseUri, "action response");
        String responseType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? String.Empty;
        if (responseType != MediaType && responseType != LumuiProtocol.MediaTypes.ProblemJson)
        {
            throw new LumuiProtocolException(
                $"The action returned unsupported content type '{responseType}'.");
        }
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (JsonException exception)
        {
            throw new LumuiProtocolException("The action response is not valid JSON.", exception);
        }
        if (!response.IsSuccessStatusCode)
        {
            String? title = document.RootElement.TryGetProperty(
                LumuiProtocol.Fields.Title,
                out JsonElement titleValue)
                && titleValue.ValueKind == JsonValueKind.String
                ? titleValue.GetString()
                : null;
            document.Dispose();
            throw new LumuiProtocolException(title ?? $"The action failed with HTTP {(Int32)response.StatusCode}.");
        }
        String resolvedStatus;
        try
        {
            resolvedStatus = ValidateActionResult(document.RootElement, messageId);
        }
        catch
        {
            document.Dispose();
            throw;
        }
        if (resolvedStatus is
            LumuiProtocol.ActionStatuses.Failed or
            LumuiProtocol.ActionStatuses.Rejected or
            LumuiProtocol.ActionStatuses.RequiresPermission)
        {
            String? message = ActionFailureMessage(document.RootElement);
            document.Dispose();
            throw new LumuiProtocolException(message ?? "The action was not completed.");
        }
        return new ActionResult(document, responseUri);
    }

    public async Task<ActionResult> WaitForCompletionAsync(
        ActionResult accepted,
        CancellationToken cancellationToken = default)
    {
        if (accepted.Status != LumuiProtocol.ActionStatuses.AcceptedAsync)
        {
            throw new LumuiProtocolException("Only an accepted asynchronous action can be monitored.");
        }

        String correlationId = accepted.CorrelationId;
        ActionResult current = accepted;
        Boolean ownsCurrent = false;
        DateTimeOffset hostDeadline = DateTimeOffset.UtcNow.AddMinutes(5);
        TimeSpan? retryAfter = null;
        try
        {
            for (Int32 poll = 0; poll < 100; poll++)
            {
                Uri statusUri = current.StatusUri()
                    ?? throw new LumuiProtocolException(
                        "The asynchronous action did not advertise a status resource.");
                EnsureTransport(statusUri);
                EnsureSameOrigin(accepted.ResponseUri, statusUri, "action status resource");

                DateTimeOffset? expiration = current.StatusExpiration();
                DateTimeOffset deadline = expiration is not null && expiration.Value < hostDeadline
                    ? expiration.Value
                    : hostDeadline;
                if (deadline <= DateTimeOffset.UtcNow)
                {
                    throw new LumuiProtocolException("The asynchronous action status resource expired.");
                }

                TimeSpan delay = current.PollDelay();
                if (retryAfter is not null && retryAfter.Value > delay)
                {
                    delay = retryAfter.Value;
                }
                retryAfter = null;
                TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
                if (delay > remaining)
                {
                    delay = remaining;
                }
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

                using HttpResponseMessage response = await SendGetAsync(
                    statusUri,
                    accepted.ResponseUri,
                    request => request.Headers.Accept.Add(
                        new MediaTypeWithQualityHeaderValue(MediaType)),
                    cancellationToken).ConfigureAwait(false);
                Uri responseUri = response.RequestMessage?.RequestUri ?? statusUri;
                EnsureSameOrigin(accepted.ResponseUri, responseUri, "action status response");
                retryAfter = RetryAfter(response.Headers.RetryAfter);
                String responseType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? String.Empty;
                if (responseType != MediaType)
                {
                    throw new LumuiProtocolException(
                        $"The action status resource returned unsupported content type '{responseType}'.");
                }
                Byte[] bytes = await ReadLimitedAsync(
                    response.Content,
                    MaximumDocumentBytes,
                    cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new LumuiProtocolException(ProblemMessage(bytes, response.StatusCode));
                }

                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
                }
                catch (JsonException exception)
                {
                    throw new LumuiProtocolException(
                        "The action status response is not valid JSON.",
                        exception);
                }

                String status;
                try
                {
                    status = ValidateActionResult(document.RootElement, correlationId);
                }
                catch
                {
                    document.Dispose();
                    throw;
                }
                if (status is
                    LumuiProtocol.ActionStatuses.Failed or
                    LumuiProtocol.ActionStatuses.Rejected or
                    LumuiProtocol.ActionStatuses.RequiresPermission)
                {
                    String? message = ActionFailureMessage(document.RootElement);
                    document.Dispose();
                    throw new LumuiProtocolException(message ?? "The asynchronous action was not completed.");
                }

                ActionResult next = new ActionResult(document, responseUri);
                if (ownsCurrent)
                {
                    current.Dispose();
                }
                current = next;
                ownsCurrent = true;
                if (status != LumuiProtocol.ActionStatuses.AcceptedAsync)
                {
                    return current;
                }
            }
            throw new LumuiProtocolException("The asynchronous action exceeded the polling limit.");
        }
        catch
        {
            if (ownsCurrent)
            {
                current.Dispose();
            }
            throw;
        }
    }

    public async Task<Stream> GetAssetAsync(Uri assetUri, Uri surfaceUri, CancellationToken cancellationToken = default)
    {
        EnsureTransport(assetUri);
        EnsureSameOrigin(surfaceUri, assetUri, "asset");
        using HttpResponseMessage response = await SendGetAsync(
            assetUri,
            surfaceUri,
            null,
            cancellationToken).ConfigureAwait(false);
        Uri finalUri = response.RequestMessage?.RequestUri ?? assetUri;
        EnsureSameOrigin(surfaceUri, finalUri, "asset");
        response.EnsureSuccessStatusCode();
        Byte[] bytes = await ReadLimitedAsync(response.Content, MaximumAssetBytes, cancellationToken).ConfigureAwait(false);
        return new MemoryStream(bytes, writable: false);
    }

    private async Task<LoadedSurface> LoadCoreAsync(
        Uri requestUri,
        Uri logicalAddress,
        Uri? descriptorUri,
        Uri? actionUri,
        Uri? policyOrigin,
        HashSet<String> visited,
        Int32 hop,
        CancellationToken cancellationToken)
    {
        if (hop >= MaximumDiscoveryHops || !visited.Add(requestUri.AbsoluteUri))
        {
            throw new LumuiProtocolException("LUMUI discovery entered a loop or exceeded its hop limit.");
        }

        _cache.TryGetValue(requestUri.AbsoluteUri, out CachedResponse? cached);
        using HttpResponseMessage response = await SendGetAsync(
            requestUri,
            policyOrigin,
            request =>
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaType));
                request.Headers.Accept.Add(
                    new MediaTypeWithQualityHeaderValue(LumuiProtocol.MediaTypes.Html, 0.8));
                if (cached?.EntityTag is not null)
                {
                    request.Headers.IfNoneMatch.Add(cached.EntityTag);
                }
            },
            cancellationToken).ConfigureAwait(false);
        Boolean notModified = response.StatusCode == HttpStatusCode.NotModified && cached is not null;
        Uri finalUri = notModified ? cached!.FinalUri : response.RequestMessage?.RequestUri ?? requestUri;
        EnsureTransport(finalUri);
        if (policyOrigin is null)
        {
            policyOrigin = finalUri;
        }
        else
        {
            EnsureSameOrigin(policyOrigin, finalUri, "LUMUI resource");
        }
        Byte[] bytes = notModified
            ? cached!.Bytes
            : await ReadLimitedAsync(response.Content, MaximumDocumentBytes, cancellationToken).ConfigureAwait(false);
        String contentType = notModified
            ? cached!.ContentType
            : response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? String.Empty;
        EntityTagHeaderValue? entityTag = notModified ? cached!.EntityTag : response.Headers.ETag;

        if (!response.IsSuccessStatusCode && !notModified)
        {
            throw new LumuiProtocolException(ProblemMessage(bytes, response.StatusCode));
        }
        if (!notModified && response.Headers.ETag is not null)
        {
            _cache[requestUri.AbsoluteUri] = new CachedResponse(
                bytes,
                contentType,
                finalUri,
                response.Headers.ETag);
        }

        if (contentType == MediaType)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
            }
            catch (JsonException exception)
            {
                throw new LumuiProtocolException("The LUMUI response is not valid JSON.", exception);
            }

            JsonElement root = document.RootElement;
            if (root.TryGetProperty(LumuiProtocol.Fields.LumuiSurface, out JsonElement surfaceVersion))
            {
                if (surfaceVersion.ValueKind != JsonValueKind.String
                    || surfaceVersion.GetString() != LumuiProtocol.Versions.Surface)
                {
                    document.Dispose();
                    throw new LumuiProtocolException("The surface version is not supported.");
                }
                if (!notModified || cached?.Validated != true)
                {
                    _validator.Value.ValidateSurface(root);
                    MarkValidated(requestUri);
                }
                Uri? surfaceAction = SurfaceRelation(root, finalUri, LumuiProtocol.Relations.Actions);
                if (surfaceAction is not null)
                {
                    EnsureSameOrigin(finalUri, surfaceAction, "action endpoint");
                }
                return new LoadedSurface(
                    logicalAddress,
                    finalUri,
                    document,
                    Encoding.UTF8.GetString(bytes),
                    descriptorUri,
                    surfaceAction ?? actionUri,
                    entityTag);
            }

            if (root.TryGetProperty(LumuiProtocol.Fields.LumuiWeb, out JsonElement webVersion))
            {
                if (webVersion.ValueKind != JsonValueKind.String
                    || webVersion.GetString() != LumuiProtocol.Versions.Web)
                {
                    document.Dispose();
                    throw new LumuiProtocolException("The web protocol version is not supported.");
                }
                if (!notModified || cached?.Validated != true)
                {
                    _validator.Value.ValidateDescriptor(root);
                    MarkValidated(requestUri);
                }
                if (root.TryGetProperty(LumuiProtocol.Fields.Authentication, out JsonElement authentication)
                    && authentication.ValueKind == JsonValueKind.Object
                    && authentication.TryGetProperty(LumuiProtocol.Fields.Mode, out JsonElement authenticationMode))
                {
                    if (authenticationMode.ValueKind != JsonValueKind.String)
                    {
                        document.Dispose();
                        throw new LumuiProtocolException("The descriptor authentication mode is invalid.");
                    }
                    String mode = authenticationMode.GetString() ?? LumuiProtocol.AuthenticationModes.None;
                    if (mode is not
                        LumuiProtocol.AuthenticationModes.None and not
                        LumuiProtocol.AuthenticationModes.Session)
                    {
                        document.Dispose();
                        throw new LumuiProtocolException(
                            $"This site requires authentication mode '{mode}', which this reference client does not support.");
                    }
                }
                Uri surface = RequiredLink(root, LumuiProtocol.Fields.Surface, finalUri);
                Uri? descriptorAction = OptionalLink(root, LumuiProtocol.Fields.Actions, finalUri);
                EnsureSameOrigin(
                    policyOrigin,
                    surface,
                    LumuiProtocol.Fields.Surface);
                if (descriptorAction is not null)
                {
                    EnsureSameOrigin(policyOrigin, descriptorAction, "action endpoint");
                }
                document.Dispose();
                return await LoadCoreAsync(
                    surface,
                    logicalAddress,
                    finalUri,
                    descriptorAction,
                    policyOrigin,
                    visited,
                    hop + 1,
                    cancellationToken).ConfigureAwait(false);
            }

            if (root.TryGetProperty(LumuiProtocol.Fields.LumuiDiscovery, out JsonElement discoveryVersion))
            {
                if (discoveryVersion.ValueKind != JsonValueKind.String
                    || discoveryVersion.GetString() != LumuiProtocol.Versions.Web)
                {
                    document.Dispose();
                    throw new LumuiProtocolException("The discovery version is not supported.");
                }
                if (!notModified || cached?.Validated != true)
                {
                    _validator.Value.ValidateDiscovery(root);
                    MarkValidated(requestUri);
                }
                Uri descriptor = RequiredLink(root, LumuiProtocol.Fields.Descriptor, finalUri);
                EnsureSameOrigin(policyOrigin, descriptor, "service descriptor");
                document.Dispose();
                return await LoadCoreAsync(
                    descriptor,
                    logicalAddress,
                    descriptorUri,
                    actionUri,
                    policyOrigin,
                    visited,
                    hop + 1,
                    cancellationToken).ConfigureAwait(false);
            }

            document.Dispose();
            throw new LumuiProtocolException("The JSON response is not a LUMUI resource.");
        }

        if (contentType.StartsWith(LumuiProtocol.MediaTypes.Html, StringComparison.Ordinal))
        {
            String html = Encoding.UTF8.GetString(bytes);
            List<DiscoveredLink> links = ParseResponseLinks(response, html, finalUri);
            DiscoveredLink? alternate = links.FirstOrDefault(link =>
                link.Relation.Equals(LumuiProtocol.Relations.Alternate, StringComparison.OrdinalIgnoreCase)
                && link.Type.Equals(MediaType, StringComparison.OrdinalIgnoreCase));
            if (alternate is not null)
            {
                EnsureSameOrigin(policyOrigin, alternate.Uri, "route surface");
                return await LoadCoreAsync(
                    alternate.Uri,
                    logicalAddress,
                    descriptorUri,
                    actionUri,
                    policyOrigin,
                    visited,
                    hop + 1,
                    cancellationToken).ConfigureAwait(false);
            }

            DiscoveredLink? service = links.FirstOrDefault(link =>
                link.Relation.Equals(
                    LumuiProtocol.Relations.ServiceDescriptor,
                    StringComparison.OrdinalIgnoreCase)
                && link.Type.Equals(MediaType, StringComparison.OrdinalIgnoreCase));
            if (service is not null)
            {
                EnsureSameOrigin(policyOrigin, service.Uri, "service descriptor");
                return await LoadCoreAsync(
                    service.Uri,
                    logicalAddress,
                    service.Uri,
                    actionUri,
                    policyOrigin,
                    visited,
                    hop + 1,
                    cancellationToken).ConfigureAwait(false);
            }

            Uri wellKnown = new Uri(
                finalUri.GetLeftPart(UriPartial.Authority) + LumuiProtocol.Paths.WellKnown);
            return await LoadCoreAsync(
                wellKnown,
                logicalAddress,
                descriptorUri,
                actionUri,
                policyOrigin,
                visited,
                hop + 1,
                cancellationToken).ConfigureAwait(false);
        }

        throw new LumuiProtocolException($"The address returned unsupported content type '{contentType}'.");
    }

    private static List<DiscoveredLink> ParseResponseLinks(HttpResponseMessage response, String html, Uri baseUri)
    {
        List<DiscoveredLink> links = new List<DiscoveredLink>();
        if (response.Headers.TryGetValues(
                HttpHeaderNames.Link,
                out IEnumerable<String>? headers))
        {
            foreach (String header in headers)
            {
                foreach (String entry in SplitLinkHeader(header))
                {
                    Int32 open = entry.IndexOf('<');
                    Int32 close = entry.IndexOf('>');
                    if (open < 0 || close <= open)
                    {
                        continue;
                    }
                    String href = entry[(open + 1)..close].Trim();
                    String relation = Parameter(entry, LumuiProtocol.Fields.Rel);
                    String type = Parameter(entry, LumuiProtocol.Fields.Type);
                    if (Uri.TryCreate(baseUri, href, out Uri? uri) && relation.Length > 0)
                    {
                        links.Add(new DiscoveredLink(uri, relation, type));
                    }
                }
            }
        }

        Int32 position = 0;
        while ((position = html.IndexOf("<link", position, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            Int32 end = html.IndexOf('>', position);
            if (end < 0)
            {
                break;
            }
            String tag = html[position..(end + 1)];
            String href = Attribute(tag, LumuiProtocol.Fields.Href);
            String relation = Attribute(tag, LumuiProtocol.Fields.Rel);
            String type = Attribute(tag, LumuiProtocol.Fields.Type);
            if (href.Length > 0 && relation.Length > 0 && Uri.TryCreate(baseUri, href, out Uri? uri))
            {
                links.Add(new DiscoveredLink(uri, relation, type));
            }
            position = end + 1;
        }
        return links;
    }

    private static IEnumerable<String> SplitLinkHeader(String value)
    {
        Int32 start = 0;
        Boolean inQuotes = false;
        Boolean inUri = false;
        for (Int32 index = 0; index < value.Length; index++)
        {
            switch (value[index])
            {
                case '"':
                    inQuotes = !inQuotes;
                    break;
                case '<' when !inQuotes:
                    inUri = true;
                    break;
                case '>' when !inQuotes:
                    inUri = false;
                    break;
                case ',' when !inQuotes && !inUri:
                    yield return value[start..index];
                    start = index + 1;
                    break;
            }
        }
        yield return value[start..];
    }

    private static String Parameter(String value, String name)
    {
        foreach (String part in value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            Int32 equals = part.IndexOf('=');
            if (equals > 0 && part[..equals].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return part[(equals + 1)..].Trim().Trim('"');
            }
        }
        return String.Empty;
    }

    private static String Attribute(String tag, String name)
    {
        String search = name + "=";
        Int32 position = tag.IndexOf(search, StringComparison.OrdinalIgnoreCase);
        if (position < 0)
        {
            return String.Empty;
        }
        position += search.Length;
        while (position < tag.Length && Char.IsWhiteSpace(tag[position]))
        {
            position++;
        }
        if (position >= tag.Length)
        {
            return String.Empty;
        }
        Char quote = tag[position] is '"' or '\'' ? tag[position++] : '\0';
        Int32 end = quote == '\0'
            ? tag.IndexOfAny(new Char[] { ' ', '\t', '\r', '\n', '>' }, position)
            : tag.IndexOf(quote, position);
        if (end < 0)
        {
            end = tag.Length;
        }
        return WebUtility.HtmlDecode(tag[position..end]);
    }

    private static Uri RequiredLink(JsonElement root, String name, Uri baseUri)
    {
        Uri? uri = OptionalLink(root, name, baseUri);
        return uri ?? throw new LumuiProtocolException($"The LUMUI resource does not provide '{name}.href'.");
    }

    private static Uri? OptionalLink(JsonElement root, String name, Uri baseUri)
    {
        if (!root.TryGetProperty(name, out JsonElement link)
            || link.ValueKind != JsonValueKind.Object
            || !link.TryGetProperty(LumuiProtocol.Fields.Href, out JsonElement href)
            || href.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        String? value = href.GetString();
        return String.IsNullOrWhiteSpace(value) ? null : new Uri(baseUri, value);
    }

    private static Uri? SurfaceRelation(JsonElement root, Uri baseUri, String relation)
    {
        if (!root.TryGetProperty(LumuiProtocol.Fields.Links, out JsonElement links)
            || links.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        foreach (JsonElement link in links.EnumerateArray())
        {
            if (link.ValueKind == JsonValueKind.Object
                && link.TryGetProperty(LumuiProtocol.Fields.Rel, out JsonElement rel)
                && rel.ValueKind == JsonValueKind.String
                && rel.GetString() == relation
                && link.TryGetProperty(LumuiProtocol.Fields.Href, out JsonElement href)
                && href.ValueKind == JsonValueKind.String)
            {
                String? value = href.GetString();
                if (!String.IsNullOrWhiteSpace(value))
                {
                    return new Uri(baseUri, value);
                }
            }
        }
        return null;
    }

    private static String RequiredString(JsonElement root, String property)
    {
        if (!root.TryGetProperty(property, out JsonElement value)
            || value.ValueKind != JsonValueKind.String
            || String.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new LumuiProtocolException($"The surface does not provide '{property}'.");
        }
        return value.GetString()!;
    }

    private static async Task<Byte[]> ReadLimitedAsync(
        HttpContent content,
        Int32 maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is Int64 length && length > maximumBytes)
        {
            throw new LumuiProtocolException(
                $"The response exceeds the {maximumBytes} byte limit.");
        }
        await using Stream source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using MemoryStream destination = new MemoryStream();
        Byte[] buffer = ArrayPool<Byte>.Shared.Rent(16_384);
        try
        {
            Int32 total = 0;
            Int32 read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > maximumBytes)
                {
                    throw new LumuiProtocolException(
                        $"The response exceeds the {maximumBytes} byte limit.");
                }
                destination.Write(buffer, 0, read);
            }
            return destination.ToArray();
        }
        finally
        {
            ArrayPool<Byte>.Shared.Return(buffer);
        }
    }

    private async Task<HttpResponseMessage> SendGetAsync(
        Uri requestUri,
        Uri? policyOrigin,
        Action<HttpRequestMessage>? configure,
        CancellationToken cancellationToken)
    {
        Uri currentUri = requestUri;
        for (Int32 redirect = 0; redirect <= MaximumDiscoveryHops; redirect++)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            configure?.Invoke(request);
            HttpResponseMessage response;
            try
            {
            response = await SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                request.Dispose();
                throw;
            }

            if (!IsRedirect(response.StatusCode))
            {
                request.Dispose();
                return response;
            }

            Uri? location = response.Headers.Location;
            if (location is null)
            {
                response.Dispose();
                request.Dispose();
                throw new LumuiProtocolException("A redirect response did not provide a destination.");
            }
            Uri nextUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
            EnsureTransport(nextUri);
            if (policyOrigin is not null)
            {
                EnsureSameOrigin(
                    policyOrigin,
                    nextUri,
                    LumuiProtocol.Fields.Redirect);
            }
            response.Dispose();
            request.Dispose();
            currentUri = nextUri;
        }
        throw new LumuiProtocolException("The address exceeded the redirect limit.");
    }

    private static Boolean IsRedirect(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.Moved
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        Stopwatch stopwatch = Stopwatch.StartNew();
        Uri requestUri = request.RequestUri ?? throw new LumuiProtocolException(
            "The HTTP request has no address.");
        IReadOnlyDictionary<String, String> requestHeaders = Headers(
            request.Headers,
            request.Content?.Headers);
        try
        {
            HttpResponseMessage response = await _http.SendAsync(
                request,
                completionOption,
                cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            RecordTrace(new LumuiRequestTrace(
                startedAt,
                request.Method.Method,
                requestUri,
                response.RequestMessage?.RequestUri,
                (Int32)response.StatusCode,
                response.ReasonPhrase ?? String.Empty,
                response.Content.Headers.ContentType?.ToString() ?? String.Empty,
                stopwatch.Elapsed,
                requestHeaders,
                Headers(response.Headers, response.Content.Headers),
                String.Empty));
            return response;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            RecordTrace(new LumuiRequestTrace(
                startedAt,
                request.Method.Method,
                requestUri,
                null,
                null,
                String.Empty,
                String.Empty,
                stopwatch.Elapsed,
                requestHeaders,
                new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase),
                exception.Message));
            throw;
        }
    }

    private static IReadOnlyDictionary<String, String> Headers(
        HttpHeaders primary,
        HttpHeaders? secondary)
    {
        Dictionary<String, String> headers =
            new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);
        AddHeaders(headers, primary);
        if (secondary is not null)
        {
            AddHeaders(headers, secondary);
        }
        return headers;
    }

    private void RecordTrace(LumuiRequestTrace trace)
    {
        try
        {
            _observer.Record(trace);
        }
        catch
        {
        }
    }

    private static void AddHeaders(
        IDictionary<String, String> destination,
        HttpHeaders source)
    {
        foreach (KeyValuePair<String, IEnumerable<String>> header in source)
        {
            destination[header.Key] = String.Join(", ", header.Value);
        }
    }

    private String ValidateActionResult(JsonElement root, String correlationId)
    {
        _validator.Value.ValidateActionResultEnvelope(root);
        if (!root.TryGetProperty(LumuiProtocol.Fields.MessageType, out JsonElement messageType)
            || messageType.ValueKind != JsonValueKind.String
            || messageType.GetString() != LumuiProtocol.MessageTypes.ActionResult
            || !root.TryGetProperty(LumuiProtocol.Fields.LumuiMessage, out JsonElement resultVersion)
            || resultVersion.ValueKind != JsonValueKind.String
            || resultVersion.GetString() != LumuiProtocol.Versions.Message
            || !root.TryGetProperty(LumuiProtocol.Fields.CorrelationId, out JsonElement correlation)
            || correlation.ValueKind != JsonValueKind.String
            || correlation.GetString() != correlationId
            || !root.TryGetProperty(LumuiProtocol.Fields.Status, out JsonElement status)
            || status.ValueKind != JsonValueKind.String
            || status.GetString() is not (
                LumuiProtocol.ActionStatuses.Completed or
                LumuiProtocol.ActionStatuses.AcceptedAsync or
                LumuiProtocol.ActionStatuses.Failed or
                LumuiProtocol.ActionStatuses.Rejected or
                LumuiProtocol.ActionStatuses.RequiresPermission or
                LumuiProtocol.ActionStatuses.RequiresConfirmation))
        {
            throw new LumuiProtocolException("The server did not return a LUMUI action result.");
        }
        String resolved = status.GetString()!;
        if (resolved == LumuiProtocol.ActionStatuses.AcceptedAsync)
        {
            if (!root.TryGetProperty(LumuiProtocol.Fields.StatusResource, out JsonElement statusResource)
                || statusResource.ValueKind != JsonValueKind.Object
                || !statusResource.TryGetProperty(LumuiProtocol.Fields.Href, out JsonElement href)
                || href.ValueKind != JsonValueKind.String
                || String.IsNullOrWhiteSpace(href.GetString())
                || !statusResource.TryGetProperty(LumuiProtocol.Fields.Type, out JsonElement type)
                || type.ValueKind != JsonValueKind.String
                || type.GetString() != MediaType)
            {
                throw new LumuiProtocolException(
                    "An accepted asynchronous action must advertise a LUMUI status resource.");
            }
        }
        return resolved;
    }

    private static String? ActionFailureMessage(JsonElement root)
    {
        return root.TryGetProperty(LumuiProtocol.Fields.Result, out JsonElement result)
            && result.ValueKind == JsonValueKind.Object
            && result.TryGetProperty(LumuiProtocol.Fields.Message, out JsonElement resultMessage)
            && resultMessage.ValueKind == JsonValueKind.String
                ? resultMessage.GetString()
                : null;
    }

    private static TimeSpan? RetryAfter(RetryConditionHeaderValue? value)
    {
        if (value?.Delta is TimeSpan delta)
        {
            return TimeSpan.FromMilliseconds(
                Math.Clamp(delta.TotalMilliseconds, 250, 60_000));
        }
        if (value?.Date is DateTimeOffset date)
        {
            TimeSpan delay = date - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                return TimeSpan.FromMilliseconds(
                    Math.Clamp(delay.TotalMilliseconds, 250, 60_000));
            }
        }
        return null;
    }

    private static Boolean LooksLikeJson(Byte[] bytes)
    {
        foreach (Byte value in bytes)
        {
            if (value is (Byte)' ' or (Byte)'\t' or (Byte)'\r' or (Byte)'\n')
            {
                continue;
            }
            return value is (Byte)'{' or (Byte)'[';
        }
        return false;
    }

    private static String ProblemMessage(Byte[] bytes, HttpStatusCode statusCode)
    {
        if (LooksLikeJson(bytes))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
                JsonElement root = document.RootElement;
                String? title = root.TryGetProperty(LumuiProtocol.Fields.Title, out JsonElement titleValue)
                    && titleValue.ValueKind == JsonValueKind.String ? titleValue.GetString() : null;
                String? detail = root.TryGetProperty(
                        ErrorResponseFields.Detail,
                        out JsonElement detailValue)
                    && detailValue.ValueKind == JsonValueKind.String ? detailValue.GetString() : null;
                String? errorId = root.TryGetProperty(
                        ErrorResponseFields.ErrorId,
                        out JsonElement idValue)
                    && idValue.ValueKind == JsonValueKind.String ? idValue.GetString() : null;
                String? message = String.Join(
                    " ",
                    new String?[] { title, detail }.Where(
                        (String? value) => !String.IsNullOrWhiteSpace(value)));
                if (!String.IsNullOrWhiteSpace(errorId))
                {
                    message += " Reference: " + errorId;
                }
                if (!String.IsNullOrWhiteSpace(message))
                {
                    return message;
                }
            }
            catch (JsonException)
            {
            }
        }
        return $"The address returned HTTP {(Int32)statusCode}.";
    }

    private static void WriteDictionary(Utf8JsonWriter writer, IReadOnlyDictionary<String, Object?> values)
    {
        writer.WriteStartObject();
        foreach (KeyValuePair<String, Object?> pair in values)
        {
            writer.WritePropertyName(pair.Key);
            WriteValue(writer, pair.Value);
        }
        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, Object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case String text:
                writer.WriteStringValue(text);
                break;
            case Boolean boolean:
                writer.WriteBooleanValue(boolean);
                break;
            case Int32 integer:
                writer.WriteNumberValue(integer);
                break;
            case Int64 longValue:
                writer.WriteNumberValue(longValue);
                break;
            case Single single:
                writer.WriteNumberValue(single);
                break;
            case Double number:
                writer.WriteNumberValue(number);
                break;
            case Decimal decimalValue:
                writer.WriteNumberValue(decimalValue);
                break;
            case JsonElement element:
                element.WriteTo(writer);
                break;
            case IReadOnlyDictionary<String, Object?> dictionary:
                WriteDictionary(writer, dictionary);
                break;
            case IEnumerable<String> strings:
                writer.WriteStartArray();
                foreach (String item in strings)
                {
                    writer.WriteStringValue(item);
                }
                writer.WriteEndArray();
                break;
            case IEnumerable<Object?> values:
                writer.WriteStartArray();
                foreach (Object? item in values)
                {
                    WriteValue(writer, item);
                }
                writer.WriteEndArray();
                break;
            default:
                writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }

    private void MarkValidated(Uri requestUri)
    {
        String key = requestUri.AbsoluteUri;
        while (_cache.TryGetValue(key, out CachedResponse? current)
            && !current.Validated)
        {
            if (_cache.TryUpdate(
                    key,
                    current with { Validated = true },
                    current))
            {
                return;
            }
        }
    }

    private static String Origin(Uri uri) =>
        uri.GetLeftPart(UriPartial.Authority);

    private static void EnsureSameOrigin(Uri origin, Uri target, String resource)
    {
        if (!origin.Scheme.Equals(target.Scheme, StringComparison.OrdinalIgnoreCase)
            || !origin.Host.Equals(target.Host, StringComparison.OrdinalIgnoreCase)
            || origin.Port != target.Port)
        {
            throw new LumuiProtocolException($"The {resource} is outside the permitted origin.");
        }
    }

    private static void EnsureTransport(Uri uri)
    {
        if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && uri.IsLoopback)
        {
            return;
        }
        throw new LumuiProtocolException("LUMUI addresses require HTTPS outside local development.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _http.Dispose();
        _disposed = true;
    }
}
