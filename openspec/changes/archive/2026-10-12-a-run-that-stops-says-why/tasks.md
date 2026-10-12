# Tasks

## 1. The run window and the runner

- [x] 1.1 The run-window boundary raises an exception that is not a VB6 error as a fault, rather than only
  logging it. A cancellation is not a fault.
- [x] 1.2 A startup-form run listens for faults while it is live, ends on the first, and reports it once torn
  down. A fault raised while the form is still being shown is acted on as soon as the run exists.
- [x] 1.3 Sub Main builds its interpreter inside the run's guard. A load failure is reported as a start that
  never became a run, and anything after that as a run that ended.
- [x] 1.4 One text for each kind: an unimplemented construct, any other fault, a VB6 run-time error with its
  statement, a compile error.
- [x] 1.5 The IDE shows and records both reports through the existing runtime-error dialog.
- [x] 1.6 Remove `VBMDIWindow`, which discarded its own run's task and which nothing instantiated.

## 2. Tests

- [x] 2.1 Neither `On Error Resume Next` nor `On Error GoTo` traps an unimplemented statement, on either of the
  interpreter's trap sites, with a VB6 error in the same place as the control.
- [x] 2.2 The run-window boundary raises a fault for an unimplemented statement and for any other exception, and
  not for a VB6 error or a cancellation.
- [x] 2.3 From the start call, with a real form and a real click: a fault in a click handler and in Form_Load
  each end the run, close the form and report once; a VB6 error leaves the run going; a fault after the run has
  ended reports nothing.
- [x] 2.4 From the start call on Sub Main: a fault ends the run and reports it, a load failure is a start
  failure that leaves nothing running, and VB6's own errors reach the same report instead of a message box.
- [x] 2.5 From the start call to the view model: the error is recorded and the runtime-error dialog shown.

## 3. Record

- [x] 3.1 `interpreter-core` gains the two scenarios.
- [x] 3.2 `MISSING_LANGUAGE.md`, `interpreter-gaps.md` and `MISSING_FEATURES.md` say what a Dies row now does.
- [x] 3.3 The new key is in English and every shipped pack.
- [x] 3.4 CHANGELOG.
