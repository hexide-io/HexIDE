# inspect_element listed a property but never its value — **CLOSED** (#342, 2026-09-08)

> **Fixed.** `dataContextMembers` now carries each property's current `value` where it can be read as text.
>
> **Only recognised types are called at all** — strings, primitives, enums, `decimal`, the date/time types,
> `Guid`, and AvaloniaEdit's `TextDocument`. Reading a property invokes a getter, which is arbitrary code;
> restricting it keeps the blast radius to reads that are ordinary. Anything else is listed exactly as
> before, with no value.
>
> **`TextDocument` earns its special case**, and is why this closes rather than half-closes: the Immediate
> window's contents are a `TextDocument`, not a string, so a scalars-only reader would have left the most
> useful text in the IDE unreadable.
>
> **Null means "not read", `""` means "empty".** Collapsing the two would make *no output yet* and *cannot
> see the output* identical, which is the failure this entry is about. A throwing getter yields null rather
> than failing the whole inspection, and a value over 4000 characters is truncated with a marker saying so —
> `Debug.Print` output has no natural limit.
>
> Measured live, with the Immediate pane only ~4 lines tall:
>
> ```
> ImmediateToolViewModel.Document  → "before
clean run
"   (both runs, not the visible strip)
> RuntimeErrorViewModel.ErrorText  → "Run-time error '11': Division by zero, at 1 / 0"
> ```
>

**Symptom.** `dataContextMembers` gives each member's name, type and `canWrite`, but no current value. Two
cases hit in one session: `ImmediateToolViewModel.Document` (the Immediate window's contents) and
`RuntimeErrorViewModel.ErrorText` (a runtime error message). Both had to be read by screenshotting and
squinting, and the Immediate window's buffer scrolls, so output beyond the visible ~4 lines is unreachable
without resizing the pane.

**Consequence.** Any assertion about text the IDE produced — program output, an error message, a status
line — degrades from a structured check to reading a PNG. That is slower, and it silently caps at whatever
the pane happens to show.

**Workaround.** Keep program output to one line per run so it fits the visible strip, and restart the IDE
between runs when the buffer needs clearing (there is no clear-Immediate action). Note that setting the
containing `ToolDock`'s `Proportion` via `interact set_property` reports success but does not re-run the
layout, so it does not actually enlarge the pane.

**Suggested fix.** Return scalar property values (string/number/bool) alongside the member list. A
`get_immediate_output` returning the Immediate buffer as text would remove the whole class of workaround.
