namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// VB6's case mapping, as UCase and LCase do it: a character takes its other case only where that case maps back to it. So
/// ƒ is Ƒ and ÿ is Ÿ in upper case, while µ, ς, ſ and ı keep theirs, and so do the Kelvin sign and İ in lower case, whose
/// Unicode case belongs to another character (measured over Chr(0) to Chr(255) and those six, #1560). It is the rule
/// VbLike's text fold was measured to follow (#1547).
/// </summary>
internal static class VbCase
{
    public static char Upper(char c)
    {
        var upper = char.ToUpperInvariant(c);
        return upper != c && char.ToLowerInvariant(upper) == c ? upper : c;
    }

    public static char Lower(char c)
    {
        var lower = char.ToLowerInvariant(c);
        return lower != c && char.ToUpperInvariant(lower) == c ? lower : c;
    }

    public static string Upper(string s) => Map(s, Upper);

    public static string Lower(string s) => Map(s, Lower);

    private static string Map(string s, Func<char, char> map) =>
        string.Create(s.Length, (s, map), static (span, state) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = state.map(state.s[i]);
        });
}
