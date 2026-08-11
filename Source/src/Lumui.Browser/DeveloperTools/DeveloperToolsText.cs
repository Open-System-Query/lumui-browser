namespace Lumui.Browser.DeveloperTools;

public static class DeveloperToolsText
{
    public const String Accessibility = "Accessibility";
    public const String Actions = "Actions";
    public const String ActionsDefined = "Actions defined";
    public const String Address = "Address";
    public const String Application = "Application";
    public const String Clear = "Clear";
    public const String Components = "Components";
    public const String ContentType = "Content type";
    public const String Copy = "Copy";
    public const String Description = "See the page, actions and any problems.";
    public const String Descriptor = "Descriptor";
    public const String Diagnostics = "Diagnostics";
    public const String Duration = "Duration";
    public const String EntityTag = "ETag";
    public const String Error = "Error";
    public const String ErrorStatus = "ERR";
    public const String FinalAddress = "Final address";
    public const String Find = "Find";
    public const String FindInSource = "Find in source";
    public const String Format = "Format";
    public const String Interaction = "Interaction";
    public const String Json = "JSON";
    public const String JsonMediaType = "application/json";
    public const String JsonPattern = "*.json";
    public const String LoadTime = "Load time";
    public const String Mode = "Mode";
    public const String Network = "Network";
    public const String None = "None";
    public const String NotAdvertised = "Not advertised";
    public const String NotPresent = "Not present";
    public const String Overview = "Overview";
    public const String Output = "Output";
    public const String Pages = "Pages";
    public const String Profile = "Profile";
    public const String Problems = "Problems";
    public const String Protocol = "Protocol";
    public const String Raw = "Raw";
    public const String RequestHeaders = "Request headers";
    public const String RequestError = "an error";
    public const String ResponseHeaders = "Response headers";
    public const String Revision = "Revision";
    public const String Save = "Save";
    public const String SaveSource = "Save LUMUI source";
    public const String Source = "Source";
    public const String SourceBytes = "Source bytes";
    public const String SourceCopied = "Source copied.";
    public const String SourceFilename = "surface.json";
    public const String SourceNotFound = "Source text not found.";
    public const String SourceSaved = "Source saved.";
    public const String Status = "Status";
    public const String Style = "Style";
    public const String Surface = "Surface";
    public const String SurfaceId = "Surface ID";
    public const String Title = "Developer tools";
    public const String Tree = "Structure";
    public const String Validated = "Document validated and rendered.";
    public const String DocumentTitle = "Title";
    public const String RegionNode = "region";
    public const String ComponentNode = "component";

    public static String Presentation(
        String profile,
        String appearance,
        String output,
        String interaction,
        String accessibility) =>
        "Presentation: "
        + profile
        + ", "
        + appearance
        + ", "
        + output
        + ", "
        + interaction
        + ", "
        + accessibility
        + ".";

    public static String RequestResult(
        String method,
        Uri requestUri,
        String status,
        Double milliseconds) =>
        method
        + " "
        + requestUri
        + " returned "
        + status
        + " in "
        + milliseconds.ToString("0")
        + " ms.";

    public static String RequestListEntry(
        String status,
        String method,
        Uri requestUri,
        Double milliseconds) =>
        $"{status}  {method}  {requestUri}  {milliseconds:0} ms";

    public static String DurationValue(Double milliseconds) =>
        $"{milliseconds:0.0} ms";

    public static String LoadTimeValue(Double milliseconds) =>
        $"{milliseconds:0} ms";
}
