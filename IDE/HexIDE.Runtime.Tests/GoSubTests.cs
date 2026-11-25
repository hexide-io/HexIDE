using HexIDE.IDE;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Error descriptions 5/94: docs/vb6-fidelity-oracle/strcomp-strconv-environ-switch-and-error.md (Error catalogue).
// Measured programs/results: https://github.com/hexide-io/HexIDE/issues/4726 (latest comment), oracle PR #4727.
public class GoSubTests : BaseVBTestFixture
{
    private const string OverflowProgram = """
        Dim EHit As Long, Depth As Long
          Sub G41()
              On Error GoTo E
              GoSub A
              Exit Sub
          A:
              Depth = Depth + 1
              GoSub A
              Return
          E:
              EHit = Err.Number
              Exit Sub
          End Sub
          Sub G42()
              On Error GoTo E
              GoSub A
              EHit = EHit + 1000
              Exit Sub
          A:
              Depth = Depth + 1
              GoSub A
              Return
          E:
              EHit = Err.Number
              Resume Next
          End Sub
          Sub Main()
              On Error GoTo MH
              G41
              Log "G41 back EHit=" & EHit & " depth=" & Depth
              EHit = 0: Depth = 0
              G42
              Log "G42 back EHit=" & EHit & " depth=" & Depth
              Exit Sub
          MH:
              Log "main err " & Err.Number & " EHit=" & EHit & " depth=" & Depth
              Resume Next
          End Sub

        Sub Log(ByVal text As String)
        Debug.Print text
        End Sub
        Main
        """ + "\n";

    public static IEnumerable<object[]> MeasuredRows()
    {
        yield return new object[] { "G1", """
            On 2 GoTo A, B, C
            s = s & "fell "
            A:
            s = s & "A"
            GoTo Done
            B:
            s = s & "B"
            GoTo Done
            C:
            s = s & "C"
            Done:
            Log s
            """, new[] { "B" } };
        yield return new object[] { "G2", """
            On 0 GoTo A, B, C
            s = s & "fell "
            A:
            s = s & "A"
            GoTo Done
            B:
            s = s & "B"
            GoTo Done
            C:
            s = s & "C"
            Done:
            Log s
            """, new[] { "fell A" } };
        yield return new object[] { "G3", """
            On 4 GoTo A, B, C
            s = s & "fell "
            A:
            s = s & "A"
            GoTo Done
            B:
            s = s & "B"
            GoTo Done
            C:
            s = s & "C"
            Done:
            Log s
            """, new[] { "fell A" } };
        yield return new object[] { "G4", """
            On -1 GoTo A, B, C
            s = s & "fell "
            A:
            s = s & "A"
            GoTo Done
            B:
            s = s & "B"
            GoTo Done
            C:
            s = s & "C"
            Done:
            Log s
            """, new[] { "error 5 Invalid procedure call or argument" } };
        yield return new object[] { "G5", """
            On 256 GoTo A, B, C
            s = s & "fell "
            A:
            s = s & "A"
            GoTo Done
            B:
            s = s & "B"
            GoTo Done
            C:
            s = s & "C"
            Done:
            Log s
            """, new[] { "error 5 Invalid procedure call or argument" } };
        yield return new object[] { "G6", """
            On 2.5 GoTo A, B, C
            s = s & "fell "
            A:
            s = s & "A"
            GoTo Done
            B:
            s = s & "B"
            GoTo Done
            C:
            s = s & "C"
            Done:
            Log s
            """, new[] { "B" } };
        yield return new object[] { "G7", """
            On 1.5 GoTo A, B, C
            s = s & "fell "
            A:
            s = s & "A"
            GoTo Done
            B:
            s = s & "B"
            GoTo Done
            C:
            s = s & "C"
            Done:
            Log s
            """, new[] { "B" } };
        yield return new object[] { "G8", """
            On 2.6 GoTo A, B, C
            s = s & "fell "
            A:
            s = s & "A"
            GoTo Done
            B:
            s = s & "B"
            GoTo Done
            C:
            s = s & "C"
            Done:
            Log s
            """, new[] { "C" } };
        yield return new object[] { "G9", """
            On "2" GoTo A, B, C
            s = s & "fell "
            A:
            s = s & "A"
            GoTo Done
            B:
            s = s & "B"
            GoTo Done
            C:
            s = s & "C"
            Done:
            Log s
            """, new[] { "B" } };
        yield return new object[] { "G10", """
            On Null GoTo A, B, C
            s = s & "fell "
            A:
            s = s & "A"
            GoTo Done
            B:
            s = s & "B"
            GoTo Done
            C:
            s = s & "C"
            Done:
            Log s
            """, new[] { "error 94 Invalid use of Null" } };
        yield return new object[] { "G11", """
            On Empty GoTo A, B, C
            s = s & "fell "
            A:
            s = s & "A"
            GoTo Done
            B:
            s = s & "B"
            GoTo Done
            C:
            s = s & "C"
            Done:
            Log s
            """, new[] { "fell A" } };
        yield return new object[] { "G12", """
            On Pick() GoTo A, B, C
            s = s & "fell "
            A:
            s = s & "A"
            GoTo Done
            B:
            s = s & "B"
            GoTo Done
            C:
            s = s & "C"
            Done:
            Log s
            Log "calls " & Calls
            """, new[] { "B", "calls 1" } };
        yield return new object[] { "G13", """
            On 255 GoTo A, B, C
            s = s & "fell "
            A:
            s = s & "A"
            GoTo Done
            B:
            s = s & "B"
            GoTo Done
            C:
            s = s & "C"
            Done:
            Log s
            """, new[] { "fell A" } };
        yield return new object[] { "G14", """
            On 2 GoSub A, B, C
            s = s & "back"
            Log s
            Exit Sub
            A:
            s = s & "A "
            Return
            B:
            s = s & "B "
            Return
            C:
            s = s & "C "
            Return
            """, new[] { "B back" } };
        yield return new object[] { "G15", """
            On 0 GoSub A, B, C
            s = s & "back"
            Log s
            Exit Sub
            A:
            s = s & "A "
            Return
            B:
            s = s & "B "
            Return
            C:
            s = s & "C "
            Return
            """, new[] { "back" } };
        yield return new object[] { "G16", """
            On 5 GoSub A, B, C
            s = s & "back"
            Log s
            Exit Sub
            A:
            s = s & "A "
            Return
            B:
            s = s & "B "
            Return
            C:
            s = s & "C "
            Return
            """, new[] { "back" } };
        yield return new object[] { "G17", """
            On -1 GoSub A, B, C
            s = s & "back"
            Log s
            Exit Sub
            A:
            s = s & "A "
            Return
            B:
            s = s & "B "
            Return
            C:
            s = s & "C "
            Return
            """, new[] { "error 5 Invalid procedure call or argument" } };
        yield return new object[] { "G18", """
            GoSub A
            s = s & "back"
            Log s
            Exit Sub
            A:
            s = s & "A "
            Return
            """, new[] { "A back" } };
        yield return new object[] { "G19", """
            Return
            """, new[] { "error 3 Return without GoSub" } };
        yield return new object[] { "G20", """
            GoSub A
            s = s & "back"
            Log s
            Exit Sub
            A:
            s = s & "A1 "
            GoSub B
            s = s & "A2 "
            Return
            B:
            s = s & "B "
            Return
            """, new[] { "A1 B A2 back" } };
        yield return new object[] { "G21", """
            Dim i As Integer
            For i = 1 To 3
            GoSub A
            Next i
            s = s & "end"
            Log s
            Exit Sub
            A:
            s = s & i & " "
            Return
            """, new[] { "1 2 3 end" } };
        yield return new object[] { "G22", """
            Dim i As Integer
            i = 2
            Do While i = 2
            Select Case i
            Case 2
            If i = 2 Then
            GoSub A
            s = s & "in "
            End If
            End Select
            i = 0
            Loop
            s = s & "end"
            Log s
            Exit Sub
            A:
            s = s & "A" & i & " "
            Return
            """, new[] { "A2 in end" } };
        yield return new object[] { "G23", """
            GoSub A
            Log "wrong"
            Exit Sub
            A:
            Log "A"
            Exit Sub
            """, new[] { "A" } };
        yield return new object[] { "G24", """
            GoSub A
            s = s & "back"
            Log s
            Exit Sub
            A:
            s = s & "A "
            GoTo B
            B:
            s = s & "B "
            Return
            """, new[] { "A B back" } };
        yield return new object[] { "G25", """
            s = "x "
            A:
            s = s & "A"
            Log s
            Return
            """, new[] { "x A", "error 3 Return without GoSub" } };
        yield return new object[] { "G26", """
                  On Error GoTo H
                  GoSub A
                  s = s & "back "
                  GoTo Done
              A:
                  s = s & "A1 "
                  Err.Raise 5
                  s = s & "A2 "
                  Return
              H:
                  s = s & "H" & Err.Number & " "
                  Resume Next
              Done:
                  Log ID & s

            """, new[] { "G26 A1 H5 A2 back " } };
        yield return new object[] { "G27", """
                  On Error Resume Next
                  GoSub A
                  s = s & "back " & Err.Number & " "
                  Log ID & s
                  Exit Sub
              A:
                  s = s & "A1 "
                  Err.Raise 11
                  s = s & "A2 " & Err.Number & " "
                  Err.Clear
                  Return

            """, new[] { "G27 A1 A2 11 back 0 " } };
        yield return new object[] { "G28", """
                  On Error GoTo H
                  GoSub A
                  s = s & "back "
                  GoTo Done
              A:
                  Err.Raise 5
                  s = s & "A2 "
                  Return
              H:
                  s = s & "H" & Err.Number & " "
                  If Err.Number = 3 Then Resume Done
                  Resume L
              L:
                  s = s & "L "
                  Return
              Done:
                  Log ID & s

            """, new[] { "G28 H5 L back " } };
        yield return new object[] { "G29", """
            GoSub 100
            s = s & "back "
            On 2 GoTo 200, 300
            100 s = s & "100 "
            Return
            200 Log "wrong"
            Exit Sub
            300 s = s & "300"
            Log s
            """, new[] { "100 back 300" } };
        yield return new object[] { "G30", """
            GoSub A
            Log "wrong"
            Exit Sub
            A:
            Log "A"
            """, new[] { "A" } };
        yield return new object[] { "G31", """
            GoSub A
            Log "wrong"
            Exit Sub
            A:
            s = "A "
            GoTo Done
            Done:
            s = s & "Done"
            Log s
            """, new[] { "A Done" } };
        yield return new object[] { "G32", """
            Dim n As Integer
            GoSub A
            s = s & "back " & n
            Log s
            Exit Sub
            A:
            n = n + 1
            If n < 4 Then GoSub A
            s = s & "r" & n & " "
            Return
            """, new[] { "r4 r4 r4 r4 back 4" } };
        yield return new object[] { "G33", """
                  GoSub A
                  s = s & "back "
                  Err.Raise 6
                  s = s & "after "
                  Log ID & s
                  Exit Sub
              A:
                  On Error GoTo H
                  s = s & "A "
                  Return
              H:
                  s = s & "H" & Err.Number & " "
                  Resume Next

            """, new[] { "G33 A back H6 after " } };
        yield return new object[] { "G34", """
                  On Error GoTo H
                  Err.Raise 5
                  s = s & "after "
                  Log ID & s
                  Exit Sub
              H:
                  s = s & "H" & Err.Number & " "
                  GoSub A
                  s = s & "H2 "
                  Resume Next
              A:
                  s = s & "A "
                  Return

            """, new[] { "G34 H5 A H2 after " } };
        yield return new object[] { "G35", """
                  On Error GoTo H
                  GoSub A
                  s = s & "back " & Err.Number & " "
                  Log ID & s
                  Exit Sub
              A:
                  Err.Raise 5
                  s = s & "A2 "
                  Return
              H:
                  s = s & "H" & Err.Number & " "
                  Log ID & s & "| returning"
                  Return

            """, new[] { "G35 H5 | returning", "G35 H5 back 5 " } };
        yield return new object[] { "G41", "", new[] { $"G41 back EHit=28 depth={StatementExecutor.MaxGoSubDepth}" } };
        yield return new object[] { "G42", "", new[] { $"G42 back EHit=1028 depth={StatementExecutor.MaxGoSubDepth}" } };
    }

    [Theory]
    [MemberData(nameof(MeasuredRows))]
    public async Task MeasuredRow(string row, string body, string[] expected)
    {
        if (row is "G41" or "G42")
        {
            await RunOnUiStack(OverflowProgram);
            debug.Should().HaveCount(2);
            debug[row == "G41" ? 0 : 1].Value.Should().Be(expected.Single());
            return;
        }
        await Run($"""
            Dim Calls As Long, EHit As Long, Depth As Long
            Sub {row}()
                Const ID = "{row} "
                Dim s As String, v
                {body}
            End Sub
            Sub Main()
                On Error GoTo MH
                {row}
                Exit Sub
            MH:
                Log "error " & Err.Number & " " & Err.Description
                Resume Next
            End Sub
            Sub Log(ByVal text As String)
                Debug.Print text
            End Sub
            Function Pick() As Integer
                Calls = Calls + 1
                Pick = 2
            End Function
            Main
            """ + "\n");
        AssertDebugLog(expected.Select(s => new Vb6Value(s)).ToList());
    }

    /// <summary>
    /// Runs <paramref name="code"/> on a thread with the stack the interpreter has on Windows and Linux (64 MiB). G41's
    /// handler runs where the GoSub stood, so the run needs room for MaxGoSubDepth passes and the handler after them:
    /// a test thread's 1.5 MiB holds only about 395 passes, and there the stack probe raises 28 first.
    /// </summary>
    private async Task RunOnUiStack(string code)
    {
        Exception? failure = null;
        await Task.Run(() => UiThread.RunOnNewThread(() =>
        {
            try
            {
                Run(code).GetAwaiter().GetResult();
            }
            catch (Exception error)
            {
                failure = error;
            }
            return 0;
        }, UiThread.StackBytes), TestContext.Current.CancellationToken);
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
    }

    // Native and p-code measurements: #4880, oracle PR #4986,
    // docs/vb6-fidelity-oracle/gosub-return-and-on-goto.md.
    public static IEnumerable<object[]> SelectorRows()
    {
        foreach (var branch in new[] { "GoTo", "GoSub" })
        {
            foreach (var value in new[] { "2", "CByte(2)", "255", "256", "32767", "-32768", "-32768.5",
                         "32768", "-32769", "65537", "65538", "-65534", "131074", "2147483647",
                         "2147483648#", "1E+20", "CSng(32768)", "CCur(32768)", "CDec(32768)", "\"32768\"", "32767.5", "32768.4" })
            {
                var expected = value is "2" or "CByte(2)" ? "B" : value == "255" ? "fell" :
                    value is "256" or "32767" or "-32768" or "-32768.5" ? "5" : "6";
                yield return new object[] { branch, value, false, expected };
            }
            yield return new object[] { branch, "32768", true, "next 6" };
            yield return new object[] { branch, "256", true, "next 5" };
        }
    }

    [Theory]
    [MemberData(nameof(SelectorRows))]
    public async Task MeasuredSelector(string branch, string value, bool resumeNext, string expected)
    {
        await Run($"""
            Sub Main()
                On Error {(resumeNext ? "Resume Next" : "GoTo E")}
                Dim v As Variant
                v = {value}
                On v {branch} A, B
                Debug.Print {(resumeNext ? "\"next \" & Err.Number" : "\"fell\"")}
                Exit Sub
            A:
                Debug.Print "A"
                Exit Sub
            B:
                Debug.Print "B"
                Exit Sub
            E:
                Debug.Print CStr(Err.Number)
            End Sub
            Main
            """ + "\n");
        AssertDebugLog(new List<Vb6Value> { new(expected) });
    }

    public static IEnumerable<object[]> HandlerResumeRows()
    {
        yield return new object[]
        {
            "R1",
            "Err.Raise 5",
            "Debug.Print \"after \" & Err.Number",
            "GoSub A",
            "Debug.Print \"wrong\"",
            "Resume Next",
            new[] { "H 5", "after 0" }
        };
        yield return new object[]
        {
            "R2",
            "v = Boom()",
            "Debug.Print \"v=\" & v & \" err \" & Err.Number",
            "GoSub A",
            "Debug.Print \"wrong\"",
            "Resume",
            new[] { "H 5", "v=2 err 0" }
        };
        yield return new object[]
        {
            "R3",
            "Err.Raise 5",
            "L1:\nDebug.Print \"L1 \" & Err.Number",
            "GoSub A",
            "Debug.Print \"wrong\"",
            "Resume L1",
            new[] { "H 5", "L1 0" }
        };
        yield return new object[]
        {
            "R4",
            "Err.Raise 5",
            "Err.Raise 6\nDebug.Print \"after \" & Err.Number",
            "If Err.Number = 6 Then Resume Next\nGoSub A",
            "Debug.Print \"wrong\"",
            "Resume Next",
            new[] { "H 5", "H 6", "after 0" }
        };
        yield return new object[]
        {
            "R5",
            "Err.Raise 5",
            "Debug.Print \"after \" & Err.Number",
            "GoSub A",
            "Debug.Print \"wrong\"",
            "GoSub B\nReturn\nB:\nResume Next",
            new[] { "H 5", "after 0" }
        };
        yield return new object[]
        {
            "R6",
            "Err.Raise 5",
            "Debug.Print \"after \" & Err.Number",
            "On 1 GoSub A",
            "Debug.Print \"wrong\"",
            "Resume Next",
            new[] { "H 5", "after 0" }
        };
        yield return new object[]
        {
            "R7",
            "Err.Raise 5",
            "Return",
            "GoSub A",
            "Debug.Print \"back \" & Err.Number",
            "Resume Next",
            new[] { "H 5", "back 0" }
        };
        yield return new object[]
        {
            "R8",
            "Err.Raise 5",
            "Return",
            "If Err.Number = 7 Then Resume Next\nGoSub A",
            "Err.Raise 7\nDebug.Print \"after7 \" & Err.Number",
            "Resume Next",
            new[] { "H 5", "H 7", "after7 0" }
        };
        yield return new object[]
        {
            "R1b",
            "Err.Raise 5",
            "Debug.Print \"after \" & Err.Number\nReturn",
            "GoSub A",
            "passes = passes + 1\nDebug.Print \"back \" & Err.Number & \" pass \" & passes\nIf passes = 3 Then Exit Sub\nResume Next",
            "Resume Next\nDebug.Print \"A after \" & Err.Number & \" pass \" & passes\nReturn",
            new[] { "H 5", "after 0", "back 0 pass 1", "H 20", "H 20", "A after 0 pass 1", "back 0 pass 2", "H 20", "H 20", "A after 0 pass 2", "back 0 pass 3" }
        };
    }
    [Theory]
    [MemberData(nameof(HandlerResumeRows))]
    public async Task MeasuredHandlerResume(string row, string fault, string after, string call, string back, string routine, string[] expected)
    {
        await Run($"""
            Dim calls As Long
            Function Boom() As Long
                calls = calls + 1
                If calls = 1 Then Err.Raise 5
                Boom = calls
            End Function
            Sub {row}()
                Dim v, passes As Long
                On Error GoTo H
                {fault}
                {after}
                Exit Sub
            H:
                Debug.Print "H " & Err.Number
                {call}
                {back}
                {(row == "R1b" ? "" : "Exit Sub")}
            A:
                {routine}
                Return
            End Sub
            Sub Main()
                On Error Resume Next
                {row}
                If Err.Number <> 0 Then Debug.Print "caller " & Err.Number
            End Sub
            Main
            """ + "\n");
        AssertDebugLog(expected.Select(s => new Vb6Value(s)).ToList());
    }

    // VB6 SP6, native and p-code, on #4880's review: a fault left behind by a routine, then a Resume in the main flow.
    public static IEnumerable<object[]> LeftoverFaultRows()
    {
        // Q1: the handler leaves by Return (G35), so the fault in A is still the one Resume resumes.
        yield return new object[]
        {
            "Q1",
            """
                Dim k As Long
                On Error GoTo H
                GoSub A
                Debug.Print "back " & Err.Number
                Resume Next
                Debug.Print "wrong"
                Exit Sub
            A:
                k = k + 1
                If k > 2 Then
                    Debug.Print "stopped"
                    Exit Sub
                End If
                Err.Raise 5
                Debug.Print "A after " & Err.Number
                Return
            H:
                Debug.Print "H " & Err.Number
                Return
            """,
            new[] { "H 5", "back 5", "A after 0", "H 3", "caller 3" }
        };
        // Q2: an error On Error Resume Next went past, inside A, leaves nothing to Resume.
        yield return new object[]
        {
            "Q2",
            """
                On Error Resume Next
                GoSub A
                Debug.Print "back " & Err.Number
                Resume Next
                Debug.Print "after " & Err.Number
                Exit Sub
            A:
                Err.Raise 5
                Debug.Print "A after " & Err.Number
                Return
            """,
            new[] { "A after 5", "back 5", "after 20", "caller 20" }
        };
        // Q3: after the handler's Return, it is still running, so a new error reaches the caller.
        yield return new object[]
        {
            "Q3",
            """
                On Error GoTo H
                GoSub A
                Debug.Print "back " & Err.Number
                Err.Raise 6
                Debug.Print "wrong"
                Exit Sub
            A:
                Err.Raise 5
                Debug.Print "wrong"
                Return
            H:
                Debug.Print "H " & Err.Number
                If Err.Number = 5 Then Return
                Resume Next
            """,
            new[] { "H 5", "back 5", "caller 6" }
        };
        // Q4: the same in the main flow, with no GoSub.
        yield return new object[]
        {
            "Q4",
            """
                Dim k As Long
                On Error Resume Next
                Err.Raise 5
                Debug.Print "after the raise " & Err.Number
                k = k + 1
                If k > 2 Then
                    Debug.Print "stopped"
                    Exit Sub
                End If
                Resume Next
                Debug.Print "after Resume Next " & Err.Number
            """,
            new[] { "after the raise 5", "after Resume Next 20", "caller 20" }
        };
    }

    [Theory]
    [MemberData(nameof(LeftoverFaultRows))]
    public async Task MeasuredLeftoverFault(string row, string body, string[] expected)
    {
        await Run($"Sub {row}()\n{body}\nEnd Sub\n" + """
            Sub Main()
                On Error Resume Next
            """ + $"\n    {row}\n" + """
                If Err.Number <> 0 Then Debug.Print "caller " & Err.Number
            End Sub
            Main
            """ + "\n");
        AssertDebugLog(expected.Select(s => new Vb6Value(s)).ToList());
    }
}
