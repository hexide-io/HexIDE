using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// <c>Erl</c> (#2528): the line number at or above where the latest error was raised, in the procedure that trapped it and
/// in source order, a Long, and 0 again wherever VB6 clears <c>Err</c> itself. The procedures are the probe's, and every line was logged by vb6.exe, native and
/// p-code alike (the issue has both probes). E13's `Next` is unnumbered here, where the probe's was `30  Next`: a
/// number on a closing line doesn't parse yet (#2529).
/// </summary>
public class ErlTests : BaseVBTestFixture
{
    private static List<Vb6Value> Lines(params string[] lines) => lines.Select(l => new Vb6Value(l)).ToList();

    [Fact]
    public async Task Erl_is_the_last_line_number_passed_in_the_procedure_that_traps_the_error()
    {
        await Run("""
            Dim x As Integer
            Sub E1()
                On Error GoTo h
            10  x = 1
            20  x = 1 / 0
            30  x = 2
                Exit Sub
            h:
                Debug.Print "E1: Erl " & Erl & " " & TypeName(Erl) & " err " & Err.Number
            End Sub
            Sub E2()
                On Error GoTo h
            10  x = 1
            20  x = 2
                x = 1 / 0
                Exit Sub
            h:
                Debug.Print "E2: Erl " & Erl
            End Sub
            Sub E3()
                On Error GoTo h
                x = 1 / 0
                Exit Sub
            h:
                Debug.Print "E3: Erl " & Erl
            End Sub
            Sub Callee()
                x = 1 / 0
            End Sub
            Sub NumberedCallee()
            5   x = 0
            6   x = 1 / 0
            End Sub
            Sub E4()
                On Error GoTo h
            30  x = 1
            40  Callee
            50  x = 2
                Exit Sub
            h:
                Debug.Print "E4: Erl " & Erl
            End Sub
            Sub E5()
                On Error GoTo h
            30  x = 1
            40  NumberedCallee
            50  x = 2
                Exit Sub
            h:
                Debug.Print "E5: Erl " & Erl
            End Sub
            Sub E7()
                On Error Resume Next
            100 x = 1 / 0
                Debug.Print "E7 on 100: Erl " & Erl & " err " & Err.Number
            200 x = 3
                Debug.Print "E7 after 200: Erl " & Erl
                x = 1 / 0
                Debug.Print "E7 again: Erl " & Erl
            End Sub
            Sub E8()
                On Error GoTo h
            65529 x = 1 / 0
                Exit Sub
            h:
                Debug.Print "E8: Erl " & Erl & " " & TypeName(Erl)
            End Sub
            Sub E9()
                On Error GoTo h
            70000 x = 1 / 0
                Exit Sub
            h:
                Debug.Print "E9: Erl " & Erl
            End Sub
            Sub E10()
                On Error GoTo h
            10  x = 1
            lbl: x = 1 / 0
                Exit Sub
            h:
                Debug.Print "E10: Erl " & Erl
            End Sub
            Sub E11()
                On Error GoTo h
            10  Err.Raise 1234
                Exit Sub
            h:
                Debug.Print "E11: Erl " & Erl & " err " & Err.Number
            End Sub
            Sub E12()
                On Error GoTo h
            10  x = 1
            20  If x = 1 Then x = 1 / 0
                Exit Sub
            h:
                Debug.Print "E12: Erl " & Erl
            End Sub
            Sub E13()
                Dim i As Integer
                On Error GoTo h
            10  For i = 1 To 3
            20      If i = 2 Then x = 1 / 0
                Next
                Exit Sub
            h:
                Debug.Print "E13: Erl " & Erl & " i " & i
            End Sub
            Debug.Print "no error yet: Erl " & Erl & " " & TypeName(Erl)
            E1
            E2
            E3
            E4
            E5
            E7
            E8
            E9
            E10
            E11
            E12
            E13
            """);
        AssertDebugLog(Lines(
            "no error yet: Erl 0 Long",
            "E1: Erl 20 Long err 11",
            "E2: Erl 20",
            "E3: Erl 0",
            // An error in a callee is the caller's line, the call's, whether or not the callee has numbers of its own.
            "E4: Erl 40",
            "E5: Erl 40",
            "E7 on 100: Erl 100 err 11",
            "E7 after 200: Erl 100",
            "E7 again: Erl 200",
            "E8: Erl 65529 Long",
            // Wrapped at 65536.
            "E9: Erl 4464",
            "E10: Erl 10",
            "E11: Erl 10 err 1234",
            "E12: Erl 20",
            "E13: Erl 20 i 2"));
    }

    [Fact]
    public async Task Erl_is_cleared_where_VB6_clears_Err_and_not_by_Err_Clear()
    {
        await Run("""
            Dim x As Integer
            Sub E6()
                On Error GoTo h
            10  x = 1 / 0
            20  x = 2
                Exit Sub
            h:
                Debug.Print "E6 before Clear: Erl " & Erl
                Err.Clear
                Debug.Print "E6 after Err.Clear: Erl " & Erl
                Resume Next
            End Sub
            Sub E14()
                On Error GoTo h
            10  x = 1 / 0
                Exit Sub
            h:
                Debug.Print "E14 handler: Erl " & Erl
                On Error GoTo h2
                Debug.Print "E14 after On Error GoTo h2: Erl " & Erl & " err " & Err.Number
                Exit Sub
            h2:
            End Sub
            Sub E15()
                On Error GoTo h
            10  x = 1 / 0
            20  Debug.Print "E15 after Resume Next: Erl " & Erl
                Exit Sub
            h:
                Resume Next
            End Sub
            Sub E16()
                On Error GoTo h
            10  x = 1 / 0
                Exit Sub
            h:
                Debug.Print "E16 handler: Erl " & Erl
                Resume there
            there:
                Debug.Print "E16 after Resume there: Erl " & Erl
            End Sub
            Function F17() As Long
                On Error Resume Next
            50  x = 1 / 0
                F17 = Erl
            End Function
            Sub G1()
                On Error GoTo h
            10  x = 1 / 0
                Exit Sub
            h:
                Exit Sub
            End Sub
            Sub G2()
                On Error GoTo h
            20  x = 1 / 0
                Exit Sub
            h:
            End Sub
            Sub G3()
                On Error Resume Next
            30  x = 1 / 0
            End Sub
            Sub G4()
                On Error Resume Next
            End Sub
            Sub G5()
                x = 1
            End Sub
            Sub G6()
                On Error GoTo 0
            End Sub
            E6
            E14
            E15
            E16
            Debug.Print "F17 " & F17() & ", then Erl " & Erl
            On Error Resume Next
            G1
            Debug.Print "after G1 left its handler by Exit Sub: Erl " & Erl & " err " & Err.Number
            G2
            Debug.Print "after G2 left its handler by End Sub: Erl " & Erl & " err " & Err.Number
            G3
            Debug.Print "after G3: Erl " & Erl & " err " & Err.Number
            G5
            Debug.Print "after G5: Erl " & Erl & " err " & Err.Number
            G4
            Debug.Print "after G4: Erl " & Erl & " err " & Err.Number
            70 x = 1 / 0
            Debug.Print "on 70: Erl " & Erl
            G6
            Debug.Print "after G6: Erl " & Erl & " err " & Err.Number
            80 x = 1 / 0
            Err.Raise 5
            Debug.Print "Err.Raise after 80: Erl " & Erl
            """);
        AssertDebugLog(Lines(
            "E6 before Clear: Erl 10",
            "E6 after Err.Clear: Erl 10",
            "E14 handler: Erl 10",
            "E14 after On Error GoTo h2: Erl 0 err 0",
            "E15 after Resume Next: Erl 0",
            "E16 handler: Erl 10",
            "E16 after Resume there: Erl 0",
            // Leaving a procedure under On Error Resume Next keeps it, as it keeps Err.
            "F17 50, then Erl 50",
            "after G1 left its handler by Exit Sub: Erl 0 err 0",
            "after G2 left its handler by End Sub: Erl 0 err 0",
            "after G3: Erl 30 err 11",
            "after G5: Erl 30 err 11",
            // A callee's On Error statement clears it with Err: it is the program's, not the procedure's.
            "after G4: Erl 0 err 0",
            "on 70: Erl 70",
            "after G6: Erl 0 err 0",
            "Err.Raise after 80: Erl 80"));
    }

    [Fact]
    public async Task Erl_is_the_line_number_at_or_above_where_the_error_was_raised_in_source_order()
    {
        // A loop's second pass reads the For or Do line's number, not that of the body's last line, which ran last: the
        // number above the faulting line in the source. Each procedure is a probe's Sub Main, renamed.
        await Run("""
            Sub PForBodyTop()
                Dim i As Integer, s As String, b As Integer, x As Integer, v As Variant, c As New Collection
                On Error GoTo h
            10 For i = 1 To 2
                If i = 2 Then x = 1 / 0
            20 s = s & i
            Next
                Debug.Print "p-for-body-top: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "p-for-body-top: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PDoBodyTop()
                Dim i As Integer, s As String, b As Integer, x As Integer, v As Variant, c As New Collection
                On Error GoTo h
            10 Do
                If i = 1 Then x = 1 / 0
            20 i = i + 1
            Loop While i < 3
                Debug.Print "p-do-body-top: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "p-do-body-top: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PDowhileBodyTop()
                Dim i As Integer, s As String, b As Integer, x As Integer, v As Variant, c As New Collection
                On Error GoTo h
            10 Do While i < 3
                If i = 1 Then x = 1 / 0
            20 i = i + 1
            Loop
                Debug.Print "p-dowhile-body-top: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "p-dowhile-body-top: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PForeachBodyTop()
                Dim i As Integer, s As String, b As Integer, x As Integer, v As Variant, c As New Collection
                On Error GoTo h
            c.Add 1
            c.Add 2
            10 For Each v In c
                If v = 2 Then x = 1 / 0
            20 s = s & v
            Next
                Debug.Print "p-foreach-body-top: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "p-foreach-body-top: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PDowhileLoopUnnumbered()
                Dim i As Integer, s As String, b As Integer, x As Integer, v As Variant, c As New Collection
                On Error GoTo h
            10 Do While 1 / (2 - i) > -5
            20 i = i + 1
            Loop
                Debug.Print "p-dowhile-loop-unnumbered: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "p-dowhile-loop-unnumbered: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PWhileWendUnnumbered()
                Dim i As Integer, s As String, b As Integer, x As Integer, v As Variant, c As New Collection
                On Error GoTo h
            10 While 1 / (2 - i) > -5
            20 i = i + 1
            Wend
                Debug.Print "p-while-wend-unnumbered: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "p-while-wend-unnumbered: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PForNextUnnumbered()
                Dim i As Integer, s As String, b As Integer, x As Integer, v As Variant, c As New Collection
                On Error GoTo h
            10 For b = 32766 To 32767
            20 s = s & "x"
            Next
                Debug.Print "p-for-next-unnumbered: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "p-for-next-unnumbered: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            PForBodyTop
            PDoBodyTop
            PDowhileBodyTop
            PForeachBodyTop
            PDowhileLoopUnnumbered
            PWhileWendUnnumbered
            PForNextUnnumbered
            """);
        AssertDebugLog(Lines(
            "p-for-body-top: err 11 erl 10 [1] i 2",
            "p-do-body-top: err 11 erl 10 [] i 1",
            "p-dowhile-body-top: err 11 erl 10 [] i 1",
            "p-foreach-body-top: err 11 erl 10 [1] i 0",
            "p-dowhile-loop-unnumbered: err 11 erl 10 [] i 2",
            "p-while-wend-unnumbered: err 11 erl 10 [] i 2",
            "p-for-next-unnumbered: err 6 erl 20 [xx] i 0"));
    }
}
