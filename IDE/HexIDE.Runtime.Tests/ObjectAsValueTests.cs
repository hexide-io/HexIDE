using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A class instance used where a value is wanted (#174). Measured on vb6.exe, with <c>Widget</c>'s <c>Value</c> its
/// default member returning 42 and <c>Plain</c> a class with none: every use below sees 42 for a Widget and raises 438
/// for a Plain. HexIDE does not call a class's default member yet, so a Widget is refused untrappably, where it raised a
/// trappable 438 or 13, or read 0, the CLR type name, or no match at all. A Plain raises VB6's 438, where several of
/// these raised 13 or gave the type name (#436). The probe is in docs/vb6-fidelity-oracle.md, "An object used as a
/// value".
/// </summary>
public class ObjectAsValueTests : BaseVBTestFixture
{
    private const string Widget = "Public Function Value() As Long\nAttribute Value.VB_UserMemId = 0\n    Value = 42\nEnd Function\n";
    private const string Plain = "Public Tag As Long\n";

    private const string Helpers =
        "Function ByValLong(ByVal n As Long)\n    ByValLong = n\nEnd Function\n"
        + "Function ByValString(ByVal s As String)\n    ByValString = s\nEnd Function\n"
        + "Function ByValVariant(ByVal n As Variant)\n    ByValVariant = TypeName(n)\nEnd Function\n";

    private Task RunWith(string cls, string use) => RunClasses(
        $"Dim o As {cls}\nSet o = New {cls}\nDim c\nOn Error Resume Next\n{use}\nDebug.Print Err.Number\nDebug.Print IsEmpty(c)\n"
        + Helpers,
        ("Widget", Widget), ("Plain", Plain));

    public static TheoryData<string> MeasuredUses => new()
    {
        "Dim x As Long\nx = o",
        "Dim s As String\ns = o",
        "Dim d As Double\nd = o",
        "Dim b As Boolean\nb = o",
        "Dim vv As Variant\nvv = o",
        "c = o + 1",
        "c = o * 2",
        "c = -o",
        "c = o \\ 5",
        "c = (o = 42)",
        "c = (o > 41)",
        "c = \"v=\" & o",
        "c = CStr(o)",
        "c = CDbl(o)",
        "c = CBool(o)",
        "c = Val(o)",
        "c = ByValLong(o)",
        "c = ByValString(o)",
        "Select Case o\nCase 42\nc = 1\nEnd Select",
        "Dim i As Long\nFor i = 40 To o\nc = i\nNext",
        "Debug.Print o",
    };

    [Theory]
    [MemberData(nameof(MeasuredUses))]
    public async Task A_class_with_a_default_member_used_as_a_value_is_refused_until_default_members_exist(string use)
    {
        // VB6 gives 42 for every one. A trappable error would let On Error Resume Next carry on without it.
        var run = () => RunWith("Widget", use);

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*Widget*#174*");
    }

    [Theory]
    [MemberData(nameof(MeasuredUses))]
    public async Task A_class_with_no_default_member_used_as_a_value_raises_438(string use)
    {
        await RunWith("Plain", use);

        AssertDebugLog([new Vb6Value(438L), new Vb6Value(true)]);
    }

    [Theory]
    [InlineData("If o Then c = 1")]
    [InlineData("If o Then\nc = 1\nEnd If")]
    [InlineData("While o\nc = 1\nWend")]
    [InlineData("Do While o\nc = 1\nLoop")]
    [InlineData("c = Not o")]
    [InlineData("c = CDate(o)")]
    [InlineData("Select Case 42\nCase o\nc = 1\nEnd Select")]   // a Case value is stored into the selector's type
    [InlineData("Dim a(1) As Long\na(0) = o")]
    public async Task A_condition_or_a_conversion_of_a_class_with_a_default_member_is_refused(string use)
    {
        // The same rule: VB6 reads the default member (measured for If; the others follow it).
        var run = () => RunWith("Widget", use);

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*#174*");
    }

    // Through a Variant holding the object, so Len compiles. Measured: with a default member, Left, InStr, Chr, Len,
    // UCase, CDate and a ByVal Double argument see 42, and Format, Abs and Not raise 450; with none, every one is 438.
    public static TheoryData<string> MeasuredThroughAVariant => new()
    {
        "c = Left(v, 1)",
        "c = InStr(v, \"2\")",
        "c = Chr(v)",
        "c = Format(v)",
        "c = Format(v, \"0.0\")",
        "c = Len(v)",
        "c = UCase(v)",
        "c = Abs(v)",
        "c = ByValDouble(v)",
        "c = Not v",
        "c = CDate(v)",
    };

    private Task RunThroughAVariant(string cls, string use) => RunClasses(
        $"Dim v\nSet v = New {cls}\nDim c\nOn Error Resume Next\n{use}\nDebug.Print Err.Number\nDebug.Print IsEmpty(c)\n"
        + "Function ByValDouble(ByVal d As Double)\n    ByValDouble = d\nEnd Function\n",
        ("Widget", Widget), ("Plain", Plain));

    [Theory]
    [MemberData(nameof(MeasuredThroughAVariant))]
    public async Task A_built_in_given_a_class_with_a_default_member_refuses(string use)
    {
        var run = () => RunThroughAVariant("Widget", use);

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*#174*");
    }

    [Theory]
    [MemberData(nameof(MeasuredThroughAVariant))]
    public async Task A_built_in_given_a_class_with_no_default_member_raises_438(string use)
    {
        await RunThroughAVariant("Plain", use);

        AssertDebugLog([new Vb6Value(438L), new Vb6Value(true)]);
    }

    [Theory]
    [InlineData("Widget")]
    [InlineData("Plain")]
    public async Task A_ByVal_Variant_argument_takes_the_object_itself(string cls)
    {
        // Measured: TypeName of the parameter is the class's name.
        await RunWith(cls, "c = ByValVariant(o)\nDebug.Print c");

        AssertDebugLog([new Vb6Value(cls), new Vb6Value(0L), new Vb6Value(false)]);
    }

    [Theory]
    [InlineData("Widget")]
    [InlineData("Plain")]
    public async Task A_built_in_that_asks_what_a_value_is_still_takes_the_object(string cls)
    {
        await RunClasses(
            $"Dim o As {cls}
Set o = New {cls}
Debug.Print TypeName(o)
Debug.Print IsObject(o)
Debug.Print IsEmpty(o)
",
            ("Widget", Widget), ("Plain", Plain));

        AssertDebugLog([new Vb6Value(cls), new Vb6Value(true), new Vb6Value(false)]);
    }

    [Fact]
    public async Task Set_Is_and_TypeOf_still_take_the_object()
    {
        await RunClasses(
            "Dim o As Widget, p As Widget\nSet o = New Widget\nSet p = o\nDebug.Print p Is o\nDebug.Print TypeOf o Is Widget\n",
            ("Widget", Widget));

        AssertDebugLog([new Vb6Value(true), new Vb6Value(true)]);
    }
}
