using System.Text.Json;

namespace Runesgalder.Mcp;

internal static class ToolArguments
{
    public static void RejectUnknown(
        IDictionary<string, JsonElement> arguments,
        params string[] allowed)
    {
        var allowedNames = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (var name in arguments.Keys)
        {
            if (!allowedNames.Contains(name))
            {
                throw new ToolInputException(
                    ErrorCodes.InvalidRequest,
                    $"Unknown field '{name}'.");
            }
        }
    }

    public static string RequiredString(
        IDictionary<string, JsonElement> arguments,
        string name)
    {
        if (!arguments.TryGetValue(name, out var element))
        {
            throw new ToolInputException(
                ErrorCodes.InvalidRequest,
                $"The field '{name}' is required.");
        }

        return ReadString(element, name);
    }

    public static string? OptionalString(
        IDictionary<string, JsonElement> arguments,
        string name) =>
        arguments.TryGetValue(name, out var element) ? ReadString(element, name) : null;

    public static double? OptionalNumber(
        IDictionary<string, JsonElement> arguments,
        string name)
    {
        if (!arguments.TryGetValue(name, out var element))
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetDouble(out var value) ||
            !double.IsFinite(value))
        {
            throw new ToolInputException(
                ErrorCodes.InvalidRequest,
                $"The field '{name}' must be a number.");
        }

        return value;
    }

    public static int? OptionalInteger(
        IDictionary<string, JsonElement> arguments,
        string name)
    {
        if (!arguments.TryGetValue(name, out var element))
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value))
        {
            throw new ToolInputException(
                ErrorCodes.InvalidRequest,
                $"The field '{name}' must be an integer.");
        }

        return value;
    }

    public static IDictionary<string, JsonElement> ReadObject(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ToolInputException(
                ErrorCodes.InvalidRequest,
                $"The field '{name}' must be an object.");
        }

        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            properties[property.Name] = property.Value;
        }

        return properties;
    }

    private static string ReadString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            throw new ToolInputException(
                ErrorCodes.InvalidRequest,
                $"The field '{name}' must be a string.");
        }

        return element.GetString() ?? string.Empty;
    }
}
