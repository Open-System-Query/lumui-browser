using System.Text.Json;
using LumuiProtocol = Lumui.Client.LumuiProtocol;

namespace Lumui.Browser.Security;

public static class CredentialFieldResolver
{
    public static String? SuggestedValue(
        JsonElement component,
        CredentialRecord credential)
    {
        String kind = Text(component, LumuiProtocol.Fields.Kind);
        if (kind == LumuiProtocol.ComponentKinds.PasswordField)
        {
            return credential.Password;
        }
        return kind == LumuiProtocol.ComponentKinds.TextField
            && IsUserName(component)
                ? credential.UserName
                : null;
    }

    public static CredentialSubmission? FindSubmission(
        JsonElement surface,
        IReadOnlyDictionary<String, Object?> input)
    {
        List<String> passwordIds = new List<String>();
        List<String> userNameIds = new List<String>();
        Visit(surface, passwordIds, userNameIds);
        String password = Value(input, passwordIds);
        if (password.Length == 0)
        {
            return null;
        }
        return new CredentialSubmission(
            Value(input, userNameIds),
            password);
    }

    private static void Visit(
        JsonElement element,
        ICollection<String> passwordIds,
        ICollection<String> userNameIds)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            String id = Text(element, LumuiProtocol.Fields.Id);
            String kind = Text(element, LumuiProtocol.Fields.Kind);
            if (id.Length > 0 && kind == LumuiProtocol.ComponentKinds.PasswordField)
            {
                passwordIds.Add(id);
            }
            else if (id.Length > 0
                && kind == LumuiProtocol.ComponentKinds.TextField
                && IsUserName(element))
            {
                userNameIds.Add(id);
            }
            foreach (JsonProperty property in element.EnumerateObject())
            {
                Visit(property.Value, passwordIds, userNameIds);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
            {
                Visit(child, passwordIds, userNameIds);
            }
        }
    }

    private static Boolean IsUserName(JsonElement component)
    {
        String descriptor = String.Join(
            " ",
            Text(component, LumuiProtocol.Fields.Id),
            Text(component, LumuiProtocol.Fields.Name),
            Text(component, LumuiProtocol.Fields.Label),
            Text(component, LumuiProtocol.Fields.Meaning),
            Text(component, LumuiProtocol.Fields.Placeholder))
            .ToLowerInvariant();
        return descriptor.Contains("user", StringComparison.Ordinal)
            || descriptor.Contains("email", StringComparison.Ordinal)
            || descriptor.Contains("login", StringComparison.Ordinal)
            || descriptor.Contains("account", StringComparison.Ordinal);
    }

    private static String Value(
        IReadOnlyDictionary<String, Object?> input,
        IEnumerable<String> ids)
    {
        foreach (String id in ids)
        {
            if (input.TryGetValue(id, out Object? value)
                && value is not null
                && !String.IsNullOrWhiteSpace(value.ToString()))
            {
                return value.ToString() ?? String.Empty;
            }
        }
        return String.Empty;
    }

    private static String Text(JsonElement element, String name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? String.Empty
            : String.Empty;
}
