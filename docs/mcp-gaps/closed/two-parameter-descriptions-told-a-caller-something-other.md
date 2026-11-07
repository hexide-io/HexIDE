# Two parameter descriptions told a caller something other than what was meant (#1258)

`type_text`'s `mode` was written as a regular C# literal, so its `\n` and `\t` reached a caller as a line feed and a tab.
The sentence that tells a caller how keyboard mode types several lines read "per character, (a line break) as Enter and
(a gap) as Tab". `interact`'s `close` said a MsgBox with no Cancel may refuse. An OK-only box has no Cancel, yet its
close box answers vbOK; only Yes/No and Abort/Retry/Ignore boxes refuse, as the oracle measured.

> **Fixed.** `mode` names the two characters. `close` names the boxes that refuse, Yes/No and Abort/Retry/Ignore, and
> that a form's QueryUnload may, in fewer characters than before, since the `action` description sits just under the
> 2048 a client delivers. CLAUDE.md gives the whole list, a modal form over the window included.
>
> `ToolDescriptionTextTests` holds every tool and parameter description to no control, format, line or paragraph
> separator character, except a blank line between paragraphs, which five descriptions use. It fails on the text this
> replaces, and pins both corrected sentences.
>
> Its review found that `ToolSource` stopped reading a description at the first piece that was not a literal. Three
> tools end with `+ ProgramWaitNote`, so 361 characters of each were neither measured against the length cap nor
> checked. It now reads a `const string` in the chain, and reports a chain that goes on past what it can read.
