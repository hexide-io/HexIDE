using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A line number or label on the line that closes a block, or opens its next part (#2529): `30 Next`, `Skip: Next`,
/// `40 Case Else`, `90 End Sub`. Each procedure is a vb6.exe probe's Sub Main, renamed, and every expected line is what
/// that probe logged, native and p-code alike (the issue has the tables). These lines did not parse, so a numbered module
/// did not load, and `Skip: Next` read Skip as a call. A head on End Sub was the last to load (#2561).
/// </summary>
public class ClosingLineTests : BaseVBTestFixture
{
    private static List<Vb6Value> Lines(params string[] lines) => lines.Select(l => new Vb6Value(l)).ToList();

    [Fact]
    public async Task A_line_number_on_a_closing_or_middle_line_parses_and_runs()
    {
        // Each runs as it does unnumbered: the loops count to 3, and each If takes its arm.
        await Run("""
            Sub PLnNext()
                Dim i As Integer, s As String, v As Variant, c As New Collection
                c.Add 7
            10 For i = 1 To 2
            20 s = s & i
            30 Next
                Debug.Print "next: ok [" & s & "] i " & i
            End Sub
            Sub PLnNexti()
                Dim i As Integer, s As String, v As Variant, c As New Collection
                c.Add 7
            10 For i = 1 To 2
            20 s = s & i
            30 Next i
                Debug.Print "nexti: ok [" & s & "] i " & i
            End Sub
            Sub PLnForeach()
                Dim i As Integer, s As String, v As Variant, c As New Collection
                c.Add 7
            10 For Each v In c
            20 s = s & v
            30 Next
                Debug.Print "foreach: ok [" & s & "] i " & i
            End Sub
            Sub PLnEndif()
                Dim i As Integer, s As String, v As Variant, c As New Collection
                c.Add 7
            10 If i = 0 Then
            20 s = "a"
            30 End If
                Debug.Print "endif: ok [" & s & "] i " & i
            End Sub
            Sub PLnElse()
                Dim i As Integer, s As String, v As Variant, c As New Collection
                c.Add 7
            10 If i = 1 Then
            20 s = "a"
            30 Else
            40 s = "b"
            50 End If
                Debug.Print "else: ok [" & s & "] i " & i
            End Sub
            Sub PLnElseif()
                Dim i As Integer, s As String, v As Variant, c As New Collection
                c.Add 7
            10 If i = 1 Then
            20 s = "a"
            30 ElseIf i = 0 Then
            40 s = "b"
            50 End If
                Debug.Print "elseif: ok [" & s & "] i " & i
            End Sub
            Sub PLnLoop()
                Dim i As Integer, s As String, v As Variant, c As New Collection
                c.Add 7
            10 Do
            20 i = i + 1
            30 Loop While i < 3
                Debug.Print "loop: ok [" & s & "] i " & i
            End Sub
            Sub PLnLoopbare()
                Dim i As Integer, s As String, v As Variant, c As New Collection
                c.Add 7
            10 Do While i < 3
            20 i = i + 1
            30 Loop
                Debug.Print "loopbare: ok [" & s & "] i " & i
            End Sub
            Sub PLnWend()
                Dim i As Integer, s As String, v As Variant, c As New Collection
                c.Add 7
            10 While i < 3
            20 i = i + 1
            30 Wend
                Debug.Print "wend: ok [" & s & "] i " & i
            End Sub
            Sub PLnEndwith()
                Dim i As Integer, s As String, v As Variant, c As New Collection
                c.Add 7
            10 With c
            20 .Add 1
            30 End With
                Debug.Print "endwith: ok [" & s & "] i " & i
            End Sub
            Sub PLnEndsub()
                Dim i As Integer, s As String, v As Variant, c As New Collection
                c.Add 7
            10 s = "x"
                Debug.Print "endsub: ok [" & s & "] i " & i
            90 End Sub
            Sub PLnCase2()
                Dim i As Integer, s As String
            10 Select Case i
                Case 0
            30 s = "zero"
            40 Case Else
            50 s = "other"
            60 End Select
                Debug.Print "case2: ok [" & s & "] i " & i
            End Sub
            Sub PLnCase3()
                Dim i As Integer, s As String
            10 Select Case i
                Case 1
            30 s = "one"
            40 Case 0
            50 s = "zero"
            60 End Select
                Debug.Print "case3: ok [" & s & "] i " & i
            End Sub
            PLnNext
            PLnNexti
            PLnForeach
            PLnEndif
            PLnElse
            PLnElseif
            PLnLoop
            PLnLoopbare
            PLnWend
            PLnEndwith
            PLnEndsub
            PLnCase2
            PLnCase3
            """);
        AssertDebugLog(Lines(
            "next: ok [12] i 3",
            "nexti: ok [12] i 3",
            "foreach: ok [7] i 0",
            "endif: ok [a] i 0",
            "else: ok [b] i 0",
            "elseif: ok [b] i 0",
            "loop: ok [] i 3",
            "loopbare: ok [] i 3",
            "wend: ok [] i 3",
            "endwith: ok [] i 0",
            "endsub: ok [x] i 0",
            "case2: ok [zero] i 0",
            "case3: ok [zero] i 0"));
    }

    [Fact]
    public async Task A_GoTo_to_a_numbered_closing_line_ends_the_block_it_is_in()
    {
        // From inside, a loop goes round and an If, Select or With is done; from before, Loop and Wend test, End If goes past.
        await Run("""
            Sub PGNextInside()
                Dim i As Integer, s As String, b As Integer, c As New Collection
                On Error GoTo h
            10 For i = 1 To 3
            20 If i = 2 Then GoTo 30
            25 s = s & i
            30 Next
                Debug.Print "g-next-inside: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "g-next-inside: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PGEndifInside()
                Dim i As Integer, s As String, b As Integer, c As New Collection
                On Error GoTo h
            10 If i = 0 Then
            20 GoTo 30
            25 s = "skipped"
            30 End If
            35 s = s & "after"
                Debug.Print "g-endif-inside: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "g-endif-inside: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PGEndifOutside()
                Dim i As Integer, s As String, b As Integer, c As New Collection
                On Error GoTo h
            GoTo 30
            10 If i = 0 Then
            25 s = "inside"
            30 End If
            35 s = s & "after"
                Debug.Print "g-endif-outside: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "g-endif-outside: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PGElseOutside()
                Dim i As Integer, s As String, b As Integer, c As New Collection
                On Error GoTo h
            i = 1
            GoTo 30
            10 If i = 1 Then
            20 s = "then"
            30 Else
            40 s = "else"
            50 End If
                Debug.Print "g-else-outside: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "g-else-outside: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PGElseFromThen()
                Dim i As Integer, s As String, b As Integer, c As New Collection
                On Error GoTo h
            i = 1
            10 If i = 1 Then
            20 GoTo 30
            25 s = "then"
            30 Else
            40 s = "else"
            50 End If
                Debug.Print "g-else-from-then: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "g-else-from-then: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PGElseifFromThen()
                Dim i As Integer, s As String, b As Integer, c As New Collection
                On Error GoTo h
            i = 1
            10 If i = 1 Then
            20 GoTo 30
            25 s = "then"
            30 ElseIf i = 1 Then
            40 s = "elseif"
            50 End If
                Debug.Print "g-elseif-from-then: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "g-elseif-from-then: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PGLoopInside()
                Dim i As Integer, s As String, b As Integer, c As New Collection
                On Error GoTo h
            10 Do
            20 i = i + 1
            25 If i < 3 Then GoTo 30
            27 s = s & i
            30 Loop While i < 5
                Debug.Print "g-loop-inside: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "g-loop-inside: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PGLoopOutside()
                Dim i As Integer, s As String, b As Integer, c As New Collection
                On Error GoTo h
            GoTo 30
            10 Do
            20 i = i + 1
            30 Loop While i < 3
                Debug.Print "g-loop-outside: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "g-loop-outside: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PGLoopbareInside()
                Dim i As Integer, s As String, b As Integer, c As New Collection
                On Error GoTo h
            10 Do While i < 4
            20 i = i + 1
            25 If i = 2 Then GoTo 30
            27 s = s & i
            30 Loop
                Debug.Print "g-loopbare-inside: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "g-loopbare-inside: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PGWendInside()
                Dim i As Integer, s As String, b As Integer, c As New Collection
                On Error GoTo h
            10 While i < 3
            20 i = i + 1
            25 If i = 1 Then GoTo 30
            27 s = s & i
            30 Wend
                Debug.Print "g-wend-inside: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "g-wend-inside: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PGWendOutside()
                Dim i As Integer, s As String, b As Integer, c As New Collection
                On Error GoTo h
            GoTo 30
            10 While i < 3
            20 i = i + 1
            30 Wend
                Debug.Print "g-wend-outside: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "g-wend-outside: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PGCaseelseOutside()
                Dim i As Integer, s As String, b As Integer, c As New Collection
                On Error GoTo h
            GoTo 40
            10 Select Case i
                Case 0
            30 s = "zero"
            40 Case Else
            50 s = s & "other"
            60 End Select
                Debug.Print "g-caseelse-outside: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "g-caseelse-outside: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PGCaseelseInside()
                Dim i As Integer, s As String, b As Integer, c As New Collection
                On Error GoTo h
            10 Select Case i
                Case 0
            30 GoTo 40
            35 s = "zero"
            40 Case Else
            50 s = s & "other"
            60 End Select
                Debug.Print "g-caseelse-inside: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "g-caseelse-inside: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PGCaseInside()
                Dim i As Integer, s As String, b As Integer, c As New Collection
                On Error GoTo h
            10 Select Case i
                Case 0
            30 GoTo 40
            35 s = "zero"
            40 Case 5
            50 s = s & "five"
            60 End Select
                Debug.Print "g-case-inside: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "g-case-inside: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PGEndselectInside()
                Dim i As Integer, s As String, b As Integer, c As New Collection
                On Error GoTo h
            10 Select Case i
                Case 0
            30 GoTo 60
            35 s = "zero"
            40 Case Else
            50 s = s & "other"
            60 End Select
            65 s = s & "after"
                Debug.Print "g-endselect-inside: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "g-endselect-inside: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PGEndwithInside()
                Dim i As Integer, s As String, b As Integer, c As New Collection
                On Error GoTo h
            10 With c
            20 GoTo 30
            25 .Add 2
            30 End With
            35 s = CStr(c.Count)
                Debug.Print "g-endwith-inside: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "g-endwith-inside: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub TPGEndsub()
            10 Debug.Print "T starts"
            20 GoTo 90
            30 Debug.Print "not reached"
            90 End Sub
            Sub PGEndsub()
                Dim i As Integer, s As String
                On Error GoTo h
                TPGEndsub
                Debug.Print "g-endsub: back in Main"
                Exit Sub
            h:
                Debug.Print "g-endsub: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            PGNextInside
            PGEndifInside
            PGEndifOutside
            PGElseOutside
            PGElseFromThen
            PGElseifFromThen
            PGLoopInside
            PGLoopOutside
            PGLoopbareInside
            PGWendInside
            PGWendOutside
            PGCaseelseOutside
            PGCaseelseInside
            PGCaseInside
            PGEndselectInside
            PGEndwithInside
            PGEndsub
            """);
        AssertDebugLog(Lines(
            "g-next-inside: ok [13] i 4",
            "g-endif-inside: ok [after] i 0",
            "g-endif-outside: ok [after] i 0",
            "g-else-outside: ok [] i 1",
            "g-else-from-then: ok [] i 1",
            "g-elseif-from-then: ok [] i 1",
            "g-loop-inside: ok [345] i 5",
            "g-loop-outside: ok [] i 3",
            "g-loopbare-inside: ok [134] i 4",
            "g-wend-inside: ok [23] i 3",
            "g-wend-outside: ok [] i 3",
            "g-caseelse-outside: ok [] i 0",
            "g-caseelse-inside: ok [] i 0",
            "g-case-inside: ok [] i 0",
            "g-endselect-inside: ok [after] i 0",
            "g-endwith-inside: ok [0] i 0",
            "T starts",
            "g-endsub: back in Main"));
    }

    [Fact]
    public async Task A_label_on_a_closing_line_is_a_label_not_a_call()
    {
        // The same as a number, and `Skip: Next` is the usual way to go round a loop early.
        await Run("""
            Sub PLNextInside()
                Dim i As Integer, s As String, v As Variant, c As New Collection
                On Error GoTo h
            For i = 1 To 3
            If i = 2 Then GoTo Skip
            s = s & i
            Skip: Next
                Debug.Print "l-next-inside: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "l-next-inside: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PLNextiInside()
                Dim i As Integer, s As String, v As Variant, c As New Collection
                On Error GoTo h
            For i = 1 To 3
            If i = 2 Then GoTo Skip
            s = s & i
            Skip: Next i
                Debug.Print "l-nexti-inside: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "l-nexti-inside: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PLForeachInside()
                Dim i As Integer, s As String, v As Variant, c As New Collection
                On Error GoTo h
            c.Add 1
            c.Add 2
            c.Add 3
            For Each v In c
            If v = 2 Then GoTo Skip
            s = s & v
            Skip: Next
                Debug.Print "l-foreach-inside: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "l-foreach-inside: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PLNumlabelNext()
                Dim i As Integer, s As String, v As Variant, c As New Collection
                On Error GoTo h
            For i = 1 To 3
            If i = 2 Then GoTo Skip
            s = s & i
            30 Skip: Next
                Debug.Print "l-numlabel-next: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "l-numlabel-next: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PLLoopInside()
                Dim i As Integer, s As String, v As Variant, c As New Collection
                On Error GoTo h
            Do
            i = i + 1
            If i < 3 Then GoTo Skip
            s = s & i
            Skip: Loop While i < 5
                Debug.Print "l-loop-inside: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "l-loop-inside: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PLWendInside()
                Dim i As Integer, s As String, v As Variant, c As New Collection
                On Error GoTo h
            While i < 3
            i = i + 1
            If i = 1 Then GoTo Skip
            s = s & i
            Skip: Wend
                Debug.Print "l-wend-inside: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "l-wend-inside: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PLEndifInside()
                Dim i As Integer, s As String, v As Variant, c As New Collection
                On Error GoTo h
            If i = 0 Then
            GoTo Skip
            s = "skipped"
            Skip: End If
            s = s & "after"
                Debug.Print "l-endif-inside: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "l-endif-inside: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PLElseFromThen()
                Dim i As Integer, s As String, v As Variant, c As New Collection
                On Error GoTo h
            i = 1
            If i = 1 Then
            GoTo Skip
            s = "then"
            Skip: Else
            s = "else"
            End If
                Debug.Print "l-else-from-then: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "l-else-from-then: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PLCaseelseInside()
                Dim i As Integer, s As String, v As Variant, c As New Collection
                On Error GoTo h
            Select Case i
                Case 0
            GoTo Skip
            s = "zero"
            Skip: Case Else
            s = s & "other"
            End Select
                Debug.Print "l-caseelse-inside: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "l-caseelse-inside: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PLEndselectInside()
                Dim i As Integer, s As String, v As Variant, c As New Collection
                On Error GoTo h
            Select Case i
                Case 0
            GoTo Skip
            s = "zero"
            Skip: End Select
            s = s & "after"
                Debug.Print "l-endselect-inside: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "l-endselect-inside: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PLEndwithInside()
                Dim i As Integer, s As String, v As Variant, c As New Collection
                On Error GoTo h
            With c
            GoTo Skip
            .Add 2
            Skip: End With
            s = CStr(c.Count)
                Debug.Print "l-endwith-inside: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "l-endwith-inside: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PLPlainNext()
                Dim i As Integer, s As String, v As Variant, c As New Collection
                On Error GoTo h
            For i = 1 To 3
            s = s & i
            Skip: Next
                Debug.Print "l-plain-next: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "l-plain-next: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub TPLEndsub()
                Debug.Print "T starts"
                GoTo Done
                Debug.Print "not reached"
            Done: End Sub
            Sub PLEndsub()
                Dim i As Integer, s As String
                On Error GoTo h
                TPLEndsub
                Debug.Print "l-endsub: back in Main"
                Exit Sub
            h:
                Debug.Print "l-endsub: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            PLNextInside
            PLNextiInside
            PLForeachInside
            PLNumlabelNext
            PLLoopInside
            PLWendInside
            PLEndifInside
            PLElseFromThen
            PLCaseelseInside
            PLEndselectInside
            PLEndwithInside
            PLPlainNext
            PLEndsub
            """);
        AssertDebugLog(Lines(
            "l-next-inside: ok [13] i 4",
            "l-nexti-inside: ok [13] i 4",
            "l-foreach-inside: ok [13] i 0",
            "l-numlabel-next: ok [13] i 4",
            "l-loop-inside: ok [345] i 5",
            "l-wend-inside: ok [23] i 3",
            "l-endif-inside: ok [after] i 0",
            "l-else-from-then: ok [] i 1",
            "l-caseelse-inside: ok [] i 0",
            "l-endselect-inside: ok [after] i 0",
            "l-endwith-inside: ok [0] i 0",
            "l-plain-next: ok [123] i 4",
            "T starts",
            "l-endsub: back in Main"));
    }

    [Fact]
    public async Task Erl_for_an_error_on_a_numbered_closing_line_is_that_lines_number()
    {
        // A Do While or While condition asked again reads its own line, 10, not the Loop or Wend line (#2528).
        await Run("""
            Sub PELoopcond()
                Dim i As Integer, s As String, b As Integer, c As New Collection
                On Error GoTo h
            10 Do
            20 i = i + 1
            30 Loop While 1 / (2 - i) > -5
                Debug.Print "e-loopcond: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "e-loopcond: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PELoopbare()
                Dim i As Integer, s As String, b As Integer, c As New Collection
                On Error GoTo h
            10 Do While 1 / (2 - i) > -5
            20 i = i + 1
            30 Loop
                Debug.Print "e-loopbare: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "e-loopbare: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PEWend()
                Dim i As Integer, s As String, b As Integer, c As New Collection
                On Error GoTo h
            10 While 1 / (2 - i) > -5
            20 i = i + 1
            30 Wend
                Debug.Print "e-wend: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "e-wend: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PEElseif()
                Dim i As Integer, s As String, b As Integer, c As New Collection
                On Error GoTo h
            10 If i = 1 Then
            20 s = "a"
            30 ElseIf 1 / i > 0 Then
            40 s = "b"
            50 End If
                Debug.Print "e-elseif: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "e-elseif: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PECase()
                Dim i As Integer, s As String, b As Integer, c As New Collection
                On Error GoTo h
            10 Select Case i
                Case 1
            20 s = "one"
            40 Case 1 / i
            50 s = "b"
            60 End Select
                Debug.Print "e-case: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "e-case: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PENext()
                Dim i As Integer, s As String, b As Integer, c As New Collection
                On Error GoTo h
            10 For b = 32766 To 32767
            20 s = s & "x"
            30 Next
                Debug.Print "e-next: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "e-next: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PENextStep()
                Dim i As Integer, s As String, b As Integer, c As New Collection
                On Error GoTo h
            10 For b = 1 To 3 Step 1 / i
            20 s = s & "x"
            30 Next
                Debug.Print "e-next-step: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "e-next-step: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            PELoopcond
            PELoopbare
            PEWend
            PEElseif
            PECase
            PENext
            PENextStep
            """);
        AssertDebugLog(Lines(
            "e-loopcond: err 11 erl 30 [] i 2",
            "e-loopbare: err 11 erl 10 [] i 2",
            "e-wend: err 11 erl 10 [] i 2",
            "e-elseif: err 11 erl 30 [] i 0",
            "e-case: err 11 erl 40 [] i 0",
            "e-next: err 6 erl 30 [xx] i 0",
            "e-next-step: err 11 erl 10 [] i 0"));
    }

    [Fact]
    public async Task Erl_reads_a_number_on_a_closing_line_at_or_above_the_error()
    {
        // After a Then part ran, past an unnumbered End If, the nearest number above is in the Else part (#2528).
        await Run("""
            Sub PPElseAfterThen()
                Dim i As Integer, s As String, b As Integer, x As Integer, v As Variant, c As New Collection
                On Error GoTo h
            10 If i = 0 Then
            20 s = "a"
            30 Else
            40 s = "b"
            End If
            x = 1 / 0
                Debug.Print "p-else-after-then: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "p-else-after-then: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PPElseifAfterThen()
                Dim i As Integer, s As String, b As Integer, x As Integer, v As Variant, c As New Collection
                On Error GoTo h
            10 If i = 0 Then
            20 s = "a"
            30 ElseIf i = 5 Then
            40 s = "b"
            End If
            x = 1 / 0
                Debug.Print "p-elseif-after-then: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "p-elseif-after-then: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PPCaseelseAfterCase()
                Dim i As Integer, s As String, b As Integer, x As Integer, v As Variant, c As New Collection
                On Error GoTo h
            10 Select Case i
                Case 0
            20 s = "zero"
            40 Case Else
            50 s = "other"
            End Select
            x = 1 / 0
                Debug.Print "p-caseelse-after-case: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "p-caseelse-after-case: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PPCaseAfterCase()
                Dim i As Integer, s As String, b As Integer, x As Integer, v As Variant, c As New Collection
                On Error GoTo h
            10 Select Case i
                Case 0
            20 s = "zero"
            40 Case 5
            50 s = "five"
            End Select
            x = 1 / 0
                Debug.Print "p-case-after-case: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "p-case-after-case: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PPEndif()
                Dim i As Integer, s As String, b As Integer, x As Integer, v As Variant, c As New Collection
                On Error GoTo h
            10 If i = 0 Then
            20 s = "a"
            30 End If
            x = 1 / 0
                Debug.Print "p-endif: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "p-endif: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PPEndselect()
                Dim i As Integer, s As String, b As Integer, x As Integer, v As Variant, c As New Collection
                On Error GoTo h
            10 Select Case i
                Case 0
            20 s = "zero"
            60 End Select
            x = 1 / 0
                Debug.Print "p-endselect: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "p-endselect: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PPEndwith()
                Dim i As Integer, s As String, b As Integer, x As Integer, v As Variant, c As New Collection
                On Error GoTo h
            10 With c
            20 .Add 1
            30 End With
            x = 1 / 0
                Debug.Print "p-endwith: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "p-endwith: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            Sub PPDowhileTopUnnumbered()
                Dim i As Integer, s As String, b As Integer, x As Integer, v As Variant, c As New Collection
                On Error GoTo h
            Do While 1 / (2 - i) > -5
            20 i = i + 1
            30 Loop
                Debug.Print "p-dowhile-top-unnumbered: ok [" & s & "] i " & i
                Exit Sub
            h:
                Debug.Print "p-dowhile-top-unnumbered: err " & Err.Number & " erl " & Erl & " [" & s & "] i " & i
            End Sub
            PPElseAfterThen
            PPElseifAfterThen
            PPCaseelseAfterCase
            PPCaseAfterCase
            PPEndif
            PPEndselect
            PPEndwith
            PPDowhileTopUnnumbered
            """);
        AssertDebugLog(Lines(
            "p-else-after-then: err 11 erl 40 [a] i 0",
            "p-elseif-after-then: err 11 erl 40 [a] i 0",
            "p-caseelse-after-case: err 11 erl 50 [zero] i 0",
            "p-case-after-case: err 11 erl 50 [zero] i 0",
            "p-endif: err 11 erl 30 [a] i 0",
            "p-endselect: err 11 erl 60 [zero] i 0",
            "p-endwith: err 11 erl 30 [] i 0",
            "p-dowhile-top-unnumbered: err 11 erl 0 [] i 2"));
    }

    [Fact]
    public async Task A_head_on_End_Function_returns_as_End_Sub_does()
    {
        // With the value the Function had; `90: End Sub` is a head too, and `x: End Sub` after a statement is not. The End Property probe waits for #977: a standard module Property Get is not reached by its name at all.
        await Run("""
            Function F() As Long
            10  F = 5
            20  GoTo 90
            30  F = 7
            90 End Function
            Function G() As Long
                G = 6
                GoTo Done
                G = 8
            Done: End Function
            Sub PFEndfunction()
                Debug.Print "f-endfunction: F " & F() & " G " & G()
            End Sub
            Sub TPFColonNumber()
                Debug.Print "T starts"
                GoTo 90
                Debug.Print "not reached"
            90: End Sub
            Sub PFColonNumber()
                TPFColonNumber
                Debug.Print "f-colon-number: back in Main"
            End Sub
            Sub TPFStatementThenEnd()
                Debug.Print "T starts": End Sub
            Sub PFStatementThenEnd()
                TPFStatementThenEnd
                Debug.Print "f-statement-then-end: back in Main"
            End Sub
            PFEndfunction
            PFColonNumber
            PFStatementThenEnd
            """);
        AssertDebugLog(Lines(
            "f-endfunction: F 5 G 6",
            "T starts",
            "f-colon-number: back in Main",
            "T starts",
            "f-statement-then-end: back in Main"));
    }

    [Fact]
    public async Task A_GoTo_from_outside_a_For_loop_to_its_Next_is_refused()
    {
        // vb6.exe hung on it in native code and ran on with i 1 as p-code, raising nothing either way (g-next-outside).
        var act = async () => await Run("""
            Sub Main()
                Dim i As Integer, s As String
                GoTo 30
            10 For i = 1 To 3
            25 s = s & i
            30 Next
            End Sub
            Main
            """);
        (await act.Should().ThrowAsync<NotImplementedException>()).Which.Message.Should().Contain("Next");
    }

    [Fact]
    public async Task A_line_number_before_the_first_Case_is_refused()
    {
        // vb6.exe refuses it to compile: "Statements and labels invalid between Select Case and first Case" (ln-case).
        var act = async () => await Run("""
            Sub Main()
                Dim i As Integer
            10 Select Case i
            20 Case 0
            30 i = 1
            40 End Select
            End Sub
            Main
            """);
        await act.Should().ThrowAsync<VBCompileErrorException>();
    }
}
