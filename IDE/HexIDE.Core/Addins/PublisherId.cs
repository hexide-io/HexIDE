using System.Text.RegularExpressions;

namespace HexIDE.Addins;

public static class PublisherId
{
    public static bool IsValid(string? value) =>
        value is { Length: >= 3 and <= 64 }
        && Regex.IsMatch(value, "\\A[a-z0-9]+([.-][a-z0-9]+)*\\z", RegexOptions.CultureInvariant);
}
