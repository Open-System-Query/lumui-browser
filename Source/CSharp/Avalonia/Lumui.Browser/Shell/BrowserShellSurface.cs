using System.Text.Json;
using Lumui.Client;

namespace Lumui.Browser.Shell;

public sealed class BrowserShellSurface : IDisposable
{
    private readonly JsonDocument _document;
    private readonly Dictionary<String, JsonElement> _components;

    private BrowserShellSurface(JsonDocument document)
    {
        _document = document;
        _components = new Dictionary<String, JsonElement>(StringComparer.Ordinal);
        Index(_document.RootElement);
    }

    public static BrowserShellSurface CreateDefault()
    {
        JsonDocument document = JsonDocument.Parse(BrowserShellDocument.Source);
        try
        {
            LumuiDocumentValidator.CreateDefault().ValidateSurface(document.RootElement);
            return new BrowserShellSurface(document);
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    public JsonElement Component(String id)
    {
        return _components.TryGetValue(id, out JsonElement component)
            ? component
            : throw new InvalidOperationException(
                $"The browser shell does not publish component '{id}'.");
    }

    public String Text(String id)
    {
        JsonElement component = Component(id);
        if (component.TryGetProperty("label", out JsonElement label))
        {
            return label.GetString() ?? String.Empty;
        }
        if (component.TryGetProperty("text", out JsonElement text))
        {
            return text.GetString() ?? String.Empty;
        }
        return String.Empty;
    }

    public String Help(String id)
    {
        JsonElement component = Component(id);
        return component.TryGetProperty("help", out JsonElement help)
            ? help.GetString() ?? String.Empty
            : String.Empty;
    }

    public void Dispose() => _document.Dispose();

    private void Index(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                Index(item);
            }
            return;
        }
        if (value.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        if (value.TryGetProperty("id", out JsonElement id)
            && value.TryGetProperty("kind", out JsonElement _))
        {
            _components[id.GetString() ?? String.Empty] = value;
        }
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (property.Name is "pages" or "regions" or "items" or "children" or "tabs" or "nodes")
            {
                Index(property.Value);
            }
        }
    }
}
