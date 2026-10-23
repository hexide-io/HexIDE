namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// VB6's <c>Like</c> (#1547): whether a string matches a pattern. Measured with <c>vb6.exe /make</c>, native and p-code
/// alike (the oracle's "Like"):
/// <list type="bullet">
/// <item><c>?</c> is any one character, a UTF-16 code unit (half a surrogate pair is one); <c>*</c> is any run, the empty
/// one included; <c>#</c> is a digit, <c>0</c> to <c>9</c> or a fullwidth <c>０</c> to <c>９</c>, and no other.</item>
/// <item><c>[list]</c> is one character in the list and <c>[!list]</c> one not in it. A list holds characters and
/// ranges, <c>a-z</c>, compared by code unit, not by the ANSI byte. A <c>-</c> at either end, or after a range, is itself.
/// Inside a list <c>*</c>, <c>?</c>, <c>#</c> and <c>[</c> are themselves, and <c>[!]</c> is a <c>!</c>.</item>
/// <item><c>[]</c> matches nothing and takes no character, even at the end of the string.</item>
/// <item>A pattern is checked only as far as it is matched. A list is read when a character is there to match it: it
/// needs its <c>]</c>, or the error is 93, "Invalid pattern string", and it is read item by item, so a range written
/// backwards, <c>[z-a]</c>, is 93 only if no item before it matched. With no character left, a list is simply no
/// match.</item>
/// <item>Under <c>Option Compare Text</c> a character matches its other case, and a range's bounds and the character
/// are each taken in lower case before they are compared, so <c>[a-Z]</c> is the same list as <c>[a-z]</c> and
/// <c>[Z-a]</c> is backwards. A character with no case pair of its own, such as the Kelvin sign or a dotted capital I,
/// is left as it is.</item>
/// </list>
/// VB6's Text comparison also takes a ligature or a sharp s for the letters it stands for, so <c>"ß" Like "ss"</c> is
/// True there. Matching character by character cannot do that, so it is False here (#1479).
/// </summary>
internal static class VbLike
{
    /// <summary>Whether <paramref name="text"/> matches <paramref name="pattern"/>; 93 for a list it cannot read.</summary>
    public static bool Matches(string text, string pattern, bool textCompare)
    {
        int s = 0, p = 0;
        int starPattern = -1, starText = 0;
        while (true)
        {
            if (p < pattern.Length)
            {
                var element = pattern[p];
                if (element == '*')
                {
                    starPattern = ++p;
                    starText = s;
                    continue;
                }
                if (element == '[' && p + 1 < pattern.Length && pattern[p + 1] == ']')
                {
                    p += 2;   // the empty list, which takes no character
                    continue;
                }
                if (s < text.Length)
                {
                    var c = text[s];
                    var next = p + 1;
                    var matched = element switch
                    {
                        '?' => true,
                        '#' => c is >= '0' and <= '9' or >= '０' and <= '９',
                        '[' => InList(c, pattern, p, textCompare, out next),
                        _ => Same(c, element, textCompare),
                    };
                    if (matched)
                    {
                        s++;
                        p = next;
                        continue;
                    }
                }
            }
            else if (s == text.Length)
            {
                return true;
            }
            // No match here: let the last * take one more character, if there is one.
            if (starPattern < 0 || starText >= text.Length)
                return false;
            s = ++starText;
            p = starPattern;
        }
    }

    // Whether c is in the list opening at pattern[open], read item by item until one matches; next is past its ].
    private static bool InList(char c, string pattern, int open, bool textCompare, out int next)
    {
        var close = pattern.IndexOf(']', open + 1);
        if (close < 0)
            throw new VBRunTimeException(VBStandardError.InvalidPatternString);
        next = close + 1;
        var first = open + 1;
        var negated = pattern[first] == '!' && close > first + 1;
        if (negated)
            first++;
        var x = textCompare ? Fold(c) : c;
        for (var i = first; i < close;)
        {
            if (i + 2 < close && pattern[i + 1] == '-')
            {
                var (low, high) = textCompare ? (Fold(pattern[i]), Fold(pattern[i + 2])) : (pattern[i], pattern[i + 2]);
                if (low > high)
                    throw new VBRunTimeException(VBStandardError.InvalidPatternString);
                if (x >= low && x <= high)
                    return !negated;
                i += 3;
            }
            else
            {
                if (Same(c, pattern[i], textCompare))
                    return !negated;
                i++;
            }
        }
        return negated;
    }

    private static bool Same(char a, char b, bool textCompare) => a == b || (textCompare && Fold(a) == Fold(b));

    // A character in lower case where it has a case pair of its own, and otherwise as it is: the Kelvin sign's lower case
    // is k, whose upper case is K, so it has none and stays (measured: it is not in [j-l] under Text).
    private static char Fold(char c)
    {
        var lower = char.ToLowerInvariant(c);
        return lower != c && char.ToUpperInvariant(lower) == c ? lower : c;
    }
}
