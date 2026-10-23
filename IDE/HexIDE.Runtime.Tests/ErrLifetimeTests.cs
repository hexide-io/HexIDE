// Generated from the vb6.exe probes for #877 (scratchpad p877/gentests.py): each program is the probe as it ran,
// printing where the probe logged, and each expected line is what vb6.exe logged, native and p-code alike.

using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class ErrLifetimeTests : BaseVBTestFixture
{
    // Every On Error form clears Err; so does every Resume, and leaving a procedure from its handler by Exit or by
    // its end. Leaving one under On Error Resume Next keeps it, and a helper's On Error clears the one global Err
    // even inside its caller's handler.
    [Fact]
    public async Task Err_is_cleared_by_On_Error_by_Resume_and_by_leaving_a_handler_and_kept_otherwise()
    {
        await Run("""""""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Public Function E() As String
                E = Err.Number & " [" & Err.Description & "]"
            End Function

            Sub GoToLabelClears()
                On Error Resume Next
                Err.Raise 5
                On Error GoTo H
                L "goto-label-after-raise: " & E()
                Exit Sub
            H:
                L "goto-label: handler ran"
            End Sub

            Sub ResumeNextClearsAll()
                On Error Resume Next
                Err.Raise 5, "src", "desc"
                On Error Resume Next
                L "resume-next-clears: " & E() & " source [" & Err.Source & "]"
            End Sub

            Sub GoToZeroClears()
                On Error Resume Next
                Err.Raise 5
                On Error GoTo 0
                L "goto-0-clears: " & E()
            End Sub

            Sub HandlerExitSub()
                On Error GoTo H
                Err.Raise 11
                Exit Sub
            H:
                L "  in handler (Exit Sub): " & E()
                Exit Sub
            End Sub

            Function HandlerExitFunction() As Long
                On Error GoTo H
                Err.Raise 11
                Exit Function
            H:
                L "  in handler (Exit Function): " & E()
                HandlerExitFunction = 7
                Exit Function
            End Function

            Function HandlerEndFunction() As Long
                On Error GoTo H
                Err.Raise 11
                Exit Function
            H:
                HandlerEndFunction = 8
            End Function

            Sub HandlerEndSub()
                On Error GoTo H
                Err.Raise 11
                Exit Sub
            H:
            End Sub

            Sub HandlerResumeNext()
                On Error GoTo H
                Err.Raise 11
                L "handler-resume-next, after: " & E()
                Exit Sub
            H:
                Resume Next
            End Sub

            Sub HandlerResumeLabel()
                On Error GoTo H
                Err.Raise 11
                Exit Sub
            Back:
                L "handler-resume-label, after: " & E()
                Exit Sub
            H:
                Resume Back
            End Sub

            Sub HandlerResumeRetry()
                Dim tries As Integer
                On Error GoTo H
                If tries = 0 Then Err.Raise 11
                L "handler-resume-retry, after: " & E() & " tries " & tries
                Exit Sub
            H:
                tries = tries + 1
                Resume
            End Sub

            Sub NoHandlerExitSub()
                On Error Resume Next
                Err.Raise 11
                Exit Sub
            End Sub

            Sub NoHandlerEndSub()
                On Error Resume Next
                Err.Raise 11
            End Sub

            Sub NoTrapRaiser()
                Err.Raise 9
            End Sub

            Sub HandlerRaisesAnother()
                On Error GoTo H
                Err.Raise 11
                Exit Sub
            H:
                Err.Raise 13
            End Sub

            Sub Helper()
                On Error Resume Next
                L "  helper sees: " & E()
            End Sub

            Sub HelperInsideHandler()
                On Error GoTo H
                Err.Raise 11
                Exit Sub
            H:
                L "helper-in-handler, before: " & E()
                Helper
                L "helper-in-handler, after helper: " & E()
            End Sub

            Sub HandlerGoToZeroThenEnd()
                On Error GoTo H
                Err.Raise 11
                Exit Sub
            H:
                On Error GoTo 0
                L "  after On Error GoTo 0 in handler: " & E()
            End Sub

            Sub HandlerErrClearThenRaiseInCallee()
                On Error GoTo H
                NoTrapRaiser
                L "callee-error, not reached"
                Exit Sub
            H:
                L "  handler for a callee's error: " & E()
            End Sub

            Sub ResumeNextLoop()
                Dim i As Integer
                For i = 1 To 2
                    On Error Resume Next
                    If i = 1 Then Err.Raise 6
                    L "resume-next-in-loop " & i & ": " & E()
                Next
            End Sub

            Sub HandlerSetsNumberOnly()
                On Error GoTo H
                Err.Raise 11
                Exit Sub
            H:
                Err.Number = 0
                L "  handler set Err.Number = 0: " & E()
            End Sub

            Sub OnErrorInHandlerResumeNext()
                On Error GoTo H
                Err.Raise 11
                Exit Sub
            H:
                On Error Resume Next
                L "  On Error Resume Next inside the handler: " & E()
                Err.Raise 13
                L "  then a new error is trapped: " & E()
            End Sub

            Sub Main()
                On Error Resume Next
                GoToLabelClears
                ResumeNextClearsAll
                GoToZeroClears
                Err.Clear: Err.Raise 5: HandlerExitSub: L "handler-exit-sub, caller sees: " & E()
                Err.Clear: Dim v As Long: v = HandlerExitFunction(): L "handler-exit-function, caller sees: " & E() & " value " & v
                Err.Clear: v = HandlerEndFunction(): L "handler-end-function, caller sees: " & E() & " value " & v
                Err.Clear: HandlerEndSub: L "handler-end-sub, caller sees: " & E()
                Err.Clear: Err.Raise 5: HandlerEndSub: L "handler-end-sub after the caller's own 5: " & E()
                Err.Clear: HandlerResumeNext: L "handler-resume-next, caller sees: " & E()
                Err.Clear: HandlerResumeLabel: L "handler-resume-label, caller sees: " & E()
                Err.Clear: NoHandlerExitSub: L "no-handler-exit-sub, caller sees: " & E()
                Err.Clear: NoHandlerEndSub: L "no-handler-end-sub, caller sees: " & E()
                Err.Clear: HandlerRaisesAnother: L "handler-raises-another, caller sees: " & E()
                Err.Clear: HelperInsideHandler: L "helper-in-handler, caller sees: " & E()
                Err.Clear: HandlerGoToZeroThenEnd: L "handler-goto-0-then-end, caller sees: " & E()
                Err.Clear: HandlerErrClearThenRaiseInCallee: L "handler-for-callee, caller sees: " & E()
                Err.Clear: ResumeNextLoop
                Err.Clear: HandlerSetsNumberOnly: L "handler-set-number, caller sees: " & E()
                Err.Clear: OnErrorInHandlerResumeNext: L "on-error-in-handler, caller sees: " & E()
                Err.Clear: Err.Raise 5: Err.Clear: L "err-clear: " & E()
            End Sub

            Main
            """"""");

        AssertDebugLog(Lines("""
            goto-label-after-raise: 0 []
            resume-next-clears: 0 [] source []
            goto-0-clears: 0 []
              in handler (Exit Sub): 11 [Division by zero]
            handler-exit-sub, caller sees: 0 []
              in handler (Exit Function): 11 [Division by zero]
            handler-exit-function, caller sees: 0 [] value 7
            handler-end-function, caller sees: 0 [] value 8
            handler-end-sub, caller sees: 0 []
            handler-end-sub after the caller's own 5: 0 []
            handler-resume-next, after: 0 []
            handler-resume-next, caller sees: 0 []
            handler-resume-label, after: 0 []
            handler-resume-label, caller sees: 0 []
            no-handler-exit-sub, caller sees: 11 [Division by zero]
            no-handler-end-sub, caller sees: 11 [Division by zero]
            handler-raises-another, caller sees: 13 [Division by zero]
            helper-in-handler, before: 11 [Division by zero]
              helper sees: 0 []
            helper-in-handler, after helper: 0 []
            helper-in-handler, caller sees: 0 []
              after On Error GoTo 0 in handler: 0 []
            handler-goto-0-then-end, caller sees: 0 []
              handler for a callee's error: 9 [Subscript out of range]
            handler-for-callee, caller sees: 0 []
            resume-next-in-loop 1: 6 [Overflow]
            resume-next-in-loop 2: 0 []
              handler set Err.Number = 0: 0 [Division by zero]
            handler-set-number, caller sees: 0 []
              On Error Resume Next inside the handler: 0 []
            on-error-in-handler, caller sees: 13 [Type mismatch]
            err-clear: 0 []
            """));
    }

    // VB6 compiles On Error GoTo -1. It clears Err, keeps On Error Resume Next trapping, and leaves the handler, so
    // the same handler traps the next error, which On Error GoTo H inside the handler does not do.
    [Fact]
    public async Task On_Error_GoTo_minus_1_clears_Err_and_leaves_the_handler_keeping_the_mode()
    {
        await Run("""""""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub MinusOneKeepsResumeNext()
                On Error Resume Next
                Err.Raise 5
                On Error GoTo -1
                L "goto-minus-1: " & Err.Number
                Err.Raise 6
                L "goto-minus-1, then a raise is still trapped: " & Err.Number
            End Sub

            Sub MinusOneThenRearm()
                Dim n As Integer
                On Error GoTo H
                Err.Raise 11
                Exit Sub
            H:
                n = n + 1
                L "  rearm handler " & n & ": " & Err.Number
                If n = 1 Then
                    On Error GoTo -1
                    On Error GoTo H
                    Err.Raise 13
                End If
            End Sub

            Sub NoMinusOneSecondError()
                On Error GoTo H
                Err.Raise 11
                Exit Sub
            H:
                L "  handler without -1: " & Err.Number
                On Error GoTo H
                Err.Raise 13
            End Sub

            Sub Main()
                MinusOneKeepsResumeNext
                On Error Resume Next
                MinusOneThenRearm
                L "minus-1-then-rearm, caller sees: " & Err.Number
                Err.Clear
                NoMinusOneSecondError
                L "no-minus-1-second-error, caller sees: " & Err.Number
            End Sub

            Main
            """"""");

        AssertDebugLog(Lines("""
            goto-minus-1: 0
            goto-minus-1, then a raise is still trapped: 6
              rearm handler 1: 11
              rearm handler 2: 13
            minus-1-then-rearm, caller sees: 0
              handler without -1: 11
            no-minus-1-second-error, caller sees: 13
            """));
    }

    // While a handler runs, nothing in its procedure traps, whatever On Error it says: the error goes to the caller.
    // Leaving the procedure from it clears Err, whatever mode it set. Resume and GoTo -1 end it, and the mode it set
    // then applies.
    [Fact]
    public async Task A_running_handler_traps_nothing_and_leaving_it_clears_Err_whatever_it_said()
    {
        await Run("""""""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub HX1()
                On Error GoTo H
                Err.Raise 11
                Exit Sub
            H:
                Err.Number = 7
            End Sub

            Sub HX2()
                On Error GoTo H
                Err.Raise 11
                Exit Sub
            H:
                On Error Resume Next
                Err.Number = 7
            End Sub

            Sub HX3()
                On Error GoTo H
                Err.Raise 11
                Exit Sub
            H:
                On Error Resume Next
                Err.Number = 7
                Exit Sub
            End Sub

            Sub HX4()
                On Error GoTo H
                Err.Raise 11
                Exit Sub
            H:
                On Error GoTo 0
                Err.Number = 7
            End Sub

            Sub HX6()
                On Error GoTo H
                Err.Raise 11
                L "hx6 resumed"
                Err.Raise 13
                L "hx6 after 13: " & Err.Number
                Exit Sub
            H:
                L "  hx6 handler: " & Err.Number
                On Error Resume Next
                Resume Next
            End Sub

            Sub HX7()
                On Error GoTo H
                Err.Raise 11
                L "hx7 resumed"
                Err.Raise 13
                L "hx7 not reached"
                Exit Sub
            H:
                L "  hx7 handler H: " & Err.Number
                On Error GoTo H2
                Resume Next
            H2:
                L "  hx7 handler H2: " & Err.Number
            End Sub

            Sub HX8()
                On Error GoTo H
                Err.Raise 11
                Exit Sub
            H:
                On Error GoTo -1
                On Error Resume Next
                Err.Raise 13
                L "  hx8 after -1 then Resume Next, a raise is trapped: " & Err.Number
            End Sub

            Sub HX9()
                On Error Resume Next
                Err.Raise 11
                On Error GoTo H
                Err.Raise 13
                L "hx9 not reached"
                Exit Sub
            H:
                L "  hx9 handler: " & Err.Number
                Err.Raise 5
            End Sub

            Function HX10() As Long
                On Error GoTo H
                Dim z As Long
                z = 1 / z
                Exit Function
            H:
                HX10 = Err.Number
            End Function

            Sub Main()
                On Error Resume Next
                Err.Clear: HX1: L "hx1 handler sets Err.Number = 7, caller sees: " & Err.Number
                Err.Clear: HX2: L "hx2 handler On Error Resume Next then Err.Number = 7, caller sees: " & Err.Number
                Err.Clear: HX3: L "hx3 the same, by Exit Sub, caller sees: " & Err.Number
                Err.Clear: HX4: L "hx4 handler On Error GoTo 0 then Err.Number = 7, caller sees: " & Err.Number
                Err.Clear: HX6: L "hx6 caller sees: " & Err.Number
                Err.Clear: HX7: L "hx7 caller sees: " & Err.Number
                Err.Clear: HX8: L "hx8 caller sees: " & Err.Number
                Err.Clear: HX9: L "hx9 caller sees: " & Err.Number
                Err.Clear: Dim v As Long: v = HX10(): L "hx10 function returns its handler's Err.Number " & v & ", caller sees: " & Err.Number
            End Sub

            Main
            """"""");

        AssertDebugLog(Lines("""
            hx1 handler sets Err.Number = 7, caller sees: 0
            hx2 handler On Error Resume Next then Err.Number = 7, caller sees: 0
            hx3 the same, by Exit Sub, caller sees: 0
            hx4 handler On Error GoTo 0 then Err.Number = 7, caller sees: 0
              hx6 handler: 11
            hx6 resumed
            hx6 after 13: 13
            hx6 caller sees: 13
              hx7 handler H: 11
            hx7 resumed
              hx7 handler H2: 13
            hx7 caller sees: 0
              hx8 after -1 then Resume Next, a raise is trapped: 13
            hx8 caller sees: 13
              hx9 handler: 13
            hx9 caller sees: 5
            hx10 function returns its handler's Err.Number 11, caller sees: 0
            """));
    }

    // A Raise that gives no Description keeps the one already set, the empty string included, and a run-time error
    // always sets its own. Only Err.Clear makes a Raise take the standard text again. Source is left out: VB6 fills
    // it with the project's name, which HexIDE does not model.
    [Fact]
    public async Task Err_Raise_with_no_Description_keeps_the_one_already_set()
    {
        await Run("""""""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Public Function E() As String
                E = Err.Number & " [" & Err.Description & "]"
            End Function

            Sub Main()
                On Error Resume Next
                Err.Clear: Err.Raise 11: Err.Raise 13: L "standard-kept: " & E()
                Err.Clear: Err.Raise 1000, "S1", "custom": Err.Raise 5: L "custom-kept: " & E()
                Err.Clear: Err.Number = 5: Err.Raise 6: L "number-only-then-raise: " & E()
                Err.Clear: Err.Description = "manual": Err.Raise 13: L "manual-description-kept: " & E()
                Err.Clear: Err.Raise 11: Err.Raise 13, , "": L "empty-description-given: " & E()
                Err.Clear: Err.Raise 11: Err.Raise 13, , "given": L "description-given: " & E()
                Err.Clear: Err.Raise 1000, "S1", "custom": Err.Raise 5, "S2": L "source-given: " & E()
                Err.Clear: Err.Raise 11: Err.Description = "": Err.Raise 13: L "description-emptied: " & E()
                Err.Clear: Err.Raise 11: Dim x As Integer: x = 1 / 0: L "runtime-error-after-raise: " & E()
                Err.Clear: Err.Raise 1000, , "custom": x = 1 / 0: L "runtime-error-after-custom: " & E()
                Err.Clear: Err.Raise 1000, , "custom": Err.Raise 1000: L "same-number-custom-kept: " & E()
                Err.Clear: Err.Raise 5: L "plain-raise-source: " & E()
            End Sub

            Main
            """"""");

        AssertDebugLog(Lines("""
            standard-kept: 13 [Division by zero]
            custom-kept: 5 [custom]
            number-only-then-raise: 6 [Overflow]
            manual-description-kept: 13 [manual]
            empty-description-given: 13 []
            description-given: 13 [given]
            source-given: 5 [custom]
            description-emptied: 13 []
            runtime-error-after-raise: 11 [Division by zero]
            runtime-error-after-custom: 11 [Division by zero]
            same-number-custom-kept: 1000 [custom]
            plain-raise-source: 5 [Invalid procedure call or argument]
            """));
    }

    private static List<Vb6Value> Lines(string text) =>
        text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => new Vb6Value(l)).ToList();
}
