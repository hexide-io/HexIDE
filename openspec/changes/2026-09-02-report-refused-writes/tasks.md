# Tasks

## 1. The gate above the picker
- [x] 1.1 Split `SaveModule` into `SaveModule` (reports) and `SaveModuleCore` (batch-safe)
- [x] 1.2 Hoist the `CanSaveFaithfully` gate above the file picker
- [x] 1.3 Point the `SaveProject` and `SaveSelected` batches at `SaveModuleCore`

## 2. Report the outcome
- [x] 2.1 `SaveFormCore` and `SaveForm` return whether the file was written
- [x] 2.2 `SaveModuleCore` and `SaveModule` do the same
- [x] 2.3 Both MCP write tools report a refusal rather than `MutateResult(true, null)`

## 3. Cover the .ctl path
- [x] 3.1 A UserControl the gate refuses is left untouched by a project save
- [x] 3.2 Save As on one refuses before asking for a destination
- [x] 3.3 A lone refusal is reported when it happens
- [x] 3.4 The refusal reaches the caller as a return value
- [x] 3.5 A faithful UserControl still saves — the over-reach guard
- [x] 3.6 Mutation-test: remove the hoisted gate, confirm 3.1–3.4 fail and 3.5 does not

## 4. Verify
- [x] 4.1 Runtime, IDE and Integration suites green
- [x] 4.2 Release build clean — the MCP change is `#if DEBUG` and must not reach it
