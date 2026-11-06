# No call double-clicks a running form — **CLOSED** (#1389, 2026-10-24)

**Symptom (2026-10-22, #1031, Windows).** A running form's `DblClick` comes from a double click's second press, told by
the press's click count. `interact {"target":"<a Label on a running form>","action":"double_click"}` raises Avalonia's
`DoubleTapped` and selects the target, with no pointer press, and answers `success: true`; the program's `Label1_DblClick`
never runs, and nothing in the reply says no press was sent. Two `interact {"action":"click"}` calls in a row each press
with a click count of 1, and an MCP round trip puts them past the 500 ms double-click time anyway.

**Workaround.** None through the surface. `MouseEventTests` drives a double click headlessly, with real pointer input.

**Suggested fix.** Accept `double` in `click`'s value, as in `10,5 double`, raising press, release, press and release
with the second press's click count 2. Have `double_click`'s reply say it raised `DoubleTapped` and no press.
[#1389](https://github.com/hexide-io/HexIDE/issues/1389).

**Fixed.** `interact {"target":"<a running form>","action":"click","value":"10,5 double"}` sends press,
release, press and release, with the second press carrying ClickCount 2; `10,5 right double` uses the right button,
and plain `double` uses the centre. Between clicks it waits for the first release's program work. If the target
has hidden, closed or detached, the reply says the second click was not sent. The reply names the double click,
button and coordinates. `double_click` keeps its selection and DoubleTapped behavior for the Project Explorer;
its reply now names DoubleTapped, says no pointer press was sent, and points to `click` with `double` for DblClick.
Running-form integration tests pin the vb6.exe event order already recorded in the oracle and #1031's tests.
