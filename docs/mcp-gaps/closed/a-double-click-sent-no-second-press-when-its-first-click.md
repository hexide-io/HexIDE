# A double click sent no second press when its first click replaced what it landed on — **CLOSED** (#2324, 2026-10-27)

**Symptom (2026-10-27, Windows, #2309's live check).** `interact` `click` with `double` on a running Toolbar's
`Button[New]` answered `…; the second click was not sent because the target is hidden or closed, or detached`. The
same happened on `Window/Custom[Toolbar1]` with `16,22 double`, though the bar is never replaced. So the Toolbar's
measured double-click couldn't be checked live: a second ButtonClick, and no DblClick (#1088).

**Cause.** Before the second press, `SecondPress` required the element the first press landed on to be still attached.
A Toolbar rebuilds its buttons on each release (`VBToolbar.Release` → `RefreshButtons`), so that element was always
gone.

**Workaround.** None. Two single clicks are not a double click.

**Fixed (#2324).** The second press hit-tests the first press's point again and goes to whatever takes the pointer
there, with a click count of 2, as a person's second press lands. It is refused only when the window is hidden or
closed, or nothing is at the point. When it reaches a different element from the first, the reply says so and names
it. The first live run of the fix still missed about one time in three: the second press found the bar's own presenter
though the new button was laid out, attached and composited. Hit-testing reads the composition the render thread
holds, which lagged the layout. So the second press now waits for the window's next frame and for the render thread to
process a composition batch, each bounded at 250 ms. `DoubleClickReplacedTests` guards the hit-test, and fails with the
old refusal back. The headless compositor doesn't lag, so the wait is guarded only by the live runs below.

**Live (2026-10-27, a Debug build of the fix cherry-picked onto #2309's branch on Windows at 150%, headless `claude -p`
through the hexide tools).** The project was the Toolbar corpus form, without its ImageList (#2309's branch predates
it), with handlers appending ` BC:<key>` for each ButtonClick and ` DC` for each DblClick to the form's caption.

- Without the frame and batch wait, three bar double-clicks in a row added one, two, then one ButtonClick. Diagnostics
  showed the misses' hit chain going straight to the bar's presenter.
- With them, six double-clicks on `Window/Custom[Toolbar1]` at `16,22` and two on `Button[New]` each added two
  ButtonClicks and no DblClick, as #1088 measured. Each reply said the second press went to the new button's
  `AccessText`, at `Window/Custom[Toolbar1]/Button[New]`.
