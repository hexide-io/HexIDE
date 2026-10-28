# No tool types as a person does, so anything driven by TextInput is out of reach — **CLOSED** (#943, 2026-10-20)

**Symptom.** (Linux, openSUSE Leap 16 under XWayland, Debug build of main at b15c7cf, 2026-10-13.)

```
type_text {"target":"Window/Custom[MainView]/Pane[DockControl]/Pane[Root dock]/List[PART_ItemsControl]/List[#0]/Pane[#0]/Custom[Root]/None[TextEditor]","text":"Ms"}
→ didChange seq 151, and no completion request
```

A person typing `M` triggers one. `type_text` inserts through the document API by design (#649), and `press_key`
raises only KeyDown and KeyUp, which AvaloniaEdit does not turn into text. Nothing on the surface raises
TextInput, so these are out of reach: the list that opens as you type, parameter info on `(` and `,` and its
closing on `)`, the list closing on a character that cannot be part of a name, and the edit-while-running reset
prompt for a printable character. Both tools do what they say; this is a missing capability, as #829's was.
Cross-platform by code reading.

**Workaround.** For completion and parameter info, the explicit commands: `press_key` with Ctrl+Space, Ctrl+J or
Ctrl+Shift+I on the TextArea. A list opened that way cannot be seen either (next entry). There is no workaround
for the typed-character paths.

**Suggested fix.** A keyboard typing mode, a `type_keys` tool or `mode:"keyboard"` on `type_text`, that raises
KeyDown, TextInput and KeyUp per character on the focused TextArea and reports mechanism "keyboard".
[#943](https://github.com/hexide-io/HexIDE/issues/943).

> **Fixed.** `type_text` takes `mode:"keyboard"`, which types as a person does, a key at a time: KeyDown, TextInput and
> KeyUp per character, with a line break as Enter and a tab as Tab. Each key goes where the keyboard sends it, to the
> focused control, with key bindings tried first as Avalonia's keyboard device tries them, so a Tab that moves focus
> sends the rest to where it went and an Enter bound to a command runs it; `press_key` now delivers its key the same
> way. After each key the code window's completion and parameter-info requests are waited for, up to 2 s, so the list a
> letter asks for is open before the next key, as a person sees it before typing on; a slower server is named in the
> reply. So what typing drives runs: the completion request on a letter, parameter info on `(` and `,`, auto-indent and
> the block a line break closes. The typing stops, and the reply says after how many keys, at a key the header or a
> member's Attribute lines refuse, when a dialog opens over the window, when focus leaves it, or on cancellation. The
> default `document` mode is unchanged. Guarded by `TypeKeysTests`.
