# `add_watch` turns an unrecognised `watchType` into `Expression` without saying so (#546) — **CLOSED** (#556, 2026-09-22)

> **Closed (#556).** An unrecognised non-empty `watchType` is refused and nothing is added. The error names
> the accepted values: `'BreakWhenTru' is not a watch type, so no watch was added. Use Expression (the
> default), BreakWhenTrue or BreakWhenChanged.` Null, empty and whitespace still mean `Expression`. The
> tool now carries `[DescribesEnum(typeof(WatchType))]`, so its description is guarded against the enum.

**Symptom.** From reading `HexIdeTools.AddWatchAsync`: `add_watch(expression: "x > 5", watchType:
"BreakWhenTru")` falls through the `switch` to `WatchType.Expression`, adds a watch that will never break,
and replies with the watch list as if the call had done what was asked.

**Workaround.** Read `watchType` back from the reply's list after adding.

**Fix.** Refuse an unrecognised non-empty value and name the accepted spellings.
