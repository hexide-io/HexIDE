using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HexIDE.Lsp;

internal static class FileOperationFilters
{
    public static bool Matches(JsonElement? capabilities, string operation, string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            || capabilities is not { ValueKind: JsonValueKind.Object } caps
            || !caps.TryGetProperty("workspace", out var workspace) || workspace.ValueKind != JsonValueKind.Object
            || !workspace.TryGetProperty("fileOperations", out var operations) || operations.ValueKind != JsonValueKind.Object
            || !operations.TryGetProperty(operation, out var registration) || registration.ValueKind != JsonValueKind.Object
            || !registration.TryGetProperty("filters", out var filters) || filters.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var filter in filters.EnumerateArray())
        {
            if (filter.ValueKind != JsonValueKind.Object) continue;
            if (filter.TryGetProperty("scheme", out var scheme)
                && (scheme.ValueKind != JsonValueKind.String || scheme.GetString() != parsed.Scheme)) continue;
            if (!filter.TryGetProperty("pattern", out var pattern) || pattern.ValueKind != JsonValueKind.Object
                || !pattern.TryGetProperty("glob", out var glob) || glob.ValueKind != JsonValueKind.String) continue;
            // These entry points report files; folder operations have separate semantics.
            if (pattern.TryGetProperty("matches", out var kind) && (kind.ValueKind != JsonValueKind.String || kind.GetString() != "file")) continue;
            var ignoreCase = pattern.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Object
                && options.TryGetProperty("ignoreCase", out var flag) && flag.ValueKind == JsonValueKind.True;
            try
            {
                if (Regex.IsMatch(Uri.UnescapeDataString(parsed.AbsolutePath), "\\A" + Glob(glob.GetString()!) + "\\z",
                        RegexOptions.CultureInvariant | RegexOptions.Singleline | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None),
                        TimeSpan.FromMilliseconds(100))) return true;
            }
            catch (ArgumentException) { /* malformed server pattern */ }
            catch (RegexMatchTimeoutException) { /* a server pattern cannot hold up a disk operation */ }
        }
        return false;
    }

    private static string Glob(string glob)
    {
        var result = new StringBuilder();
        var groups = 0;
        for (var i = 0; i < glob.Length; i++)
        {
            switch (glob[i])
            {
                case '*':
                    if (i + 1 < glob.Length && glob[i + 1] == '*')
                    {
                        i++;
                        if (i + 1 < glob.Length && glob[i + 1] == '/')
                        {
                            i++;
                            result.Append("(?:.*/)?");
                        }
                        else result.Append(".*");
                    }
                    else result.Append("[^/]*");
                    break;
                case '?': result.Append("[^/]"); break;
                case '{': result.Append("(?:"); groups++; break;
                case '}':
                    if (groups == 0) throw new ArgumentException("Unbalanced glob group");
                    result.Append(')'); groups--; break;
                case ',' when groups > 0: result.Append('|'); break;
                case '[':
                    var end = glob.IndexOf(']', i + 1);
                    if (end < 0) throw new ArgumentException("Unclosed glob range");
                    result.Append("(?!/)[");
                    if (i + 1 < end && glob[i + 1] == '!') { result.Append('^'); i++; }
                    for (var j = i + 1; j < end; j++)
                    {
                        if (glob[j] is '\\' or '[' or '^') result.Append('\\');
                        result.Append(glob[j]);
                    }
                    result.Append(']'); i = end; break;
                default: result.Append(Regex.Escape(glob[i].ToString())); break;
            }
        }
        if (groups != 0) throw new ArgumentException("Unclosed glob group");
        return result.ToString();
    }
}
