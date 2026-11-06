# An action that started a DoEvents loop was replied to at its first DoEvents, with no note (#1254)

The wait for a running program (#1232) counts a handler parked at `DoEvents` as the program waiting for input, which
#1233 settled for an action taken under a DoEvents loop already running. It counted the same for a loop the action itself
started: a Click that loops with a `DoEvents` per pass got `invoked Button 'Command1'` back at its first `DoEvents`, with
no note, while the form was half way through. The same loop without `DoEvents` was waited for.

> **Fixed.** The pump marks each walk that parks at `DoEvents` as a loop, and keeps every walk started and not finished.
> A reply records the loops running before its action, and counts the program idle only while every walk parked at
> `DoEvents` was one of them. A loop the action found running is still the program's message loop, wherever the action
> finds it, between two `DoEvents` included. A loop the action started is waited for as one with no `DoEvents` is, up to
> the limit. So is one set going by answering a MsgBox, or closing a modal form, its handler waited at: waiting on a
> window now clears its earlier loop mark (#1318). The original fix covered only handlers with no DoEvents before
> the window wait. A Timer's tick is no action's work, so its loop does not hold a reply. A loop outlasting
> the limit gets the usual note, naming only what the action set off: "(the program was still running Command1_Click 3s
> later, so the form may still change)". Walks, not handler names, so a click that re-enters a handler parked under the
> same name is told apart from it. A `Show vbModal` park is unchanged, so an action that opens a modal form still
> replies at once. The wait note in the `interact`, `type_text` and `press_key` descriptions says a loop running before
> the action, or a Timer's, is not waited for. Guarded by `ProgramWaitTests`, with no time slice so that `DoEvents` is
> the loop's only yield.

> **Live, through the hexide tools (2026-10-27, Windows at 150%, a Debug build of main at f9b5649c, headless
> `claude -p`, #1330).** The review of #1294 said it was checked live without saying how, so it was checked again, on a running
> form whose Command1_Click counts Text1 from 1 to 200 with a `DoEvents` each pass, then sets it to "done". `interact
> {"target":"Window/Button[Command1]","action":"invoke","window":"Form1"}` answered `invoked Button 'Loop'`, with no
> note, and `inspect_element` on `Window/Edit[Text1]` straight after gave `"value":"done"`.
