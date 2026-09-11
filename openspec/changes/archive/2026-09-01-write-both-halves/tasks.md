# Tasks

## 1. One ruler
- [x] 1.1 Decide in `SerializeFormToFile` before either write, on `CanSaveFaithfully`; return whether it wrote
- [x] 1.2 Delete `WouldLoseBlobs` and the companion write's second refusal
- [x] 1.3 Move the `AbsolutePath` assignment below the decision, for forms and for modules
- [x] 1.4 Write the companion before the text

## 2. Do not destroy what the save cannot account for
- [x] 2.1 Record at load how many offsets the designer file cited
- [x] 2.2 Delete a companion only when the form actually cited one
- [x] 2.3 Deduplicate blobs by reference so one record cited twice is written once

## 3. Callers
- [x] 3.1 Refusals banked by the caller, never by the write helper
- [x] 3.2 `MakeProjectInternal` aborts rather than packaging a `.vbp` naming an unwritten form
- [x] 3.3 `MakeProjectInternal` restores form paths in a `finally` — it now has an early exit
- [x] 3.4 `SaveProjectToDirectory` reports rather than banking; it has no drain of its own
- [x] 3.5 Batch drains in `try/finally` so a dismissed picker cannot leak refusals into the next save

## 4. Tests
- [x] 4.1 The pair on disk is one `Serialize` result, after dropping a blob-bearing control
- [x] 4.2 A form that reproduces its companion is never denied the write
- [x] 4.3 A companion nothing cites is left alone
- [x] 4.4 One record cited twice is written once
- [x] 4.5 Mutation-test: restore the old order and refusal, confirm 4.1 and 4.2 fail
- [x] 4.6 Point the companion tests at the corpus variable that is actually set — they passed vacuously

## 5. Verify
- [x] 5.1 Runtime, IDE and Integration suites green; Release build clean
- [x] 5.2 Round-trip corpus unchanged at 21/22
