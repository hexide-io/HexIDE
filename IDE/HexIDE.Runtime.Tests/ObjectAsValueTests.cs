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
        "Dim i As Long\nFor i = 40 To o\nNext",   // no assignment in the body: #839 decides whether it runs
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
            $"Dim o As {cls}\nSet o = New {cls}\nDebug.Print TypeName(o)\nDebug.Print IsObject(o)\n",
            ("Widget", Widget), ("Plain", Plain));

        AssertDebugLog([new Vb6Value(cls), new Vb6Value(true)]);
    }

    [Fact]
    public async Task Set_Is_and_TypeOf_still_take_the_object()
    {
        await RunClasses(
            "Dim o As Widget, p As Widget\nSet o = New Widget\nSet p = o\nDebug.Print p Is o\nDebug.Print TypeOf o Is Widget\n",
            ("Widget", Widget));

        AssertDebugLog([new Vb6Value(true), new Vb6Value(true)]);
    }

    // ---- From #840's review: the routes that still read an object as a value, each measured on vb6.exe, native and in
    // ---- the IDE (docs/vb6-fidelity-oracle.md, "An object used as a value").

    private Task RunPrinting(string cls, string use) => RunClasses(
        $"Dim o As {cls}\nSet o = New {cls}\nDim c\nOn Error Resume Next\n{use}\nDebug.Print Err.Number\n" + Helpers,
        ("Widget", Widget), ("Plain", Plain));

    // Every one of these reads the default member in VB6: 42 for a Widget, 438 for a Plain.
    public static TheoryData<string> FoundInReview => new()
    {
        "If False Then\nc = 0\nElseIf o Then\nc = 1\nEnd If",
        "Select Case 42\nCase Is = o\nc = 1\nEnd Select",
        "Select Case 42\nCase 40 To o\nc = 1\nEnd Select",
        "Select Case 42\nCase o To 50\nc = 1\nEnd Select",
        "Select Case Empty\nCase o\nc = 1\nEnd Select",
        "Select Case Null\nCase o\nc = 1\nEnd Select",
        "Dim i As Long\nFor i = o To 50\nNext",
        "Dim i As Long\nFor i = 1 To 3 Step o\nNext",
        "Do\nc = 1\nLoop While o",
        "Do\nc = 1\nLoop Until o",
        "Randomize o",
        "Err.Raise o",
        "Error o",
        "ChDir o",
        "ChDrive o",
        "Dim s As String\ns = \"abcdef\"\nMid(s, o, 1) = \"X\"",
        "Dim s As String\ns = \"abcdef\"\nMid(s, 2, o) = \"X\"",
        "Dim m(1 To 3, 5 To 9)\nc = UBound(m, o)",
        "Dim m(1 To 3, 5 To 9)\nc = LBound(m, o)",
        "c = Split(\"a,b,c\", \",\", o)",
        "c = Split(o)",
        "Dim a(1) As String\nc = Join(a, o)",
        "Dim a(1) As String\nc = Filter(a, \"a\", o)",
        "c = MsgBox(o)",
    };

    [Theory]
    [MemberData(nameof(FoundInReview))]
    public async Task A_class_with_a_default_member_is_refused_here_too(string use)
    {
        var run = () => RunPrinting("Widget", use);

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*Widget*#174*");
    }

    [Theory]
    [MemberData(nameof(FoundInReview))]
    public async Task A_class_with_no_default_member_raises_438_here_too(string use)
    {
        // Only the error is asserted: where a Case test raises, #839 decides what runs next.
        await RunPrinting("Plain", use);

        AssertDebugLog([new Vb6Value(438L)]);
    }

    [Theory]
    [InlineData("c = Frobnicate(o)")]                 // neither a built-in nor a procedure
    [InlineData("c = CallByName(o, \"Tag\", 2)")]    // a built-in HexIDE does not have
    public async Task A_name_that_is_no_built_in_is_still_reported_when_given_an_object(string use)
    {
        // The refusal used to run before the lookup, so each was a trappable 438 that Resume Next stepped over.
        var run = () => RunPrinting("Plain", use);

        await run.Should().ThrowAsync<VBCompileErrorException>();
    }

    [Theory]
    [InlineData("VarType")]
    [InlineData("IsNumeric")]
    [InlineData("IsEmpty")]
    [InlineData("IsNull")]
    [InlineData("IsDate")]
    [InlineData("IsError")]
    [InlineData("IsArray")]
    public async Task VarType_and_the_Is_functions_read_a_default_member(string function)
    {
        // Measured: VarType(w) is 3, IsNumeric(w) True, and IsEmpty of a class whose member returns Empty is True.
        var run = () => RunPrinting("Widget", $"c = {function}(o)");

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*#174*");
    }

    [Theory]
    [InlineData("VarType", "9")]
    [InlineData("IsNumeric", "False")]
    [InlineData("IsEmpty", "False")]
    [InlineData("IsNull", "False")]
    [InlineData("IsDate", "False")]
    [InlineData("IsError", "False")]
    [InlineData("IsArray", "False")]
    public async Task VarType_and_the_Is_functions_answer_about_an_object_with_no_default_member(string function, string expected)
    {
        await RunPrinting("Plain", $"Debug.Print CStr({function}(o))");

        AssertDebugLog([new Vb6Value(expected), new Vb6Value(0L)]);
    }

    [Fact]
    public async Task Err_used_as_a_value_is_its_Number()
    {
        await Run("""
            Dim x As Long
            On Error Resume Next
            Err.Raise 11
            Debug.Print "v=" & Err
            x = Err
            Debug.Print CStr(x)
            Debug.Print CStr(Err + 1)
            Debug.Print CStr(Err)
            Debug.Print CStr(VarType(Err))
            Debug.Print Err
            Err.Clear
            Debug.Print "v=" & Err
            """);

        // `If Err Then` is measured too, and needs a condition that is not a Boolean, which is #839's.
        AssertDebugLog([new Vb6Value("v=11"), new Vb6Value("11"), new Vb6Value("12"), new Vb6Value("11"), new Vb6Value("3"),
            new Vb6Value(11L), new Vb6Value("v=0")]);
    }

    [Fact]
    public async Task App_used_as_a_value_raises_438()
    {
        // Measured for App, Screen and Clipboard: none has a default member.
        await Run("Dim c\nOn Error Resume Next\nc = \"v=\" & App\nDebug.Print Err.Number\nDebug.Print IsEmpty(c)\n");

        AssertDebugLog([new Vb6Value(438L), new Vb6Value(true)]);
    }

    [Fact]
    public async Task Debug_used_as_a_value_is_the_compile_error_VB6_gives()
    {
        var run = () => Run("Dim c\nOn Error Resume Next\nc = \"v=\" & Debug\n");

        await run.Should().ThrowAsync<VBCompileErrorException>();
    }
}
