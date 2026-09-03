using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// The underscore: legal INSIDE a name, never at the start of one, and a line continuation everywhere else.
///
/// <para>
/// <c>fragment LETTER</c> used to include it, which made a lone <c>_</c> a well-formed identifier. The cost
/// was not a stray acceptance but a silent one: <c>x = 1 +_</c> — a continuation someone forgot the space
/// before — completed as an addition against a variable named <c>_</c> instead of failing. Thirteen corpus
/// cases turned on that one character, the largest single lever the conformance gate has ever shown.
/// </para>
///
/// <para>
/// Removing it was tried once before and reverted, because <c>_</c> alone on an INDENTED line is legal VB6
/// (measured: indented legal, column-one illegal) and <c>NEWLINE</c> had already eaten the space that
/// <c>LINE_CONTINUATION</c> needed to recognise it. The two rules were arguing over the same whitespace.
/// These tests pin both sides of that argument, because fixing either alone regresses the other.
/// </para>
/// </summary>
public class UnderscoreIdentifierTests : BaseVBTestFixture
{
    [Fact]
    public async Task AnUnderscoreMayAppearInsideAName()
    {
        await Run("Dim my_var\nmy_var = 7\nDebug.Print my_var\n");
        AssertDebugLog([new Vb6Value(7)]);
    }

    [Fact]
    public async Task AnUnderscoreMayEndAName()
    {
        await Run("Dim ab_\nab_ = 3\nDebug.Print ab_\n");
        AssertDebugLog([new Vb6Value(3)]);
    }

    [Theory]
    [InlineData("Dim _ab\n_ab = 1\n")]                 // leading underscore
    [InlineData("Dim _\n")]                            // a name that is only an underscore
    [InlineData("Dim x\nx = 1 +_\n2\nDebug.Print x\n")] // continuation with no space before it
    [InlineData("Dim x\nx = 1 + __\n2\nDebug.Print x\n")]
    [InlineData("Dim x\nx = 1 + _z\nDebug.Print x\n")]
    public async Task AnUnderscoreMayNotBeginAName(string code)
    {
        // Every one of these used to parse, because `_`, `__`, `_z` and `_ab` were all identifiers. VB6
        // refuses them all, and the malformed-continuation cases are the reason it matters: an addition
        // against a variable nobody declared is a wrong answer, not a missing error.
        var act = async () => await Run(code);
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task AContinuationOnlyLineBetweenTwoStatements()
    {
        // ` _` alone on its own line. It continues onto the next line, and continuing an empty line yields
        // the next line — so the two statements stay two statements. This is the case that made removing
        // the underscore from the identifier alphabet hard.
        await Run("Debug.Print \"A\"\n _\nDebug.Print \"B\"\n");
        AssertDebugLog([new Vb6Value("A"), new Vb6Value("B")]);
    }

    [Fact]
    public async Task SeveralContinuationOnlyLinesInARow()
    {
        await Run("Debug.Print \"A\"\n _\n  _\n\tGoTo Skip\nDebug.Print \"missed\"\nSkip: Debug.Print \"B\"\n");
        AssertDebugLog([new Vb6Value("A"), new Vb6Value("B")]);
    }

    [Fact]
    public async Task AnUnderscoreAtColumnOneIsNotAContinuation()
    {
        // The discriminator, and it is two space characters wide: `  _` on its own line is legal and `_` is
        // not. VB6 requires whitespace before the underscore literally, not merely as a way of writing the
        // rule down.
        var act = async () => await Run("Dim x\nx = 1 + _\n_\n2\nDebug.Print x\n");
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task AnIndentedContinuationOnlyLineInsideAnExpression()
    {
        // The legal twin of the previous test, differing from it by the indentation alone.
        await Run("Dim x\nx = 1 + _\n  _\n2\nDebug.Print x\n");
        AssertDebugLog([new Vb6Value(3)]);
    }

    [Fact]
    public async Task AnOrdinaryContinuationStillJoinsItsLines()
    {
        await Run("Dim x\nx = 1 + _\n    2\nDebug.Print x\n");
        AssertDebugLog([new Vb6Value(3)]);
    }
}
