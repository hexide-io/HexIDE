using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A name never declared, passed to a ByRef parameter (#1505). VB6 declares the variable where it is passed, as a Let would,
/// and the procedure writes into it. HexIDE passed a copy, so the write was lost and the variable stayed Empty, with nothing
/// to say why. Each transcript is what <c>vb6.exe</c> logged for the same program under en-GB, native and p-code agreeing
/// (the oracle's "An undeclared variable passed ByRef").
/// </summary>
public class ByRefUndeclaredTests : BaseVBTestFixture
{
    private static List<Vb6Value> Lines(string transcript) =>
        [.. transcript.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Select(l => new Vb6Value(l))];

    // A class whose method writes its ByRef parameter.
    private const string K1 = """
        Public Sub Bump(x)
            x = x + 1
        End Sub

        """;

    [Fact]
    public async Task A_procedure_writes_into_an_undeclared_variable_passed_to_it()
    {
        await Run("""
            Private Sub Bump(x)
                x = x + 1
            End Sub
            Private Sub Fill(ByRef s)
                s = "filled"
            End Sub
            Private Function BumpF(x)
                x = x + 1
                BumpF = x * 10
            End Function
            Private Sub Keep(ByVal x)
                x = 9
            End Sub
            Private Function Seven()
                Seven = 7
            End Function
            Private Sub Show()
                Bump a12
                Bump a12
                Debug.Print "a12 " & TypeName(a12) & " " & a12
                Fill b12
                Debug.Print "b12 " & TypeName(b12) & " " & b12
                Fill s:=c12
                Debug.Print "c12, named " & TypeName(c12) & " " & c12
                r = BumpF(d12)
                Debug.Print "d12, to a Function " & TypeName(d12) & " " & d12 & ", r " & r
                Bump (f12)
                Debug.Print "f12, parenthesised " & TypeName(f12)
                Keep h12
                Debug.Print "h12, to a ByVal parameter " & TypeName(h12)
                Bump Seven
                Debug.Print "Seven, a Function passed " & Seven
            End Sub

            Show

            """);

        AssertDebugLog(Lines("""
            a12 Integer 2
            b12 String filled
            c12, named String filled
            d12, to a Function Integer 1, r 10
            f12, parenthesised Empty
            h12, to a ByVal parameter Empty
            Seven, a Function passed 7
            """));
    }

    [Fact]
    public async Task A_DefTyped_variable_and_a_class_method_take_the_write_too()
    {
        await RunClasses("""
            DefInt I
            Private Sub BumpI(x As Integer)
                x = x + 1
            End Sub
            Private Sub Show()
                BumpI i12
                BumpI i12
                Debug.Print "i12 " & TypeName(i12) & " " & i12
                Dim k As K1
                Set k = New K1
                k.Bump e12
                k.Bump e12
                Debug.Print "e12, to a method " & TypeName(e12) & " " & e12
                Set k = Nothing
            End Sub

            Show

            """, ("K1", K1));

        AssertDebugLog(Lines("""
            i12 Integer 2
            e12, to a method Integer 2
            """));
    }
}
