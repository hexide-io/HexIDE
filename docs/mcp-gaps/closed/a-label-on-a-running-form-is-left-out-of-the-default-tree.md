# A Label on a running form is left out of the default tree — **CLOSED** (#1076, 2026-10-20)

**Symptom.** Measured on 2026-10-15, driving the Battleship demo (#1071), whose board is 25 Labels and a status Label:

```
dump_visual_tree {"interactiveOnly":false,"maxDepth":20}
→ {"window":"Battleship - Admiral","root":{"path":"Window",...,"children":[{"path":"Window/Menu",...,"isHidden":true,
   "children":[]}]}}
```

None of the 26 Labels is there, with `interactiveOnly` false (the default is true). A pointer reaches them, so they
are on screen: `interact {"target":"Window","action":"click","value":"20,20"}` replies "landing on its part 'VBLabel'".
Nothing in the reply says controls were left out.

**Narrowed by #954.** A Label now carries its VB6 name and is named by its caption, so `Window/#lblStatus` reaches it,
`inspect_element` reads its caption as the node's name, and a dump with `interactiveOnly` false shows it as
`Text[lblStatus]`. What remains: the default dump, `interactiveOnly` true, still leaves it out, because a Label has no
automation provider and cannot take focus, though a program may handle its Click.

**Workaround.** Pass `interactiveOnly: false`, or target `Window/#lblStatus` by the name in the code.

**Suggested fix.** Count a run-time Label as interactive in the default dump, or every node with a run-time id: the
caption is often what a program reports through. [#1076](https://github.com/hexide-io/HexIDE/issues/1076).

> **Fixed.** The default dump keeps every control a running program names, a Label among them, though it has no
> automation provider and cannot take focus: the program may handle its Click, and its caption is often what the program
> reports through. It shows as `Text[lblStatus]`, named by its caption, with the containers above it. Belonging to a
> running program is decided as its event dispatch decides it, by logical ancestry, so a control in an open menu counts;
> the form designer's controls, named the same way, do not, and a Timer and a CommonDialog, which never show, are left
> out. On a form of many Labels the default now keeps nearly everything, so `root` scopes it. Guarded by
> `RunningFormNamesTests`.
