namespace Lumui.Cli.Rendering;

public sealed class TerminalSurfaceDocument
{
    public TerminalSurfaceDocument(
        JsonElement root,
        Uri baseUri,
        String title,
        String description,
        IReadOnlyList<TerminalPage> pages,
        Int32 requestedPageIndex,
        TerminalSiteChrome siteChrome,
        IReadOnlyDictionary<String, Object?> initialInput)
    {
        Root = root;
        BaseUri = baseUri;
        Title = title;
        Description = description;
        Pages = pages;
        RequestedPageIndex = requestedPageIndex;
        SiteChrome = siteChrome;
        InitialInput = initialInput;
    }

    public JsonElement Root { get; }

    public Uri BaseUri { get; }

    public String Title { get; }

    public String Description { get; }

    public IReadOnlyList<TerminalPage> Pages { get; }

    public Int32 RequestedPageIndex { get; }

    public TerminalSiteChrome SiteChrome { get; }

    public IReadOnlyDictionary<String, Object?> InitialInput { get; }
}
