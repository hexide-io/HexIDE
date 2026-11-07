# `interact` `click` on a window that has just opened says nothing takes the pointer — **CLOSED** (#1065, 2026-10-14)

> **Fixed.** `click` now waits, up to a second, for the target's window to draw its next frame before it hit-tests. It
> asks the window's compositor for its next batch and awaits that batch's `Rendered`. The other verbs don't wait.
>
> **The cause was the frame, not the layout.** The entry below guessed "before the new window's first layout and
> render", and its suggested fix led with a layout pass. Its own evidence ruled layout out: the button had its bounds,
> and the point fell inside them. A press is hit-tested as a real one is, against what the compositor last *drew*, and a
> window opened a moment ago has drawn nothing. A layout pass alone would have fixed nothing.
>
> Waiting for the frame also covers a case nobody had hit. A control the program has just moved would have been pressed
> where the last frame showed it, not where its layout now puts it.
>
> **If the window draws nothing within the wait,** as a minimised one may not, and the hit test then finds nothing, the
> refusal adds the cause that applied: "or its window, which drew no frame in the 1000 ms the click waited, has nothing
> drawn there yet, since a press is hit-tested against what was drawn: try again once it shows".
>
> Pinned in `InteractClickTests`. A window shown and laid out in the same dispatcher job is refused by the synchronous
> path, as #1065 saw, and clicked by the waiting one. A zero wait gives the new refusal, and `invoke` doesn't wait at all.


**Symptom.** (Windows 11, Debug build of #1063's branch at c69ad5b, 2026-10-14.) A running program's Form1 button
shows Form2 modally. The click on Form2 came straight after the one that opened it, in the same turn:

```
interact {"target":"Window/Button[Show Form2 modal]","action":"click","window":"Form1"}
→ {"success":true,"mechanism":"pointer","detail":"clicked the left button at (60.7, 12.7) … landing on its part 'AccessText'"}
interact {"target":"Window/Button[End]","action":"click","window":"Form2"}
→ {"success":false,"mechanism":"pointer","error":"nothing takes the pointer at (40.7, 12.7) on 'VBCommandButton', which is
   (48.7, 84.7) in the window, so nothing was pressed: the point is outside the target, or on a part of it that is not hit-tested"}
take_snapshot {"window":"Form2"}                      → the End button drawn at that point
inspect_element {"target":"Window/Button[End]","window":"Form2"}
→ {… "isEnabled":true, "boundingRect":[8,72,81.33,25.33] …}
interact {"target":"Window/Button[End]","action":"click","window":"Form2"}
→ {"success":true,"mechanism":"pointer","detail":"clicked the left button at (40.7, 12.7) … landing on its part 'AccessText'"}
```

The point was inside the button by the tool's own reckoning, and the same call worked the second time. The likely cause
is that the hit test ran before the new window's first layout and render, when nothing in it can take a hit yet. The
error gives the two causes a caller would rule out by looking, and not the one that applied, so a caller that had never
seen this would conclude the button was covered or broken.

**Workaround.** Take a `take_snapshot` or `inspect_element` of the new window first, then click. Retrying the click also
works.

**Suggested fix.** Before `InputHitTest`, have `click` make sure the target's top level has had a layout pass, and wait
for its first render if it has had none. Where the hit test still finds nothing, say whether the window has been laid out
and rendered yet.
[#1065](https://github.com/hexide-io/HexIDE/issues/1065).
