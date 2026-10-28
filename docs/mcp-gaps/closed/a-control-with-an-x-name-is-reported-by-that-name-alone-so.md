# A control with an x:Name is reported by that name alone, so the Tools menu reads `ToolsMenuItem`

**Symptom.** (Linux, openSUSE Leap 16 under XWayland, Debug build of main at b15c7cf, 2026-10-13.)

```
dump_visual_tree {"root":"Window/Custom[MainView]/None[#0]","maxDepth":4}
→ … MenuItem[File], MenuItem[Edit], MenuItem[View], … MenuItem[ToolsMenuItem], MenuItem[AddInsMenuItem] …
interact {"target":"Window/Custom[MainView]/None[#0]/Menu[MenuBar]/MenuItem[ToolsMenuItem]","action":"expand"}
→ opened menu 'ToolsMenuItem'
```

An x:Name wins over the label a person sees, in the node's `name`, in its path and in reply text, and no field
carries "Tools". A caller searching the dump for the text in a snapshot finds nothing. Four menu items have an
x:Name: `ToolsMenuItem`, `LanguageServersMenuItem`, `ProtocolInspectorMenuItem` and `AddInsMenuItem`. x:Name first
is deliberate, because it does not change with the IDE language, so the fix adds rather than swaps.
Cross-platform by code reading.

**Workaround.** Address the item by the x:Name the dump shows. `MenuItem[Tools]` should also resolve, through the
label fallback, but that is read in code and was not tried.

**Suggested fix.** A `label` field on a node whenever the peer's label differs from `name`, reply text that uses
it (`opened menu 'Tools' (ToolsMenuItem)`), and a description that says a segment may be addressed by either.
[#948](https://github.com/hexide-io/HexIDE/issues/948).


**Fixed (#948).** Dumps and inspection now report the peer text as `label` when it differs from `name`.
Paths keep x:Name and accept the label too; discovery searches include it, and interaction replies use it.
Four headless regression cases cover these surfaces and failed before the fix. Live verification of the
IDE menu remains for the supervising session.
