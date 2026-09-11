using System.Reflection;
using System.Text;

namespace HexIDE.Redaction;

/// <summary>
/// The fixed sequence of words a pseudonym is drawn from, and the numbering scheme that never runs out.
/// </summary>
/// <remarks>
/// <b>Why words rather than <c>path-1</c>, <c>path-2</c>.</b> A pseudonymised trace is read by a person
/// holding two versions of the same conversation side by side, and numbered placeholders are the worst
/// possible thing to ask anyone to compare: <c>path-17</c> and <c>path-71</c> look alike, sort adjacent, and
/// carry no shape at all. Words are distinguishable at a glance and memorable for as long as the reading
/// takes, which is exactly as long as a session-scoped pseudonym needs to be memorable for. That they are
/// also faintly ridiculous is deliberate and load-bearing — a reader must never mistake
/// <c>hx-grumpy-toad</c> for something that was really on the wire.
///
/// <para>
/// <b>The pool is finite and the numbering is not.</b> Past the last word, an index is written in base
/// <see cref="Count"/> and each digit picks a word, so index one million reads as three words joined by
/// hyphens rather than failing or wrapping round onto a word already in use. Distinct indices always give
/// distinct names: a multi-digit name is longer than any shorter one, and base-N notation is injective
/// within a length.
/// </para>
///
/// <para>
/// <b>The pool's order carries no information.</b> It is fixed, so two builds agree about which word is at
/// which index, and nothing ever indexes it directly: a session draws a permutation from a cryptographic
/// seed and every lookup goes through that. Somebody holding this file learns nothing about a capture.
/// </para>
/// </remarks>
public static class PseudonymPool
{
    /// <summary>
    /// Every prefix a pseudonym starts with, so one is never mistaken for something that was real.
    /// </summary>
    /// <remarks>
    /// Short, lowercase and made of characters that are legal in a URI, a Windows path segment and a shell
    /// argument, because a pseudonym is substituted into all three. Angle brackets were the first idea and
    /// were dropped for exactly that reason: <c>file:///&lt;toad&gt;/x</c> is not a URI, and an export whose
    /// point is that it replays would stop replaying.
    /// </remarks>
    public const string Prefix = "hx-";

    private static readonly string[] Words = Load();

    /// <summary>How many words the pool holds, and therefore the base of the numbering scheme.</summary>
    /// <remarks>
    /// <b>Not a round number, and it does not need to be.</b> The target was "beyond most normal
    /// codebases, so about a thousand"; the list came out at this, and trimming good words to reach a
    /// rounder figure would buy nothing. Nothing is arithmetically nicer about a power of ten here — the
    /// scheme is base-N for whatever N is.
    /// </remarks>
    public static int Count => Words.Length;

    /// <summary>The word at an index, which is a detail of the pool rather than a way to name anything.</summary>
    internal static string WordAt(int index) => Words[index];

    /// <summary>
    /// The name for an index, given a permutation to read the pool through.
    /// </summary>
    /// <param name="index">A number assigned in the order values were first seen. Never negative.</param>
    /// <param name="permutation">
    /// A bijection on <c>0 .. Count-1</c>. Supplying the identity would make names predictable from this
    /// file, which is why nothing in this type creates one.
    /// </param>
    internal static string NameOf(long index, int[] permutation)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        // Least significant digit first, then reversed: the leading word is the one that changes slowest,
        // so consecutive indices differ in their last word and read as neighbours.
        var digits = new List<string>(3);
        var remaining = index;
        do
        {
            digits.Add(Words[permutation[(int)(remaining % Count)]]);
            remaining /= Count;
        }
        while (remaining > 0);

        digits.Reverse();

        var name = new StringBuilder(Prefix);
        for (var i = 0; i < digits.Count; i++)
        {
            if (i > 0) name.Append('-');
            name.Append(digits[i]);
        }

        return name.ToString();
    }

    private static string[] Load()
    {
        var assembly = typeof(PseudonymPool).Assembly;
        var name = Array.Find(assembly.GetManifestResourceNames(), n => n.EndsWith("pseudonym-pool.txt", StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                "The pseudonym pool is missing from the assembly. It is an embedded resource, so this means "
              + "the build lost it rather than that a file is absent on disk.");

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var words = new List<string>(1400);
        while (reader.ReadLine() is { } line)
        {
            var word = line.Trim();
            if (word.Length == 0 || word[0] == '#') continue;
            words.Add(word);
        }

        return [.. words];
    }
}
