using System.Globalization;
using System.Text.Json;

namespace HexIDE.Desktop.Server;

/// <summary>
/// What is wrong with a call's arguments, as far as its tool's input schema can tell, before the SDK binds them (#1270).
/// </summary>
/// <remarks>
/// <para>
/// The SDK binds arguments without checking them against the schema. A missing or mistyped one either throws inside its
/// binding, which read as a defect in the tool, or binds: JSON null reaches a parameter that cannot hold it, and the tool
/// fails in its own code, asking to be reported. Checked here first, each problem is named, all at once, and nothing runs.
/// </para>
/// <para>
/// It is no stricter than the binding. A number may come as a string, as the SDK's serializer reads one, and an argument
/// the tool does not take is ignored by the SDK, so it is named only beside a real problem, as the likely misspelling.
/// What the schema cannot see, such as an integer too big for its parameter, still fails in the binding, and
/// <see cref="ToolFailures"/> reports that as a refusal too.
/// </para>
/// </remarks>
internal static class ArgumentCheck
{
    public static List<string> Problems(IDictionary<string, JsonElement>? arguments, JsonElement? schema)
    {
        var problems = new List<string>();
        if (schema is not { ValueKind: JsonValueKind.Object } s)
            return problems;
        arguments ??= new Dictionary<string, JsonElement>();

        if (s.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
            foreach (var name in required.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.String))
                if (!arguments.ContainsKey(name.GetString()!))
                    problems.Add($"'{name.GetString()}' is required");

        if (!s.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
            return problems;

        var unknown = new List<string>();
        foreach (var (name, value) in arguments)
        {
            if (!properties.TryGetProperty(name, out var declared))
                unknown.Add(name);
            else if (Mismatch(value, declared) is { } why)
                problems.Add($"'{name}' {why}");
        }
        if (problems.Count > 0)
            problems.AddRange(unknown.Select(name => $"it takes no '{name}' parameter"));
        return problems;
    }

    /// <summary>Why <paramref name="value"/> does not fit <paramref name="declared"/>, or null when it does.</summary>
    private static string? Mismatch(JsonElement value, JsonElement declared)
    {
        var types = Types(declared);
        if (types.Count == 0)
            return null;
        if (!types.Any(t => Fits(value, t)))
            return $"must be {string.Join(" or ", types.Where(t => t != "null"))}, not {Shown(value)}";
        if (value.ValueKind == JsonValueKind.Array && declared.TryGetProperty("items", out var items))
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                if (Mismatch(item, items) is { } why)
                    return $"item {index} {why}";
                index++;
            }
        }
        return null;
    }

    private static List<string> Types(JsonElement declared) =>
        !declared.TryGetProperty("type", out var type) ? []
        : type.ValueKind == JsonValueKind.String ? [type.GetString()!]
        : type.ValueKind == JsonValueKind.Array
            ? [.. type.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.String).Select(t => t.GetString()!)]
            : [];

    // As the SDK's serializer reads them: a number from a string too, and a whole number only for an integer.
    private static bool Fits(JsonElement value, string type) => type switch
    {
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "integer" => value.ValueKind == JsonValueKind.Number ? value.TryGetInt64(out _)
                     : value.ValueKind == JsonValueKind.String
                       && long.TryParse(value.GetString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _),
        "number" => value.ValueKind == JsonValueKind.Number
                    || value.ValueKind == JsonValueKind.String
                       && double.TryParse(value.GetString(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint
                                          | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out _),
        "string" => value.ValueKind == JsonValueKind.String,
        "array" => value.ValueKind == JsonValueKind.Array,
        "object" => value.ValueKind == JsonValueKind.Object,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => true,
    };

    // The value itself where it is short, so 2.5 reads as 2.5 rather than as "a number".
    private static string Shown(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Array => "an array",
        JsonValueKind.Object => "an object",
        _ when value.GetRawText() is { Length: <= 40 } raw => raw,
        JsonValueKind.String => "a long string",
        _ => value.ValueKind.ToString().ToLowerInvariant(),
    };
}
