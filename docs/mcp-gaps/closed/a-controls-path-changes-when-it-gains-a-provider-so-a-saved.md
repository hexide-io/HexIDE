# A control's path changes when it gains a provider, so a saved path stops resolving as text grows — **CLOSED** (#1436, 2026-10-23)

**Symptom (2026-10-22, #1386, Windows).** While paused, `dump_visual_tree {"window":"ide","root":"<Imm>","maxDepth":4,
"interactiveOnly":false}`, with `<Imm>` the Immediate window's `.../TitleBar[Immediate]/Pane[Immediate]/Custom`, gave
`<Imm>/Pane[PART_ScrollViewer]/None` for its text area, and `type_text` on that path worked for three lines. The fourth
`type_text` on the same path answered `no child matching 'Pane[PART_ScrollViewer]' under '<Imm>'`. A fresh dump showed
`<Imm>/None[Editor]`, the TextEditor, now with `providers: ["scroll"]`, holding the scroll viewer. The control view hides a
`None` node with no provider and no focus, and the editor stopped qualifying once its text overflowed.

**Workaround.** Dump again and use the new path; `<Imm>/None[Editor]` takes `type_text` and `press_key` from then on.

**Suggested fix.** Decide whether a node is hidden on what its content can't change: leave `scroll` out of it, or never
hide a node with an x:Name or AutomationId. And when a segment misses, say which node has appeared above it.
[#1436](https://github.com/hexide-io/HexIDE/issues/1436).

**Resolution.** Transparency ignores scrolling, including the synthetic scroll capability a TextEditor gains on
content overflow. An x:Name alone still collapses; an AutomationId still keeps a wrapper. Range capability is
read from the peer independently of current usable bounds, so `rangeValue` appearing cannot insert a segment.
When a missed segment and its remaining path resolve under exactly one immediate control-view child, the refusal
names that wrapper and the complete corrected path, without acting on it. The tool description states the rule.

**Verification.** Headless production-view census: code editor, Immediate window, ListBox, a populated Properties
grid with its production templates, and a running VB6 form. Before the change, named and unnamed TextEditor
paths inserted a `None` segment on overflow, and the wrapper-miss refusal omitted the correction; those regression
tests failed. The census moved only the overflowed Immediate editor: `None[Editor]` is collapsed, and its 16
emitted descendants lose that segment, matching their paths before overflow. No paths moved on the other four
surfaces; the code editor remains a node because its context menu advertises `expandCollapse`. The range regression
also checks a `None` peer across an unusable-to-usable range. Integration suite: 2,063 passed. IDE unit suite:
3,273 passed, 28 environment-dependent skips. ToolDescription tests: 31 passed. Debug desktop build: zero warnings.
Tree hygiene passed. Live MCP verification was not run in this task, which prohibited MCP calls.
