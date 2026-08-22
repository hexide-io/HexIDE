using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Issue #126 — <c>Empty</c> is writable.
///
/// <para>
/// It is one of VB6's four value keywords, beside <c>True</c>/<c>False</c>, <c>Null</c> and <c>Nothing</c>,
/// and it was the only one with no token in the grammar. So it lexed as an identifier and failed at name
/// resolution with <i>"Variable not defined (Empty)"</i> — naming a variable the author never wrote, which
/// is the kind of error message that sends someone looking for a typo that is not there.
/// </para>
///
/// <para>
/// The VALUE was always reachable: an un-assigned Variant is Empty, and every behaviour below already
/// matched what HexIDE's <c>EmptyVariant</c> does. Only the spelling was unavailable, which is why this is
/// a grammar change and nothing else. Expectations are vb6.exe output (see <i>The Empty literal</i> in
/// docs/vb6-fidelity-oracle.md).
/// </para>
/// </summary>
public class EmptyLiteralTests : BaseVBTestFixture
{
    [Fact]
    public async Task EmptyIsWritable_AndIsEmpty()
    {
        await Run(
            "Debug.Print TypeName(Empty)\n" +
            "Debug.Print VarType(Empty)\n" +
            "Debug.Print IsEmpty(Empty)\n" +
            "Debug.Print IsNull(Empty)\n");
        debug[0].Value.Should().Be("Empty");
        Convert.ToInt64(debug[1].Value).Should().Be(0);
        debug[2].Value.Should().Be(true);
        debug[3].Value.Should().Be(false);
    }

    [Fact]
    public async Task EmptyEqualsAnUnassignedVariant()
    {
        await Run("Dim u\nDebug.Print TypeName(u)\nDebug.Print (u = Empty)\n");
        debug[0].Value.Should().Be("Empty");
        debug[1].Value.Should().Be(true);
    }

    [Fact]
    public async Task EmptyComparesEqualToBothZeroAndTheEmptyString()
    {
        // Both, which is the whole character of Empty — it is the value that has not decided what it is yet.
        await Run(
            "Debug.Print (Empty = 0)\n" +
            "Debug.Print (Empty = \"\")\n" +
            "Debug.Print (Empty = False)\n");
        debug[0].Value.Should().Be(true);
        debug[1].Value.Should().Be(true);
        debug[2].Value.Should().Be(true);
    }

    [Fact]
    public async Task EmptyComparedToNullIsNull()
    {
        // Not True and not False. Null propagates through comparison, and Empty does not stop it.
        await Run("Debug.Print IsNull(Empty = Null)\n");
        debug[0].Value.Should().Be(true);
    }

    [Fact]
    public async Task AssigningEmptyClearsAVariantBackToEmpty()
    {
        // The idiom this keyword mostly exists for: `v = Empty` resets a Variant, and it was unwritable.
        await Run("Dim v\nv = 5\nv = Empty\nDebug.Print TypeName(v)\nDebug.Print IsEmpty(v)\n");
        debug[0].Value.Should().Be("Empty");
        debug[1].Value.Should().Be(true);
    }

    [Fact]
    public async Task EmptyInArithmeticAndConcatenation()
    {
        await Run(
            "Debug.Print Empty + 1\n" +
            "Debug.Print Empty * 2\n" +
            "Debug.Print Empty & \"x\"\n");
        Convert.ToInt64(debug[0].Value).Should().Be(1);
        Convert.ToInt64(debug[1].Value).Should().Be(0);
        debug[2].Value.Should().Be("x");
    }

    [Fact]
    public async Task StoringEmptyIntoADeclaredVariableCoercesIt()
    {
        // The row #125 had to test through an un-assigned Variant because the literal did not exist.
        await Run(
            "Dim i As Integer, s As String\n" +
            "i = Empty\ns = Empty\n" +
            "Debug.Print i\nDebug.Print \"[\" & s & \"]\"\n");
        Convert.ToInt64(debug[0].Value).Should().Be(0);
        debug[1].Value.Should().Be("[]");
    }

    [Fact]
    public async Task EmptyIsCaseInsensitive()
    {
        // VB6 keywords are, and the lexer fragments spell it out letter by letter, so this is worth pinning.
        await Run("Debug.Print IsEmpty(EMPTY)\nDebug.Print IsEmpty(empty)\nDebug.Print IsEmpty(eMpTy)\n");
        debug.Should().AllSatisfy(d => d.Value.Should().Be(true));
    }
}
