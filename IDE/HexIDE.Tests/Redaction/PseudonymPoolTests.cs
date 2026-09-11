using System.Text.RegularExpressions;
using HexIDE.Redaction;

namespace HexIDE.Tests.Redaction;

/// <summary>
/// The pool and the numbering scheme built on it.
/// </summary>
/// <remarks>
/// The pool is data, and data in this repository rots unless something checks it. Every rule stated in the
/// file's own header is asserted here, including the count — the pool size is the base of the numbering
/// scheme, so resizing it silently changes every name past the first word and two builds would disagree
/// about the same path for no visible reason.
/// </remarks>
public partial class PseudonymPoolTests
{
    [GeneratedRegex("^[a-z]{3,}$")]
    private static partial Regex Wellformed();

    /// <summary>
    /// The pool's size, pinned.
    /// </summary>
    /// <remarks>
    /// Not a round number, and it does not need to be: the target was "beyond most normal codebases, so
    /// about a thousand", and trimming perfectly good words to reach a tidier figure would buy nothing.
    /// Update this deliberately, in the same change as the words.
    /// </remarks>
    private const int Expected = 1318;

    [Fact]
    public void ThePoolIsTheSizeItIsDocumentedToBe() =>
        PseudonymPool.Count.Should().Be(Expected,
            "the pool size is the base of the numbering scheme, so a change to it changes every name past "
          + "the first word — which is a decision, not a side effect of editing a word list");

    [Fact]
    public void EveryWordIsWellformed()
    {
        var offenders = Enumerable.Range(0, PseudonymPool.Count)
            .Select(PseudonymPool.WordAt)
            .Where(w => !Wellformed().IsMatch(w))
            .ToList();

        offenders.Should().BeEmpty(
            "a pseudonym is substituted into URIs, path segments and shell arguments, so every word must be "
          + "lowercase ASCII letters and nothing else. A hyphen in particular would be indistinguishable "
          + "from the separator that joins words when the pool runs out");
    }

    [Fact]
    public void NoWordAppearsTwice()
    {
        var words = Enumerable.Range(0, PseudonymPool.Count).Select(PseudonymPool.WordAt).ToList();

        words.Should().OnlyHaveUniqueItems(
            "a duplicate makes the permutation land two indices on the same word, so two different paths "
          + "would share a name and a reader would believe they were one path");
    }

    [Fact]
    public void ANameIsOneWordUntilThePoolRunsOut()
    {
        var identity = Identity();

        PseudonymPool.NameOf(0, identity).Should().Be(PseudonymPool.Prefix + PseudonymPool.WordAt(0));
        PseudonymPool.NameOf(PseudonymPool.Count - 1, identity)
            .Should().Be(PseudonymPool.Prefix + PseudonymPool.WordAt(PseudonymPool.Count - 1));

        PseudonymPool.NameOf(PseudonymPool.Count - 1, identity).Count(c => c == '-')
            .Should().Be(1, "only the prefix's hyphen — the last word in the pool is still one word");
    }

    [Fact]
    public void PastThePoolTheNameGrowsRatherThanWrappingRound()
    {
        var identity = Identity();

        var justOver = PseudonymPool.NameOf(PseudonymPool.Count, identity);
        justOver.Count(c => c == '-').Should().Be(2, "prefix, then two words");

        var wayOver = PseudonymPool.NameOf((long)PseudonymPool.Count * PseudonymPool.Count + 5, identity);
        wayOver.Count(c => c == '-').Should().Be(3, "prefix, then three words");
    }

    [Fact]
    public void DistinctIndicesNeverShareAName()
    {
        // Across the boundary in both directions, because that is the only place the scheme could
        // collide — a two-word name wrapping onto a one-word one would be silent and permanent.
        var identity = Identity();
        var span = Enumerable.Range(0, 40)
            .SelectMany(i => new long[] { i, PseudonymPool.Count - 20 + i, (long)PseudonymPool.Count + i })
            .Distinct();

        span.Select(i => PseudonymPool.NameOf(i, identity)).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void EveryNameSaysItIsOne()
    {
        var identity = Identity();

        foreach (var index in new long[] { 0, 1, PseudonymPool.Count - 1, PseudonymPool.Count, 999_999 })
        {
            PseudonymPool.NameOf(index, identity).Should().StartWith(PseudonymPool.Prefix,
                "a reader must never mistake a pseudonym for something that was really on the wire");
        }
    }

    [Fact]
    public void ANegativeIndexIsRefused()
    {
        var identity = Identity();
        var act = () => PseudonymPool.NameOf(-1, identity);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// The identity permutation, for asserting the numbering scheme alone.
    /// </summary>
    /// <remarks>
    /// Only ever used here. A session's permutation is drawn from the cryptographic generator, and a
    /// pseudonymiser that used the identity would let anybody holding the pool file read the order values
    /// were first seen in — which is why nothing outside this file can construct one.
    /// </remarks>
    private static int[] Identity() => [.. Enumerable.Range(0, PseudonymPool.Count)];
}
