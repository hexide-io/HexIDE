using System.Collections.Frozen;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// VB6's text compare: what <c>Option Compare Text</c> gives the comparison operators and <c>Select Case</c>, what a
/// Collection's keys use, and what a compare of 1 gives the string functions. Measured on vb6.exe under en-GB, native and
/// p-code alike (docs/vb6-fidelity-oracle.md, "Option Compare Text", #1567). There are two rules, because vb6.exe has two:
/// <list type="bullet">
/// <item><b>Comparing</b> two strings (<see cref="Compare"/>) goes a level at a time. First the letters' primary weights,
/// case ignored, an expansion such as ß counting as its two letters, and the word-sort and ignorable characters skipped;
/// then their accents, left to right; then the word-sort characters (the hyphen, the apostrophe, the two dashes and the
/// control characters), where one that comes later in the string sorts first. Chr(0) and the soft hyphen are ignored
/// altogether, and an empty string is below any other, those two included.</item>
/// <item><b>Searching</b> (<see cref="IndexOf"/>, <see cref="LastIndexOf"/>) matches a character at a time, case ignored
/// and the seven expansions read as their letters, and nothing else merges: a hyphen, Chr(0) or ¹ matches only itself,
/// although each compares equal to something else. InStr, InStrRev and Filter take a match that starts or ends inside an
/// expansion, so "s" is in "ß"; Replace and Split take only whole characters.</item>
/// </list>
/// <para>
/// The rows (VbTextCompare.Table.cs) are vb6.exe's, for the characters Chr(0) to Chr(255) give. Any other character sorts
/// after all of them, by its invariant lower case. That is unmeasured, and so is every locale but en-GB, where a Windows
/// sort may order letters differently (interpreter-gaps.md).
/// </para>
/// </summary>
internal static partial class VbTextCompare
{
    private enum Kind { Weighted, WordSort, Ignorable }

    private readonly record struct Row(char Character, Kind Kind, int Weight, int Accent);

    private static readonly FrozenDictionary<char, Row> ByCharacter = Rows.ToFrozenDictionary(r => r.Character);

    private static readonly FrozenDictionary<char, string> LettersOf =
        Expansions.ToFrozenDictionary(e => e.Character, e => e.Letters);

    // A character the rows do not hold sorts after every one they do, by its invariant lower case. Unmeasured.
    private const int Unmeasured = 0x10000;

    /// <summary>A comparer for keys compared as text, as a Collection's are: equal where <see cref="Compare"/> is 0, and
    /// hashed on what that compares, so "straße" and "STRASSE" are one key.</summary>
    public static StringComparer Comparer { get; } = new TextComparer();

    /// <summary>-1, 0 or 1, as <c>StrComp(a, b, vbTextCompare)</c> answers.</summary>
    public static int Compare(string a, string b)
    {
        // An empty string is below any other, even one of only ignorable characters, which compares equal to nothing else.
        if (a.Length == 0 || b.Length == 0)
            return (a.Length > 0 ? 1 : 0) - (b.Length > 0 ? 1 : 0);
        var order = CompareLetters(a, b, accents: false);
        if (order == 0)
            order = CompareLetters(a, b, accents: true);
        if (order == 0)
            order = CompareWordSort(a, b);
        return order;
    }

    /// <summary>Whether the two compare equal as text.</summary>
    public static bool AreEqual(string a, string b) => Compare(a, b) == 0;

    private static int CompareLetters(string a, string b, bool accents)
    {
        var x = new Letters(a);
        var y = new Letters(b);
        while (true)
        {
            bool hasX = x.Next(out var weightX, out var accentX), hasY = y.Next(out var weightY, out var accentY);
            if (!hasX || !hasY)
                return (hasX ? 1 : 0) - (hasY ? 1 : 0);
            var order = accents ? accentX.CompareTo(accentY) : weightX.CompareTo(weightY);
            if (order != 0)
                return Math.Sign(order);
        }
    }

    private static int CompareWordSort(string a, string b)
    {
        var x = new WordSortMarks(a);
        var y = new WordSortMarks(b);
        while (true)
        {
            bool hasX = x.Next(out var positionX, out var rankX), hasY = y.Next(out var positionY, out var rankY);
            if (!hasX || !hasY)
                return (hasX ? 1 : 0) - (hasY ? 1 : 0);
            if (positionX != positionY)
                return positionX > positionY ? -1 : 1;   // a later one sorts first: "a-bc" is below "-abc"
            if (rankX != rankY)
                return Math.Sign(rankX - rankY);
        }
    }

    private static int Hash(string s)
    {
        var hash = new HashCode();
        var letters = new Letters(s);
        while (letters.Next(out var weight, out var accent))
        {
            hash.Add(weight);
            hash.Add(accent);
        }
        var marks = new WordSortMarks(s);
        while (marks.Next(out var position, out var rank))
        {
            hash.Add(position);
            hash.Add(rank);
        }
        return hash.ToHashCode();
    }

    // The letters a string compares by, in order: an expansion gives its two, and a word-sort or ignorable character none.
    private struct Letters(string s)
    {
        private int index;
        private string? expansion;
        private int expansionIndex;

        public bool Next(out int weight, out int accent)
        {
            if (expansion != null)
            {
                Weigh(expansion[expansionIndex++], out weight, out accent);
                if (expansionIndex == expansion.Length)
                    expansion = null;
                return true;
            }
            while (index < s.Length)
            {
                var c = s[index++];
                if (LettersOf.TryGetValue(c, out var letters))
                {
                    expansion = letters;
                    expansionIndex = 1;
                    Weigh(letters[0], out weight, out accent);
                    return true;
                }
                if (ByCharacter.TryGetValue(c, out var row) && row.Kind != Kind.Weighted)
                    continue;
                Weigh(c, out weight, out accent);
                return true;
            }
            weight = accent = 0;
            return false;
        }
    }

    private static void Weigh(char c, out int weight, out int accent)
    {
        if (ByCharacter.TryGetValue(c, out var row))
        {
            weight = row.Weight;
            accent = row.Accent;
            return;
        }
        weight = Unmeasured + char.ToLowerInvariant(c);
        accent = 0;
    }

    // The word-sort characters of a string, each with its position: how many letters come before it, an expansion counting
    // as two and an ignorable character as none (measured: "ß-" is below "s-s", and Chr(0) moves nothing).
    private struct WordSortMarks(string s)
    {
        private int index;
        private int letters;

        public bool Next(out int position, out int rank)
        {
            while (index < s.Length)
            {
                var c = s[index++];
                if (LettersOf.TryGetValue(c, out var expansion))
                {
                    letters += expansion.Length;
                    continue;
                }
                if (ByCharacter.TryGetValue(c, out var row))
                {
                    if (row.Kind == Kind.Ignorable)
                        continue;
                    if (row.Kind == Kind.WordSort)
                    {
                        position = letters;
                        rank = row.Weight;
                        return true;
                    }
                }
                letters++;
            }
            position = rank = 0;
            return false;
        }
    }

    private sealed class TextComparer : StringComparer
    {
        public override int Compare(string? x, string? y) =>
            x is null || y is null ? (x is null ? 0 : 1) - (y is null ? 0 : 1) : VbTextCompare.Compare(x, y);

        public override bool Equals(string? x, string? y) =>
            x is null || y is null ? x is null && y is null : VbTextCompare.Compare(x, y) == 0;

        public override int GetHashCode(string obj) => Hash(obj);
    }

    /// <summary>
    /// Where <paramref name="value"/> is first found in <paramref name="source"/>, from the character at
    /// <paramref name="startIndex"/> on, searching as text: the 0-based index of the character the match starts in, or -1.
    /// <paramref name="length"/> is how many characters of <paramref name="source"/> it spans. With
    /// <paramref name="wholeCharacters"/>, as Replace and Split search, a match must start and end on whole characters;
    /// without it, as InStr and Filter search, a match may start or end inside an expansion. An empty value is found where
    /// the search starts.
    /// </summary>
    public static int IndexOf(string source, string value, int startIndex, bool wholeCharacters, out int length) =>
        new Searchable(source).IndexOf(value, startIndex, wholeCharacters, out length);

    /// <summary>A string folded once for searching as text, for a caller that searches it many times, as Replace and Split
    /// do: folding it again for every match would make each of them quadratic in the string's length.</summary>
    internal sealed class Searchable
    {
        private readonly string hay;
        private readonly int[] from;
        private readonly int[] firstLetter;
        private string? lastValue;
        private string lastNeedle = "";

        public Searchable(string source)
        {
            hay = Fold(source, 0, out from);
            // The first letter each character gives; the string's length maps to the end.
            firstLetter = new int[source.Length + 1];
            for (int c = 0, u = 0; c <= source.Length; c++)
            {
                while (u < from.Length && from[u] < c)
                    u++;
                firstLetter[c] = u;
            }
        }

        /// <summary>As <see cref="VbTextCompare.IndexOf"/>.</summary>
        public int IndexOf(string value, int startIndex, bool wholeCharacters, out int length)
        {
            length = 0;
            if (value.Length == 0)
                return startIndex;
            if (!ReferenceEquals(value, lastValue))
            {
                lastNeedle = Fold(value, 0, out _);
                lastValue = value;
            }
            var needle = lastNeedle;
            for (var u = firstLetter[Math.Min(startIndex, firstLetter.Length - 1)]; u + needle.Length <= hay.Length; u++)
            {
                if (!Matches(hay, u, needle))
                    continue;
                var end = u + needle.Length - 1;
                if (wholeCharacters && !(StartsCharacter(from, u) && EndsCharacter(from, end)))
                    continue;
                length = from[end] - from[u] + 1;
                return from[u];
            }
            return -1;
        }
    }

    /// <summary>
    /// Where <paramref name="value"/> is last found in <paramref name="source"/>, as InStrRev searches as text: the 0-based
    /// index of the character the match starts in, for the last match that ends at or before
    /// <paramref name="lastIndex"/>, or -1. A match may start or end inside an expansion.
    /// </summary>
    public static int LastIndexOf(string source, string value, int lastIndex)
    {
        if (value.Length == 0)
            return lastIndex;
        var needle = Fold(value, 0, out _);
        var hay = Fold(source, 0, out var from);
        for (var u = hay.Length - needle.Length; u >= 0; u--)
            if (from[u + needle.Length - 1] <= lastIndex && Matches(hay, u, needle))
                return from[u];
        return -1;
    }

    private static bool Matches(string hay, int at, string needle)
    {
        for (var i = 0; i < needle.Length; i++)
            if (hay[at + i] != needle[i])
                return false;
        return true;
    }

    private static bool StartsCharacter(int[] from, int unit) => unit == 0 || from[unit - 1] != from[unit];

    private static bool EndsCharacter(int[] from, int unit) => unit == from.Length - 1 || from[unit + 1] != from[unit];

    // A string as a search reads it, from the character at start: case folded, an expansion as its letters, and every other
    // character as itself. from[i] is the index of the character the i-th letter came from.
    private static string Fold(string s, int start, out int[] from)
    {
        var letters = new System.Text.StringBuilder(s.Length - start);
        var origin = new List<int>(s.Length - start);
        for (var i = start; i < s.Length; i++)
        {
            if (LettersOf.TryGetValue(s[i], out var expansion))
            {
                letters.Append(expansion);
                for (var k = 0; k < expansion.Length; k++)
                    origin.Add(i);
                continue;
            }
            letters.Append(char.ToLowerInvariant(s[i]));
            origin.Add(i);
        }
        from = [.. origin];
        return letters.ToString();
    }
}
