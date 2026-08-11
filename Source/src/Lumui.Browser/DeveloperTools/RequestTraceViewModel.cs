using Lumui.Client;

namespace Lumui.Browser.DeveloperTools;

public sealed class RequestTraceViewModel
{
    public RequestTraceViewModel(LumuiRequestTrace trace)
    {
        Trace = trace;
    }

    public LumuiRequestTrace Trace { get; }

    public override String ToString()
    {
        String status = Trace.StatusCode?.ToString()
            ?? DeveloperToolsText.ErrorStatus;
        return DeveloperToolsText.RequestListEntry(
            status,
            Trace.Method,
            Trace.RequestUri,
            Trace.Duration.TotalMilliseconds);
    }
}
