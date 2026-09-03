using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// VB6 Enums: what a member's value may be, and that a member is a CONSTANT.
///
/// <para>
/// A member's value is a constant EXPRESSION, not a literal — measured. <c>&amp;H80000005</c> is
/// −2147483643 (the high bit makes it a negative Long), <c>&amp;O17</c> is 15, and <c>xFirst + 1</c>,
/// <c>2 ^ 3</c> and bit-ors of earlier members are all ordinary VB6. The old rule here was
/// <c>long.TryParse</c>, so every one of those threw — and issue #176 understated it as "must be decimal
/// literals", which is the symptom rather than the rule.
/// </para>
///
/// <para>
/// Evaluation is a single forward walk, and that is measured rather than assumed: VB6 refuses a member
/// that references a LATER member, and one that references a later <c>Const</c>, both with "Constant
/// expression required". So the lazy-memoised treatment CLAUDE.md prescribes for <c>Const</c> — which
/// exists precisely because <c>Const</c> is order-independent — is not wanted here.
/// </para>
/// </summary>
public class EnumTests : BaseVBTestFixture
{
    [Fact]
    public async Task AHexMemberWithTheHighBitSetIsANegativeLong()
    {
        // Measured -2147483643. This is Microsoft's own canonical Enum example shape — every &H colour
        // constant — and it threw.
        await Run("Public Enum EColor\n    itemNormal = &H80000005\nEnd Enum\nDebug.Print EColor.itemNormal\n");
        AssertDebugLog([new Vb6Value(-2147483643L)]);
    }

    [Fact]
    public async Task AutoIncrementContinuesFromAHexValue()
    {
        await Run("Public Enum EH\n    a = &H80000005\n    b\nEnd Enum\nDebug.Print EH.b\n");
        AssertDebugLog([new Vb6Value(-2147483642L)]);
    }

    [Theory]
    [InlineData("&HFF", 255L)]
    [InlineData("&O17", 15L)]
    [InlineData("-3", -3L)]
    [InlineData("2 ^ 3", 8L)]
    [InlineData("(2 + 3) * 4", 20L)]
    [InlineData("7 Mod 4", 3L)]
    [InlineData("&H0F Or &HF0", 255L)]
    [InlineData("&HFF And &H0F", 15L)]
    public async Task AMemberValueIsAConstantExpression(string expr, long expected)
    {
        await Run($"Public Enum EX\n    m = {expr}\nEnd Enum\nDebug.Print EX.m\n");
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Fact]
    public async Task AMemberMayReferenceAnEarlierMemberOfItsOwnEnum()
    {
        await Run("Public Enum EX\n    xFirst = 5\n    xSecond = xFirst + 1\nEnd Enum\nDebug.Print EX.xSecond\n");
        AssertDebugLog([new Vb6Value(6L)]);
    }

    [Fact]
    public async Task AMemberMayReferenceAnEarlierMemberByItsOwnEnumName()
    {
        // The enum's own name is in scope inside its own body — measured legal.
        await Run("Public Enum ESelf\n    sOne = 1\n    sTwo = ESelf.sOne + 1\nEnd Enum\nDebug.Print ESelf.sTwo\n");
        AssertDebugLog([new Vb6Value(2L)]);
    }

    [Fact]
    public async Task AMemberMayReferenceAnEarlierEnum()
    {
        await Run("Public Enum EBase\n    bTen = 10\nEnd Enum\n" +
                  "Public Enum EDeriv\n    dEleven = EBase.bTen + 1\nEnd Enum\nDebug.Print EDeriv.dEleven\n");
        AssertDebugLog([new Vb6Value(11L)]);
    }

    [Fact]
    public async Task AFlagEnumBuiltFromEarlierMembers()
    {
        // The shape the bitwise operators are actually for.
        await Run("Public Enum EFlags\n    fRead = 1\n    fWrite = 2\n    fBoth = fRead Or fWrite\nEnd Enum\n" +
                  "Debug.Print EFlags.fBoth\n");
        AssertDebugLog([new Vb6Value(3L)]);
    }

    [Fact]
    public async Task AForwardReferenceIsRefused()
    {
        // VB6: "Constant expression required". Measured illegal, so refusing is faithful — and refusing is
        // also the only safe answer, since a member is a value the whole program reads.
        var act = async () => await Run("Public Enum EFwd\n    a = b + 1\n    b = 5\nEnd Enum\nDebug.Print EFwd.a\n");
        (await act.Should().ThrowAsync<VBCompileErrorException>())
            .Which.Message.Should().Contain("Constant expression required");
    }

    [Fact]
    public async Task AMemberIsAConstantAndRefusesAssignment()
    {
        // VB6 refuses this at compile time with "Assignment to constant not permitted". Before, the member
        // was hoisted as an ordinary variable and the assignment SUCCEEDED — so a program could overwrite
        // vbRed and nothing anywhere would say so.
        var act = async () => await Run("Public Enum EPlain\n    pOne\n    pTwo\nEnd Enum\npTwo = 5\n");
        (await act.Should().ThrowAsync<VBCompileErrorException>())
            .Which.Message.Should().Contain("Assignment to constant not permitted");
    }

    [Fact]
    public async Task AMemberIsStillReadableEverywhereItWasBefore()
    {
        // The guard on the constness change: read-only must not mean unreadable.
        await Run("Public Enum EPlain\n    pOne\n    pTwo\n    pThree\nEnd Enum\n" +
                  "Debug.Print pTwo\nDebug.Print EPlain.pTwo\nDim x\nx = pThree + 1\nDebug.Print x\n");
        AssertDebugLog([new Vb6Value(1L), new Vb6Value(1L), new Vb6Value(3L)]);
    }

    [Fact]
    public async Task AnEnumTypedVariableIsAnOpenLong()
    {
        // Measured: TypeName is "Long", not the enum name, and a value no member declares is accepted.
        await Run("Public Enum EPlain\n    pOne\n    pTwo\nEnd Enum\n" +
                  "Dim y As EPlain\ny = 999\nDebug.Print y\nDebug.Print TypeName(y)\n");
        AssertDebugLog([new Vb6Value(999L), new Vb6Value("Long")]);
    }

    [Fact]
    public async Task ImplicitMembersCountFromZero()
    {
        await Run("Public Enum EPlain\n    pOne\n    pTwo\n    pThree\nEnd Enum\n" +
                  "Debug.Print EPlain.pOne\nDebug.Print EPlain.pThree\n");
        AssertDebugLog([new Vb6Value(0L), new Vb6Value(2L)]);
    }

    [Fact]
    public async Task AutoIncrementContinuesFromANegativeValue()
    {
        await Run("Public Enum ENeg\n    nMinus = -3\n    nAfter\nEnd Enum\nDebug.Print ENeg.nAfter\n");
        AssertDebugLog([new Vb6Value(-2L)]);
    }
}
