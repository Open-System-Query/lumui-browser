using System.Globalization;
using System.Collections.Concurrent;
using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lumui.Client;

internal sealed class JsonSchemaValidator
{
    private const Int32 MaximumDepth = 64;
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromSeconds(1);
    private readonly JsonElement _rootSchema;
    private readonly ConcurrentDictionary<String, JsonElement> _references =
        new ConcurrentDictionary<String, JsonElement>(StringComparer.Ordinal);

    public JsonSchemaValidator(JsonElement rootSchema)
    {
        if (rootSchema.ValueKind != JsonValueKind.Object)
        {
            throw new LumuiProtocolException("The JSON Schema document must be an object.");
        }
        _rootSchema = rootSchema.Clone();
    }

    public void Validate(JsonElement value, String path)
    {
        ValidateValue(value, _rootSchema, path, 0);
    }

    public void ValidateDefinition(JsonElement value, String definition, String path)
    {
        JsonElement definitions = RequireObject(
            _rootSchema,
            JsonSchemaKeywords.Definitions,
            path);
        if (!definitions.TryGetProperty(definition, out JsonElement schema)
            || schema.ValueKind != JsonValueKind.Object)
        {
            throw Error(path, $"schema definition '{definition}' is unavailable.");
        }
        ValidateValue(value, schema, path, 0);
    }

    private void ValidateValue(
        JsonElement value,
        JsonElement schema,
        String path,
        Int32 depth)
    {
        if (depth > MaximumDepth)
        {
            throw Error(path, "the value exceeds the validation depth limit.");
        }
        if (schema.ValueKind != JsonValueKind.Object)
        {
            throw Error(path, "the applicable schema is not an object.");
        }

        if (schema.TryGetProperty(
                JsonSchemaKeywords.Reference,
                out JsonElement reference))
        {
            if (reference.ValueKind != JsonValueKind.String)
            {
                throw Error(path, "the schema reference is invalid.");
            }
            ValidateValue(
                value,
                ResolveReference(reference.GetString() ?? String.Empty, path),
                path,
                depth + 1);
        }

        ValidateAllOf(value, schema, path, depth);
        ValidateAnyOf(value, schema, path, depth);
        ValidateOneOf(value, schema, path, depth);
        ValidateConditional(value, schema, path, depth);

        if (schema.TryGetProperty(
                JsonSchemaKeywords.Constant,
                out JsonElement constant)
            && !JsonValuesEqual(value, constant))
        {
            throw Error(path, "the value is not the defined constant.");
        }
        if (schema.TryGetProperty(
                JsonSchemaKeywords.Enumeration,
                out JsonElement enumeration))
        {
            RequireArray(enumeration, JsonSchemaKeywords.Enumeration, path);
            Boolean found = false;
            foreach (JsonElement candidate in enumeration.EnumerateArray())
            {
                if (JsonValuesEqual(value, candidate))
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                throw Error(path, "the value is not in the defined set.");
            }
        }

        if (schema.TryGetProperty(
                JsonSchemaKeywords.Type,
                out JsonElement type)
            && !MatchesType(value, type, path))
        {
            throw Error(path, "the value has an invalid type.");
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            ValidateString(value.GetString() ?? String.Empty, schema, path);
        }
        else if (value.ValueKind == JsonValueKind.Number)
        {
            ValidateNumber(value, schema, path);
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            ValidateArray(value, schema, path, depth);
        }
        else if (value.ValueKind == JsonValueKind.Object)
        {
            ValidateObject(value, schema, path, depth);
        }
    }

    private void ValidateAllOf(
        JsonElement value,
        JsonElement schema,
        String path,
        Int32 depth)
    {
        if (!schema.TryGetProperty(
                JsonSchemaKeywords.AllOf,
                out JsonElement branches))
        {
            return;
        }
        RequireArray(branches, JsonSchemaKeywords.AllOf, path);
        foreach (JsonElement branch in branches.EnumerateArray())
        {
            ValidateValue(value, branch, path, depth + 1);
        }
    }

    private void ValidateAnyOf(
        JsonElement value,
        JsonElement schema,
        String path,
        Int32 depth)
    {
        if (!schema.TryGetProperty(
                JsonSchemaKeywords.AnyOf,
                out JsonElement branches))
        {
            return;
        }
        if (CountMatchingBranches(value, branches, path, depth) < 1)
        {
            throw Error(path, "the value does not match an allowed form.");
        }
    }

    private void ValidateOneOf(
        JsonElement value,
        JsonElement schema,
        String path,
        Int32 depth)
    {
        if (!schema.TryGetProperty(
                JsonSchemaKeywords.OneOf,
                out JsonElement branches))
        {
            return;
        }
        Int32 matches = CountMatchingBranches(value, branches, path, depth);
        if (matches != 1 && !(IsEmptyArray(value) && matches > 0))
        {
            throw Error(path, "the value does not match exactly one allowed form.");
        }
    }

    private void ValidateConditional(
        JsonElement value,
        JsonElement schema,
        String path,
        Int32 depth)
    {
        if (!schema.TryGetProperty(
                JsonSchemaKeywords.If,
                out JsonElement condition))
        {
            return;
        }
        String branchName = Matches(value, condition, path, depth + 1)
            ? JsonSchemaKeywords.Then
            : JsonSchemaKeywords.Else;
        if (schema.TryGetProperty(branchName, out JsonElement branch))
        {
            ValidateValue(value, branch, path, depth + 1);
        }
    }

    private Int32 CountMatchingBranches(
        JsonElement value,
        JsonElement branches,
        String path,
        Int32 depth)
    {
        RequireArray(branches, "schema branches", path);
        Int32 matches = 0;
        foreach (JsonElement branch in branches.EnumerateArray())
        {
            if (Matches(value, branch, path, depth + 1))
            {
                matches++;
            }
        }
        return matches;
    }

    private Boolean Matches(
        JsonElement value,
        JsonElement schema,
        String path,
        Int32 depth)
    {
        try
        {
            ValidateValue(value, schema, path, depth);
            return true;
        }
        catch (LumuiProtocolException)
        {
            return false;
        }
    }

    private void ValidateString(String value, JsonElement schema, String path)
    {
        if (schema.TryGetProperty(
                JsonSchemaKeywords.MinimumLength,
                out JsonElement minimumLength)
            && value.Length < ReadNonNegativeInteger(
                minimumLength,
                JsonSchemaKeywords.MinimumLength,
                path))
        {
            throw Error(path, "the text is shorter than allowed.");
        }
        if (schema.TryGetProperty(
                JsonSchemaKeywords.MaximumLength,
                out JsonElement maximumLength)
            && value.Length > ReadNonNegativeInteger(
                maximumLength,
                JsonSchemaKeywords.MaximumLength,
                path))
        {
            throw Error(path, "the text is longer than allowed.");
        }
        if (schema.TryGetProperty(
                JsonSchemaKeywords.Pattern,
                out JsonElement pattern))
        {
            if (pattern.ValueKind != JsonValueKind.String)
            {
                throw Error(path, "the schema pattern is invalid.");
            }
            try
            {
                if (!Regex.IsMatch(
                        value,
                        pattern.GetString() ?? String.Empty,
                        RegexOptions.CultureInvariant,
                        PatternTimeout))
                {
                    throw Error(path, "the text does not match the defined pattern.");
                }
            }
            catch (ArgumentException exception)
            {
                throw Error(path, $"the schema pattern is invalid: {exception.Message}");
            }
            catch (RegexMatchTimeoutException)
            {
                throw Error(path, "the schema pattern exceeded the validation time limit.");
            }
        }
        if (schema.TryGetProperty(
                JsonSchemaKeywords.Format,
                out JsonElement format))
        {
            if (format.ValueKind != JsonValueKind.String
                || !MatchesFormat(value, format.GetString() ?? String.Empty))
            {
                throw Error(path, "the text does not match the defined format.");
            }
        }
    }

    private static void ValidateNumber(
        JsonElement value,
        JsonElement schema,
        String path)
    {
        Double number = ReadNumber(value, nameof(value), path);
        if (schema.TryGetProperty(
                JsonSchemaKeywords.Minimum,
                out JsonElement minimum)
            && number < ReadNumber(minimum, JsonSchemaKeywords.Minimum, path))
        {
            throw Error(path, "the number is below the minimum.");
        }
        if (schema.TryGetProperty(
                JsonSchemaKeywords.Maximum,
                out JsonElement maximum)
            && number > ReadNumber(maximum, JsonSchemaKeywords.Maximum, path))
        {
            throw Error(path, "the number is above the maximum.");
        }
        if (schema.TryGetProperty(
                JsonSchemaKeywords.ExclusiveMinimum,
                out JsonElement exclusiveMinimum)
            && number <= ReadNumber(
                exclusiveMinimum,
                JsonSchemaKeywords.ExclusiveMinimum,
                path))
        {
            throw Error(path, "the number must be above the exclusive minimum.");
        }
        if (schema.TryGetProperty(
                JsonSchemaKeywords.ExclusiveMaximum,
                out JsonElement exclusiveMaximum)
            && number >= ReadNumber(
                exclusiveMaximum,
                JsonSchemaKeywords.ExclusiveMaximum,
                path))
        {
            throw Error(path, "the number must be below the exclusive maximum.");
        }
    }

    private void ValidateArray(
        JsonElement value,
        JsonElement schema,
        String path,
        Int32 depth)
    {
        Int32 length = value.GetArrayLength();
        if (schema.TryGetProperty(
                JsonSchemaKeywords.MinimumItems,
                out JsonElement minimumItems)
            && length < ReadNonNegativeInteger(
                minimumItems,
                JsonSchemaKeywords.MinimumItems,
                path))
        {
            throw Error(path, "the list contains too few items.");
        }
        if (schema.TryGetProperty(
                JsonSchemaKeywords.MaximumItems,
                out JsonElement maximumItems)
            && length > ReadNonNegativeInteger(
                maximumItems,
                JsonSchemaKeywords.MaximumItems,
                path))
        {
            throw Error(path, "the list contains too many items.");
        }
        if (schema.TryGetProperty(
                JsonSchemaKeywords.UniqueItems,
                out JsonElement uniqueItems))
        {
            if (uniqueItems.ValueKind is not (
                JsonValueKind.True or
                JsonValueKind.False))
            {
                throw Error(path, "the uniqueItems schema value must be boolean.");
            }
            if (uniqueItems.ValueKind == JsonValueKind.True)
            {
                List<JsonElement> previousItems = new List<JsonElement>();
                foreach (JsonElement item in value.EnumerateArray())
                {
                    if (previousItems.Any(
                            (JsonElement previous) => JsonValuesEqual(previous, item)))
                    {
                        throw Error(path, "the list contains duplicate items.");
                    }
                    previousItems.Add(item);
                }
            }
        }
        if (schema.TryGetProperty(
                JsonSchemaKeywords.Items,
                out JsonElement itemSchema))
        {
            Int32 index = 0;
            foreach (JsonElement item in value.EnumerateArray())
            {
                ValidateValue(
                    item,
                    itemSchema,
                    $"{path}[{index}]",
                    depth + 1);
                index++;
            }
        }
    }

    private void ValidateObject(
        JsonElement value,
        JsonElement schema,
        String path,
        Int32 depth)
    {
        Int32? propertyCount = null;
        if (schema.TryGetProperty(
                JsonSchemaKeywords.MinimumProperties,
                out JsonElement minimumProperties)
            && (propertyCount ??= value.EnumerateObject().Count()) < ReadNonNegativeInteger(
                minimumProperties,
                JsonSchemaKeywords.MinimumProperties,
                path))
        {
            throw Error(path, "the object contains too few fields.");
        }
        if (schema.TryGetProperty(
                JsonSchemaKeywords.MaximumProperties,
                out JsonElement maximumProperties)
            && (propertyCount ??= value.EnumerateObject().Count()) > ReadNonNegativeInteger(
                maximumProperties,
                JsonSchemaKeywords.MaximumProperties,
                path))
        {
            throw Error(path, "the object contains too many fields.");
        }

        if (schema.TryGetProperty(
                JsonSchemaKeywords.Required,
                out JsonElement required))
        {
            RequireArray(required, JsonSchemaKeywords.Required, path);
            foreach (JsonElement requiredField in required.EnumerateArray())
            {
                if (requiredField.ValueKind != JsonValueKind.String)
                {
                    throw Error(path, "a required field name is not text.");
                }
                String name = requiredField.GetString() ?? String.Empty;
                if (!value.TryGetProperty(name, out JsonElement _))
                {
                    throw Error($"{path}.{name}", "the field is required.");
                }
            }
        }

        JsonElement properties = default;
        Boolean hasProperties = schema.TryGetProperty(
            JsonSchemaKeywords.Properties,
            out properties);
        if (hasProperties && properties.ValueKind != JsonValueKind.Object)
        {
            throw Error(path, "the schema properties member must be an object.");
        }
        Boolean hasAdditionalProperties = schema.TryGetProperty(
            JsonSchemaKeywords.AdditionalProperties,
            out JsonElement additionalProperties);
        Boolean hasPropertyNames = schema.TryGetProperty(
            JsonSchemaKeywords.PropertyNames,
            out JsonElement propertyNames);

        foreach (JsonProperty property in value.EnumerateObject())
        {
            String propertyPath = $"{path}.{property.Name}";
            if (hasPropertyNames)
            {
                ValidatePropertyName(
                    property.Name,
                    propertyNames,
                    propertyPath,
                    depth + 1);
            }
            if (hasProperties
                && properties.TryGetProperty(
                    property.Name,
                    out JsonElement propertySchema))
            {
                ValidateValue(
                    property.Value,
                    propertySchema,
                    propertyPath,
                    depth + 1);
                continue;
            }
            if (!hasAdditionalProperties)
            {
                continue;
            }
            if (additionalProperties.ValueKind == JsonValueKind.False)
            {
                throw Error(propertyPath, "the field is not defined.");
            }
            if (additionalProperties.ValueKind == JsonValueKind.Object)
            {
                ValidateValue(
                    property.Value,
                    additionalProperties,
                    propertyPath,
                    depth + 1);
                continue;
            }
            if (additionalProperties.ValueKind != JsonValueKind.True)
            {
                throw Error(path, "the additionalProperties schema value is invalid.");
            }
        }
    }

    private void ValidatePropertyName(
        String value,
        JsonElement schema,
        String path,
        Int32 depth)
    {
        if (depth > MaximumDepth)
        {
            throw Error(path, "the property name exceeds the validation depth limit.");
        }
        if (schema.TryGetProperty(
                JsonSchemaKeywords.Reference,
                out JsonElement reference))
        {
            if (reference.ValueKind != JsonValueKind.String)
            {
                throw Error(path, "the property-name schema reference is invalid.");
            }
            ValidatePropertyName(
                value,
                ResolveReference(reference.GetString() ?? String.Empty, path),
                path,
                depth + 1);
        }
        if (schema.TryGetProperty(
                JsonSchemaKeywords.Type,
                out JsonElement type)
            && !SchemaTypeIncludes(type, JsonSchemaValueTypes.String, path))
        {
            throw Error(path, "the property-name schema must accept text.");
        }
        if (schema.TryGetProperty(
                JsonSchemaKeywords.Constant,
                out JsonElement constant)
            && (
                constant.ValueKind != JsonValueKind.String
                || constant.GetString() != value
            ))
        {
            throw Error(path, "the property name is not the defined constant.");
        }
        if (schema.TryGetProperty(
                JsonSchemaKeywords.Enumeration,
                out JsonElement enumeration))
        {
            RequireArray(enumeration, JsonSchemaKeywords.Enumeration, path);
            Boolean found = enumeration.EnumerateArray().Any(
                (JsonElement item) => item.ValueKind == JsonValueKind.String
                    && item.GetString() == value);
            if (!found)
            {
                throw Error(path, "the property name is not in the defined set.");
            }
        }
        ValidateString(value, schema, path);
    }

    private static Boolean MatchesType(
        JsonElement value,
        JsonElement schemaType,
        String path)
    {
        if (schemaType.ValueKind == JsonValueKind.String)
        {
            return MatchesSingleType(
                value,
                schemaType.GetString() ?? String.Empty,
                path);
        }
        if (schemaType.ValueKind != JsonValueKind.Array)
        {
            throw Error(path, "the schema type must be text or a list.");
        }
        foreach (JsonElement type in schemaType.EnumerateArray())
        {
            if (type.ValueKind != JsonValueKind.String)
            {
                throw Error(path, "a schema type list contains a non-text value.");
            }
            if (MatchesSingleType(
                    value,
                    type.GetString() ?? String.Empty,
                    path))
            {
                return true;
            }
        }
        return false;
    }

    private static Boolean MatchesSingleType(
        JsonElement value,
        String type,
        String path)
    {
        return type switch
        {
            JsonSchemaValueTypes.Null => value.ValueKind == JsonValueKind.Null,
            JsonSchemaValueTypes.String => value.ValueKind == JsonValueKind.String,
            JsonSchemaValueTypes.Boolean => value.ValueKind is
                JsonValueKind.True or
                JsonValueKind.False,
            JsonSchemaValueTypes.Integer => IsInteger(value),
            JsonSchemaValueTypes.Number => value.ValueKind == JsonValueKind.Number,
            JsonSchemaValueTypes.Array => value.ValueKind == JsonValueKind.Array,
            JsonSchemaValueTypes.Object => value.ValueKind == JsonValueKind.Object
                || IsEmptyArray(value),
            _ => throw Error(path, $"the schema type '{type}' is unsupported."),
        };
    }

    private static Boolean IsEmptyArray(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.Array
            && value.GetArrayLength() == 0;
    }

    private static Boolean SchemaTypeIncludes(
        JsonElement schemaType,
        String expected,
        String path)
    {
        if (schemaType.ValueKind == JsonValueKind.String)
        {
            return schemaType.GetString() == expected;
        }
        if (schemaType.ValueKind != JsonValueKind.Array)
        {
            throw Error(path, "the schema type must be text or a list.");
        }
        foreach (JsonElement type in schemaType.EnumerateArray())
        {
            if (type.ValueKind != JsonValueKind.String)
            {
                throw Error(path, "a schema type list contains a non-text value.");
            }
            if (type.GetString() == expected)
            {
                return true;
            }
        }
        return false;
    }

    private static Boolean IsInteger(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetDecimal(out Decimal number))
        {
            return false;
        }
        return Decimal.Truncate(number) == number;
    }

    private static Boolean MatchesFormat(String value, String format)
    {
        switch (format)
        {
            case JsonSchemaFormats.UriReference:
                return ValidUri(value, UriKind.RelativeOrAbsolute);
            case JsonSchemaFormats.Uri:
                return ValidUri(value, UriKind.Absolute);
            case JsonSchemaFormats.Email:
                return MailAddress.TryCreate(value, out MailAddress? _);
            case JsonSchemaFormats.DateTime:
                return value.Contains('T', StringComparison.Ordinal)
                    && (
                        value.EndsWith('Z')
                        || Regex.IsMatch(
                            value,
                            "[+-][0-9]{2}:[0-9]{2}$",
                            RegexOptions.CultureInvariant,
                            PatternTimeout)
                    )
                    && DateTimeOffset.TryParse(
                        value,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out DateTimeOffset _);
            default:
                throw new LumuiProtocolException(
                    $"The JSON Schema format '{format}' is unsupported.");
        }
    }

    private static Boolean ValidUri(String value, UriKind kind)
    {
        if (value.Any(Char.IsControl)
            || value.Any(Char.IsWhiteSpace)
            || value.Contains('\\'))
        {
            return false;
        }
        for (Int32 index = 0; index < value.Length; index++)
        {
            if (value[index] != '%')
            {
                continue;
            }
            if (index + 2 >= value.Length
                || !Uri.IsHexDigit(value[index + 1])
                || !Uri.IsHexDigit(value[index + 2]))
            {
                return false;
            }
            index += 2;
        }
        return Uri.TryCreate(value, kind, out Uri? _);
    }

    private JsonElement ResolveReference(String reference, String path)
    {
        if (reference == "#")
        {
            return _rootSchema;
        }
        if (_references.TryGetValue(reference, out JsonElement cached))
        {
            return cached;
        }
        if (!reference.StartsWith("#/", StringComparison.Ordinal))
        {
            throw Error(path, $"schema reference '{reference}' is unsupported.");
        }

        JsonElement value = _rootSchema;
        String[] segments = reference[2..].Split('/');
        foreach (String encodedSegment in segments)
        {
            String segment = Uri.UnescapeDataString(encodedSegment)
                .Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
            if (value.ValueKind != JsonValueKind.Object
                || !value.TryGetProperty(segment, out JsonElement next))
            {
                throw Error(path, $"schema reference '{reference}' cannot be resolved.");
            }
            value = next;
        }
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Error(path, $"schema reference '{reference}' is not an object.");
        }
        _references.TryAdd(reference, value);
        return value;
    }

    private static Boolean JsonValuesEqual(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
        {
            if (left.ValueKind == JsonValueKind.Number
                && right.ValueKind == JsonValueKind.Number)
            {
                return ReadNumber(left, "left value", "$")
                    .Equals(ReadNumber(right, "right value", "$"));
            }
            return false;
        }

        switch (left.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return true;
            case JsonValueKind.String:
                return String.Equals(
                    left.GetString(),
                    right.GetString(),
                    StringComparison.Ordinal);
            case JsonValueKind.True:
            case JsonValueKind.False:
                return left.GetBoolean() == right.GetBoolean();
            case JsonValueKind.Number:
                return ReadNumber(left, "left value", "$")
                    .Equals(ReadNumber(right, "right value", "$"));
            case JsonValueKind.Array:
                if (left.GetArrayLength() != right.GetArrayLength())
                {
                    return false;
                }
                JsonElement.ArrayEnumerator leftItems = left.EnumerateArray();
                JsonElement.ArrayEnumerator rightItems = right.EnumerateArray();
                while (leftItems.MoveNext() && rightItems.MoveNext())
                {
                    if (!JsonValuesEqual(leftItems.Current, rightItems.Current))
                    {
                        return false;
                    }
                }
                return true;
            case JsonValueKind.Object:
                JsonProperty[] leftProperties = left.EnumerateObject().ToArray();
                JsonProperty[] rightProperties = right.EnumerateObject().ToArray();
                if (leftProperties.Length != rightProperties.Length)
                {
                    return false;
                }
                foreach (JsonProperty leftProperty in leftProperties)
                {
                    if (!right.TryGetProperty(
                            leftProperty.Name,
                            out JsonElement rightValue)
                        || !JsonValuesEqual(leftProperty.Value, rightValue))
                    {
                        return false;
                    }
                }
                return true;
            default:
                return false;
        }
    }

    private static Int32 ReadNonNegativeInteger(
        JsonElement value,
        String name,
        String path)
    {
        if (!value.TryGetInt32(out Int32 result) || result < 0)
        {
            throw Error(path, $"the schema value '{name}' is not a non-negative integer.");
        }
        return result;
    }

    private static Double ReadNumber(
        JsonElement value,
        String name,
        String path)
    {
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetDouble(out Double result)
            || !Double.IsFinite(result))
        {
            throw Error(path, $"the schema value '{name}' is not a finite number.");
        }
        return result;
    }

    private static JsonElement RequireObject(
        JsonElement value,
        String name,
        String path)
    {
        if (!value.TryGetProperty(name, out JsonElement result)
            || result.ValueKind != JsonValueKind.Object)
        {
            throw Error(path, $"the schema does not define object '{name}'.");
        }
        return result;
    }

    private static void RequireArray(
        JsonElement value,
        String name,
        String path)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Error(path, $"the schema value '{name}' must be a list.");
        }
    }

    private static LumuiProtocolException Error(String path, String message)
    {
        return new LumuiProtocolException(
            $"Schema validation failed at {path}: {message}");
    }
}
