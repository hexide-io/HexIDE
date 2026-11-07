# A running form's menu items are named with VB6's `&` — **CLOSED** (#1128, 2026-10-20)

> **Fixed.** A run-time menu item is named by its caption as it reads, `&` removed and `&&` read as one `&`, and carries
> its VB6 name as its AutomationId, so the Game menu dumps as `MenuItem[mnuGame]` named "Game" and is reached as
> `#mnuGame` or `MenuItem[Game]`. A path written with the `&`, `MenuItem[&Game]`, no longer matches. Fixed with #954;
> guarded by `RunningFormNamesTests`.

**Symptom.** Measured on 2026-10-17, driving the Minesweeper demo's menued version (#1123, caab5bdd):

```
dump_visual_tree {}
→ {"path":"Window/Menu/MenuItem[&Game]","controlType":"MenuItem","name":"&Game",...}
interact {"target":"Window/Menu/MenuItem[&Game]","action":"expand"}
→ ... {"path":"Window/Menu/MenuItem[&Game]/Pane/MenuItem[E&xit]","name":"E&xit",...}
```

The menu draws "Game" and "Exit", with the access key underlined. The name and the path carry the raw caption, so a
caller must know VB6's caption syntax to address what it can see.

**Workaround.** Put the `&` where the caption has it: `MenuItem[&Game]`, `MenuItem[E&xit]`.

**Suggested fix.** Name each run-time menu item by its caption as drawn, `&` removed and `&&` read as one `&`. The IDE's
own menus had the same fault with `_` (#578). [#1128](https://github.com/hexide-io/HexIDE/issues/1128).
