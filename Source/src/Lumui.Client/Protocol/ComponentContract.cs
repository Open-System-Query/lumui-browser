namespace Lumui.Client;

public sealed class ComponentContract
{
    internal ComponentContract(
        String kind,
        IReadOnlySet<String> allowedFields,
        IReadOnlyList<String[]> requiredFields,
        IReadOnlySet<String> forbiddenFields)
    {
        Kind = kind;
        AllowedFields = allowedFields;
        RequiredFields = requiredFields;
        ForbiddenFields = forbiddenFields;
    }

    public String Kind { get; }

    public IReadOnlySet<String> AllowedFields { get; }

    public IReadOnlyList<String[]> RequiredFields { get; }

    public IReadOnlySet<String> ForbiddenFields { get; }
}
