using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// IIf (#1461) and Choose (#1464), which HexIDE did not have, so a program that called either stopped there. Each transcript
/// is what <c>vb6.exe</c> logged for the same program under en-GB, native and p-code agreeing (the oracle's "IIf and Choose").
/// Two of its answers are not the ones usually given, and are VB6's: both of IIf's parts are evaluated, so a guarded
/// division still divides, and Choose truncates its index rather than rounding it.
/// </summary>
public class IIfChooseTests : BaseVBTestFixture
{
    private static List<Vb6Value> Lines(string transcript) =>
        [.. transcript.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Select(l => new Vb6Value(l))];

    // A value's type and text, and its type alone when it has no text (Null). It clears Err itself, so a row never reads
    // the one before it.
    private const string Helpers = """
        Private Function F(ByVal tag As String, ByVal v As Variant) As Variant
            Debug.Print "  evaluated " & tag
            F = v
        End Function
        Private Function D(ByVal v As Variant) As String
            On Error Resume Next
            Err.Clear
            D = TypeName(v) & " [" & CStr(v) & "]"
            If Err.Number <> 0 Then D = TypeName(v)
            Err.Clear
        End Function

        """;

    [Fact]
    public async Task IIf_evaluates_both_parts_and_returns_one_as_a_Variant()
    {
        await Run(Helpers + """
            Dim x As Variant, e As Long, i As Integer, n As Integer
            On Error Resume Next
            Debug.Print "IIf order:"
            x = IIf(True, F("true part", 1), F("false part", 2))
            Debug.Print "  -> " & D(x)
            Err.Clear: x = Empty: x = IIf(True, 1, 1 / 0)
            e = Err.Number: Err.Clear: Debug.Print "IIf(True, 1, 1 / 0): err " & e & " " & D(x)
            n = 0: Err.Clear: x = Empty: x = IIf(n <> 0, 1 / n, 0)
            e = Err.Number: Err.Clear: Debug.Print "IIf(n <> 0, 1 / n, 0): err " & e & " " & D(x)
            Debug.Print "IIf(True, 5, a): " & D(IIf(True, 5, "a"))
            Debug.Print "IIf(False, 5, a): " & D(IIf(False, 5, "a"))
            Debug.Print "IIf(False, 5, 2.5): " & D(IIf(False, 5, 2.5))
            Debug.Print "IIf(True, 5&, 2): " & D(IIf(True, 5&, 2))
            Debug.Print "IIf(True, Null, 1): " & D(IIf(True, Null, 1))
            Debug.Print "IIf(True, Empty, 1): " & D(IIf(True, Empty, 1))
            Err.Clear: x = Empty: x = IIf(Null, 1, 2)
            e = Err.Number: Err.Clear: Debug.Print "IIf(Null, 1, 2): err " & e & " " & D(x)
            Err.Clear: x = Empty: x = IIf("True", 1, 2)
            e = Err.Number: Err.Clear: Debug.Print "IIf(True as a String, 1, 2): err " & e & " " & D(x)
            Err.Clear: x = Empty: x = IIf("abc", 1, 2)
            e = Err.Number: Err.Clear: Debug.Print "IIf(abc, 1, 2): err " & e & " " & D(x)
            Err.Clear: x = Empty: x = IIf("0", 1, 2)
            e = Err.Number: Err.Clear: Debug.Print "IIf(0 as a String, 1, 2): err " & e & " " & D(x)
            Err.Clear: x = Empty: x = IIf(2, 1, 2)
            e = Err.Number: Err.Clear: Debug.Print "IIf(2, 1, 2): err " & e & " " & D(x)
            Err.Clear: x = Empty: x = IIf(0.4, 1, 2)
            e = Err.Number: Err.Clear: Debug.Print "IIf(0.4, 1, 2): err " & e & " " & D(x)
            Err.Clear: x = Empty: x = IIf(Empty, 1, 2)
            e = Err.Number: Err.Clear: Debug.Print "IIf(Empty, 1, 2): err " & e & " " & D(x)
            Err.Clear: x = Empty: x = IIf(True, 5, 6) * 7000
            e = Err.Number: Err.Clear: Debug.Print "IIf(True, 5, 6) * 7000: err " & e & " " & D(x)
            i = 5: Err.Clear: x = Empty: x = IIf(True, i, 6) * 7000
            e = Err.Number: Err.Clear: Debug.Print "IIf(True, i, 6) * 7000: err " & e & " " & D(x)

            """);

        AssertDebugLog(Lines("""
            IIf order:
              evaluated true part
              evaluated false part
              -> Integer [1]
            IIf(True, 1, 1 / 0): err 11 Empty []
            IIf(n <> 0, 1 / n, 0): err 11 Empty []
            IIf(True, 5, a): Integer [5]
            IIf(False, 5, a): String [a]
            IIf(False, 5, 2.5): Double [2.5]
            IIf(True, 5&, 2): Long [5]
            IIf(True, Null, 1): Null
            IIf(True, Empty, 1): Empty []
            IIf(Null, 1, 2): err 0 Integer [2]
            IIf(True as a String, 1, 2): err 0 Integer [1]
            IIf(abc, 1, 2): err 13 Empty []
            IIf(0 as a String, 1, 2): err 0 Integer [2]
            IIf(2, 1, 2): err 0 Integer [1]
            IIf(0.4, 1, 2): err 0 Integer [1]
            IIf(Empty, 1, 2): err 0 Integer [2]
            IIf(True, 5, 6) * 7000: err 0 Long [35000]
            IIf(True, i, 6) * 7000: err 0 Long [35000]
            """));
    }

    [Fact]
    public async Task Choose_truncates_its_index_and_is_Null_outside_its_choices()
    {
        await Run(Helpers + """
            Dim x As Variant, e As Long
            On Error Resume Next
            Debug.Print "Choose order:"
            x = Choose(2, F("one", "a"), F("two", "b"), F("three", "c"))
            Debug.Print "  -> " & D(x)
            Err.Clear: x = Empty: x = Choose(1, 1, 1 / 0)
            e = Err.Number: Err.Clear: Debug.Print "Choose(1, 1, 1 / 0): err " & e & " " & D(x)
            Debug.Print "Choose(1.5): " & D(Choose(1.5, "a", "b", "c"))
            Debug.Print "Choose(2.5): " & D(Choose(2.5, "a", "b", "c"))
            Debug.Print "Choose(1.4): " & D(Choose(1.4, "a", "b", "c"))
            Debug.Print "Choose(1.6): " & D(Choose(1.6, "a", "b", "c"))
            Debug.Print "Choose(0.5): " & D(Choose(0.5, "a", "b", "c"))
            Debug.Print "Choose(3.5): " & D(Choose(3.5, "a", "b", "c"))
            Debug.Print "Choose(-0.6): " & D(Choose(-0.6, "a", "b", "c"))
            Debug.Print "Choose(0): " & D(Choose(0, "a", "b", "c"))
            Debug.Print "Choose(4): " & D(Choose(4, "a", "b", "c"))
            Debug.Print "Choose(-1): " & D(Choose(-1, "a", "b", "c"))
            Debug.Print "Choose(1), no choices: " & D(Choose(1))
            Err.Clear: x = Empty: x = Choose(Null, "a", "b")
            e = Err.Number: Err.Clear: Debug.Print "Choose(Null): err " & e & " " & D(x)
            Err.Clear: x = Empty: x = Choose("2", "a", "b")
            e = Err.Number: Err.Clear: Debug.Print "Choose(2 as a String): err " & e & " " & D(x)
            Err.Clear: x = Empty: x = Choose("x", "a", "b")
            e = Err.Number: Err.Clear: Debug.Print "Choose(x): err " & e & " " & D(x)
            Err.Clear: x = Empty: x = Choose(True, "a", "b")
            e = Err.Number: Err.Clear: Debug.Print "Choose(True): err " & e & " " & D(x)
            Err.Clear: x = Empty: x = Choose(40000, "a", "b")
            e = Err.Number: Err.Clear: Debug.Print "Choose(40000): err " & e & " " & D(x)
            Err.Clear: x = Empty: x = Choose(1E+20, "a", "b")
            e = Err.Number: Err.Clear: Debug.Print "Choose(1E+20): err " & e & " " & D(x)
            Debug.Print "Choose(2, 5, x): " & D(Choose(2, 5, "x"))
            Err.Clear: x = Empty: x = Choose(1, 5, 6) * 7000
            e = Err.Number: Err.Clear: Debug.Print "Choose(1, 5, 6) * 7000: err " & e & " " & D(x)

            """);

        AssertDebugLog(Lines("""
            Choose order:
              evaluated one
              evaluated two
              evaluated three
              -> String [b]
            Choose(1, 1, 1 / 0): err 11 Empty []
            Choose(1.5): String [a]
            Choose(2.5): String [b]
            Choose(1.4): String [a]
            Choose(1.6): String [a]
            Choose(0.5): Null
            Choose(3.5): String [c]
            Choose(-0.6): Null
            Choose(0): Null
            Choose(4): Null
            Choose(-1): Null
            Choose(1), no choices: Null
            Choose(Null): err 94 Empty []
            Choose(2 as a String): err 0 String [b]
            Choose(x): err 13 Empty []
            Choose(True): err 0 Null
            Choose(40000): err 0 Null
            Choose(1E+20): err 0 Null
            Choose(2, 5, x): String [x]
            Choose(1, 5, 6) * 7000: err 0 Long [35000]
            """));
    }

    [Fact]
    public async Task IIf_and_Choose_return_an_object_as_the_object()
    {
        await RunClasses("""
            Dim k As Class1, o As Object, x As Variant, e As Long
            Set k = New Class1
            Set o = IIf(True, k, Nothing): Debug.Print "IIf(True, k, Nothing): " & TypeName(o)
            Set o = IIf(False, k, Nothing): Debug.Print "IIf(False, k, Nothing): " & TypeName(o)
            Set o = Choose(1, k, Nothing): Debug.Print "Choose(1, k, Nothing): " & TypeName(o)
            Set o = Choose(2, k, Nothing): Debug.Print "Choose(2, k, Nothing): " & TypeName(o)
            On Error Resume Next
            Err.Clear: x = IIf(k, 1, 2)
            e = Err.Number: Err.Clear: Debug.Print "IIf(k, 1, 2): err " & e

            """, ("Class1", """
            Public Tag As String

            """));

        AssertDebugLog(Lines("""
            IIf(True, k, Nothing): Class1
            IIf(False, k, Nothing): Nothing
            Choose(1, k, Nothing): Class1
            Choose(2, k, Nothing): Nothing
            IIf(k, 1, 2): err 438
            """));
    }

    [Theory]
    [InlineData("Dim x\nx = IIf(True, 1)\n", "*Argument not optional*")]
    [InlineData("Dim x\nx = IIf(True, 1, 2, 3)\n", "*Wrong number of arguments or invalid property assignment*")]
    [InlineData("Dim x\nx = Choose()\n", "*Argument not optional*")]
    public async Task What_VB6_refuses_to_compile_is_refused(string code, string message)
    {
        var run = () => Run(code);

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage(message);
    }
}
