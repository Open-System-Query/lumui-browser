namespace Lumui.Cli.Security;

public static class CredentialSemantics
{
    public static String? SuggestedValue(JsonElement component, CredentialRecord credential)
    {
        String kind = Text(component, LumuiProtocol.Fields.Kind);
        if (kind == LumuiProtocol.ComponentKinds.PasswordField)
        {
            return credential.Password;
        }
        return kind == LumuiProtocol.ComponentKinds.TextField && IsUserName(component)
            ? credential.UserName
            : null;
    }

    public static (String UserName, String Password)? FindSubmission(
        JsonElement surface,
        IReadOnlyDictionary<String, Object?> input)
    {
        List<String> passwords = new List<String>();
        List<String> users = new List<String>();
        Visit(surface, passwords, users);
        String password = Value(input, passwords);
        return password.Length == 0 ? null : (Value(input, users), password);
    }

    public static Boolean IsSensitiveAction(JsonElement surface, String componentId)
    {
        if (surface.ValueKind == JsonValueKind.Object)
        {
            if (Text(surface, LumuiProtocol.Fields.Id) == componentId
                && Text(surface, LumuiProtocol.Fields.Kind) is
                    LumuiProtocol.ComponentKinds.ContactPicker
                    or LumuiProtocol.ComponentKinds.FilePicker
                    or LumuiProtocol.ComponentKinds.LocationPicker
                    or LumuiProtocol.ComponentKinds.MediaPicker
                    or LumuiProtocol.ComponentKinds.Dialer)
            {
                return true;
            }
            return surface.EnumerateObject().Any(property => IsSensitiveAction(property.Value, componentId));
        }
        return surface.ValueKind == JsonValueKind.Array
            && surface.EnumerateArray().Any(child => IsSensitiveAction(child, componentId));
    }

    private static void Visit(JsonElement element, ICollection<String> passwords, ICollection<String> users)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            String id = Text(element, LumuiProtocol.Fields.Id);
            String kind = Text(element, LumuiProtocol.Fields.Kind);
            if (id.Length > 0 && kind == LumuiProtocol.ComponentKinds.PasswordField)
            {
                passwords.Add(id);
            }
            else if (id.Length > 0 && kind == LumuiProtocol.ComponentKinds.TextField && IsUserName(element))
            {
                users.Add(id);
            }
            foreach (JsonProperty property in element.EnumerateObject())
            {
                Visit(property.Value, passwords, users);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
            {
                Visit(child, passwords, users);
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
            Text(component, LumuiProtocol.Fields.Placeholder)).ToLowerInvariant();
        return descriptor.Contains("user", StringComparison.Ordinal)
            || descriptor.Contains("email", StringComparison.Ordinal)
            || descriptor.Contains("login", StringComparison.Ordinal)
            || descriptor.Contains("account", StringComparison.Ordinal);
    }

    private static String Value(IReadOnlyDictionary<String, Object?> input, IEnumerable<String> ids)
    {
        foreach (String id in ids)
        {
            if (input.TryGetValue(id, out Object? value) && !String.IsNullOrWhiteSpace(value?.ToString()))
            {
                return value?.ToString() ?? String.Empty;
            }
        }
        return String.Empty;
    }

    private static String Text(JsonElement element, String name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? String.Empty
            : String.Empty;
}
