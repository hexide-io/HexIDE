# A save the code page or the serializer refuses held the tool's reply on a dialog — **CLOSED** (#751, by #729, 2026-10-12)

> **Fixed.** `set_file_content`, `set_control_property`, `add_control` and `add_file` now save with
> `interactive: false`. No dialog is shown. A save the code page refuses comes back in the reply, naming
> the document, the code page and the first character it cannot hold, and the change stays in the IDE. A
> save HexIDE refuses because it cannot reproduce the file comes back as the existing "cannot reproduce
> this file faithfully" reply, and is no longer reported again by the next save.
> `AutomationSaveRefusalTests` fails the build if a tool calls a save without `interactive: false`.
>
> Verified live on 2026-10-12, in a Debug build of #729 with `--ansi-code-page 1252`, on a project of
> ASCII files. Replies are shown decoded; the server escapes `'`, `+` and non-ASCII characters in its JSON.
>
> ```
> set_file_content {"name":"Module1","content":"Public Sub One()\n    Debug.Print \"Go → there\"\nEnd Sub\n"}
> → {"success":false,"error":"The new code is in the IDE but was not written: Module1 is kept in code page 1252,
>    which cannot hold '→' (U+2192). Nothing was written. HexIDE keeps a file in the code page it was read or
>    created in rather than convert it, so use characters that code page holds."}
> set_control_property {"formName":"Form1","controlName":"Command1","property":"Caption","value":"Next →"}
> → {"success":false,"error":"'Caption' is set in the IDE but was not written: Form1 is kept in code page 1252,
>    which cannot hold '→' (U+2192). …"}
> add_control {"formName":"Form1","type":"Label","x":20,"y":80,"width":80,"height":20}   (caption still unsaved)
> → {"success":false,"controlName":"Label0","error":"'Label0' was added to the designer but NOT written: Form1 is
>    kept in code page 1252, which cannot hold '→' (U+2192). …"}
> add_file {"name":"Ж1","type":"Module"}
> → {"success":false,"error":"'Ж1' was not added: Ж1 is kept in code page 1252, which cannot hold 'Ж' (U+0416). …"}
> ```
>
> After each call, `dump_visual_tree(maxDepth: 1, interactiveOnly: false)` reported the main window, so no
> dialog was open. The SHA-256 of every file in the project was unchanged, and `add_file` created no file.
> `get_file_content` showed the refused code still in the IDE, with `hasUnsavedChanges: true`.
>
> **The wording above was revised after review, in the same PR, and measured again live the same day.** Each
> reply now quotes the line of the file that holds the character, because the character can be in a property
> no read tool shows. The prefixes no longer name the new input as the cause, since the save writes the whole
> file. `add_control`'s reply ends by naming the call that writes the form once that text is changed. The
> replies are built from `SourceEncodingRefusal.Message`, which `Vb6TextFileTests` pins.
>
> Measured in a Debug build of #729 with main merged (`e999170`), without `--ansi-code-page`, the host's being
> 1252, on a project of ASCII files. Replies decoded as before:
>
> ```
> set_file_content {"name":"Module1","content":"Sub Main()\n    MsgBox \"Привет\"\nEnd Sub\n"}
> → {"success":false,"error":"The new code is in the IDE, but the file was not written. Module1 is kept in code
>    page 1252, which cannot hold 'П' (U+041F), found in the line `MsgBox "Привет"`. Nothing was written.
>    HexIDE keeps a file in the code page it was read or created in rather than convert it, so use characters
>    that code page holds, and where that line names a file or folder, a name it can hold."}
> set_control_property {"formName":"Form1","controlName":"Form1","property":"Caption","value":"Привет"}
> → {"success":false,"error":"'Caption' is set in the IDE, but the file was not written. Form1 is kept in code
>    page 1252, which cannot hold 'П' (U+041F), found in the line `Caption         =   "Привет"`. …"}
> add_control {"formName":"Form1","type":"CommandButton","x":20,"y":20,"width":81,"height":33}   (caption still unsaved)
> → {"success":false,"controlName":"Command0","error":"'Command0' was added to the designer, but the file was
>    NOT written. Form1 is kept in code page 1252, … found in the line `Caption         =   "Привет"`. …
>    Once that text is changed, set_control_property or set_file_content writes the file, 'Command0' included."}
> add_file {"name":"Ж1","type":"Module"}
> → {"success":false,"error":"'Ж1' was not added: Ж1 is kept in code page 1252, which cannot hold 'Ж' (U+0416),
>    found in the line `Attribute VB_Name = "Ж1"`. …"}
> ```
>
> `add_control`'s last sentence holds. `set_control_property` then set the caption back to `Hello` and answered
> success, and `Form1.frm` on disk held `Caption = "Hello"` and `Command0`. `add_file` created no file, and
> `dump_visual_tree(maxDepth: 1, interactiveOnly: false)` after it reported the main window.
>
> The warning a person sees was measured in the same run. With `Module1`'s refused code still in the IDE,
> `invoke_menu_item {"path":"File/Save Project1"}` opened one message box, read with
> `dump_visual_tree(interactiveOnly: false)` and checked in a `take_snapshot`, nothing clipped:
>
> ```
> Nothing was written. Module1 is kept in code page 1252, which cannot hold 'П' (U+041F). It is in this line:
>
> MsgBox "Привет"
>
> HexIDE keeps each file in the code page it was read or created in, rather than converting it. Change that
> text to characters the code page holds, or, where the line names a file or folder, choose a name it can hold.
> Then try again. Your changes are still open in the IDE.
> ```
>
> `Module1.bas` on disk was unchanged afterwards.

**Symptom.** Each of the three write tools saves through `ProjectService.SaveForm` or `SaveModule` on the UI
thread, and awaits the result. A refused save reported itself in a message box inside that save:

- `Str.Dialog.SourceEncoding.SaveFailed` when the code page could not hold a character;
- `Str.Dialog.UnfaithfulSave.Body.One` when HexIDE could not reproduce the file.

Nothing answers a message box for an automation caller (`ScriptedFileDialogs` covers the two file pickers
only), so the request stayed open until a person closed the dialog. When the reply did come, the encoding case
read `The new code is in the IDE but was not written: Source encoding cannot represent the edit.` It named no
document, code page or character.

`add_file` had the encoding case in another form. A name the code page cannot hold reached the encoder
unguarded. The reply was .NET's own message, naming the character's escape and an index into the file, and
by then the project's scratch folder had already been created.

**Not the same as #681.** That one is about tools acting behind a dialog that is already open. Here the tool
itself opened the dialog, and then waited on it.

**Still not covered.** A save started by `invoke_menu_item`, `press_key` or `interact` is a UI command. Its
reply says `Invoked` before the save finishes, and a refusal is shown to a person as a dialog, as it is for
a click.
