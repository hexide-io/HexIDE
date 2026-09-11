using HexIDE.Redaction;

namespace HexIDE.Tests.Redaction;

/// <summary>
/// The session mapping: stable within a session, unrelated between them, and never colliding.
/// </summary>
/// <remarks>
/// <b>Every property here is one a redactor is worthless without.</b> Stability is what makes a redacted
/// trace readable at all; the case behaviour is what keeps a normalisation defect diagnosable after
/// redaction; non-collision is what stops a reader believing two paths were one. The session-scope
/// assertions matter for a different reason — the weakness of any tokenisation scheme is correlation
/// across captures, and session scope is the entire mitigation.
/// </remarks>
public class PseudonymiserTests
{
    [Fact]
    public void TheSameValueAlwaysGetsTheSameName()
    {
        var names = new Pseudonymiser(new Random(1));

        var first = names.For("Projects");
        names.For("Projects").Should().Be(first);
        names.For("Projects").Should().Be(first);
    }

    [Fact]
    public void TwoValuesDifferingOnlyInCaseShareAWordAndDifferInCapitalisation()
    {
        // THE assertion, and the reason this is pseudonymisation rather than removal. A client sending one
        // drive-letter case and a server echoing another cost this project real time, and it becomes
        // invisible again the moment both spellings turn into the same token.
        var names = new Pseudonymiser(new Random(2));

        var lower = names.For("projects");
        var upperFirst = names.For("Projects");
        var shouty = names.For("PROJECTS");

        lower.Should().NotBe(upperFirst, "two different strings must not come back as one name");
        upperFirst.Should().NotBe(shouty);

        lower.ToLowerInvariant().Should().Be(upperFirst.ToLowerInvariant(),
            "the same word, so a reader can see these are the same path spelled differently");
        lower.ToLowerInvariant().Should().Be(shouty.ToLowerInvariant());

        upperFirst.Should().MatchRegex("^hx-[A-Z]", "a leading capital is projected onto the name");
        shouty.Should().Be(PseudonymPool.Prefix + shouty[PseudonymPool.Prefix.Length..].ToUpperInvariant());
        shouty.Should().MatchRegex("^hx-[A-Z]+$", "all upper in, all upper out");
    }

    [Fact]
    public void ACasingTheSchemeCannotExpressGetsASuffixRatherThanACollision()
    {
        // Three case shapes are representable and these two are neither. Left alone they would render
        // identically, which is the one failure worse than an ugly name: a reader seeing one name while
        // looking at two different strings.
        var names = new Pseudonymiser(new Random(3));

        var first = names.For("aBc");
        var second = names.For("abC");

        first.Should().NotBe(second);
        second.Should().StartWith(first + "~");
    }

    [Fact]
    public void TwoDifferentValuesNeverShareAName()
    {
        var names = new Pseudonymiser(new Random(4));

        var assigned = Enumerable.Range(0, 4000).Select(i => names.For($"value-{i}")).ToList();

        assigned.Should().OnlyHaveUniqueItems(
            "four thousand values against a pool of about thirteen hundred, so most of these are two-word "
          + "names — which is the part of the scheme that could collide and does not");
    }

    [Fact]
    public void ThePermutationIsABijectionOverTheWholePool()
    {
        // Every word reachable, each exactly once. A shuffle with an off-by-one — the classic Fisher-Yates
        // mistake — leaves one index never swapped and another reachable twice, which would show up here
        // and nowhere else until two paths silently shared a name.
        var names = new Pseudonymiser(new Random(5));

        var words = Enumerable.Range(0, PseudonymPool.Count)
            .Select(i => names.For($"v{i}"))
            .Select(n => n[PseudonymPool.Prefix.Length..])
            .ToList();

        words.Should().OnlyHaveUniqueItems();
        words.Should().HaveCount(PseudonymPool.Count);
        words.Order(StringComparer.Ordinal).Should().Equal(
            Enumerable.Range(0, PseudonymPool.Count).Select(PseudonymPool.WordAt).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheNamingOrderIsNotThePoolOrder()
    {
        // The teeth for the permutation existing at all. With no shuffle the first values seen would get
        // the first words in the file, so anybody holding the pool could read the order a session saw
        // things in. Thirty values against a shuffled pool agreeing with the file's order is not something
        // that happens.
        var names = new Pseudonymiser();

        var drawn = Enumerable.Range(0, 30).Select(i => names.For($"v{i}")).ToList();
        var inFileOrder = Enumerable.Range(0, 30)
            .Select(i => PseudonymPool.Prefix + PseudonymPool.WordAt(i))
            .ToList();

        drawn.Should().NotEqual(inFileOrder);
    }

    [Fact]
    public void TwoSessionsDisagreeAboutTheSameValues()
    {
        // Correlation across captures is the weakness of every tokenisation scheme, and a mapping that
        // survived a restart would be the thing that makes a pseudonym stop being one.
        var first = new Pseudonymiser();
        var second = new Pseudonymiser();

        var values = Enumerable.Range(0, 50).Select(i => $"folder-{i}").ToList();

        values.Select(first.For).Should().NotEqual(values.Select(second.For),
            "two sessions agreeing about fifty values would mean the permutation is not per session");
    }

    [Fact]
    public void CountingIsByDistinctValueRatherThanByCall()
    {
        var names = new Pseudonymiser(new Random(6));

        names.For("one");
        names.For("one");
        names.For("two");

        names.Assigned.Should().Be(2, "the disclosure summary sizes what is leaving, not how often it was "
                                    + "asked for");
    }

    [Fact]
    public void ACaseVariantIsNotASecondValue()
    {
        var names = new Pseudonymiser(new Random(7));

        names.For("Alpha");
        names.For("alpha");

        names.Assigned.Should().Be(1, "they are one path, and telling a person otherwise would overstate "
                                    + "what an export contains");
    }

    [Fact]
    public void AnEmptyValueIsLeftAlone() =>
        new Pseudonymiser(new Random(8)).For("").Should().BeEmpty();
}
