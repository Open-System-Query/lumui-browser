namespace Lumui.Client;

public sealed class LumuiRequestTrace
{
    public LumuiRequestTrace(
        DateTimeOffset startedAt,
        String method,
        Uri requestUri,
        Uri? responseUri,
        Int32? statusCode,
        String reasonPhrase,
        String contentType,
        TimeSpan duration,
        IReadOnlyDictionary<String, String> requestHeaders,
        IReadOnlyDictionary<String, String> responseHeaders,
        String error)
    {
        StartedAt = startedAt;
        Method = method;
        RequestUri = requestUri;
        ResponseUri = responseUri;
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
        ContentType = contentType;
        Duration = duration;
        RequestHeaders = requestHeaders;
        ResponseHeaders = responseHeaders;
        Error = error;
    }

    public DateTimeOffset StartedAt { get; }

    public String Method { get; }

    public Uri RequestUri { get; }

    public Uri? ResponseUri { get; }

    public Int32? StatusCode { get; }

    public String ReasonPhrase { get; }

    public String ContentType { get; }

    public TimeSpan Duration { get; }

    public IReadOnlyDictionary<String, String> RequestHeaders { get; }

    public IReadOnlyDictionary<String, String> ResponseHeaders { get; }

    public String Error { get; }

    public Boolean Succeeded =>
        StatusCode is >= 200 and < 400 && Error.Length == 0;
}
