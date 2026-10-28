# A running form's controls carry no VB6 name, and a `&` caption is named with `_` — **CLOSED** (#954, 2026-10-20)

> **Fixed.** Every control on a running form carries the name its code uses as its AutomationId: `Text1`, or
> `Command1(0)` for an element of a control array, including one `Load` adds at run time. A tree shows it as the
> path's discriminator, `Window/Edit[Text1]`, and a tool reaches it as `Window/#Text1` at any depth. A CommandButton,
> CheckBox, OptionButton or Frame is named by its caption as it reads: `&OK` is "OK", `E&xit` is "Exit", `Tom && Jerry`
> is "Tom & Jerry", so `Button[Exit]` reaches it. A Label is too, its `&` dropped only where UseMnemonic marks a key.
> The rule is Windows' documented one for prefix characters, not measured on the oracle (#920 owes that); a trailing
> `&`, which Microsoft does not document, is kept.
>
> The id makes a node of a container the tree used to fold away, so the tree now follows the form's containment: a
> control in a Frame is `Window/Group[Frame1]/Button[cmdOK]`, in a PictureBox `Window/Pane[Picture1]/...`, where both
> used to sit directly under `Window`. A Label is `Text[Label1]` and an Image `Image[Image1]`; a Timer, Shape, Line or
> CommonDialog has no type of its own and shows as `None[Timer1]`. Designer controls are left as they were (#526). Not
> covered: the constituent controls of a UserControl, which are built without the loader. Guarded by
> `RunningFormNamesTests`.

**Symptom.** Found on Linux and read in code on Windows (#954):

```
dump_visual_tree {"window":"auto"}
→ Window/Button[_OK]          (Command1, caption "&OK")
  Window/Edit, Window/List    (Text0, List0: no name)
```

No node carried `Command1` or `Text0`, so a caller holding names from the code or from `get_form_controls` had no
bridge to the running form. Seen again in #1232's live check: the agent had to guess that `Window/Edit` was `txtResult`.

**Cause.** The VB6 name was kept only in HexIDE's attached `VBProps.Name`, and the button's raw caption reached the
automation peer through the `AccessText` the template draws, with `&` turned into Avalonia's `_` marker.
