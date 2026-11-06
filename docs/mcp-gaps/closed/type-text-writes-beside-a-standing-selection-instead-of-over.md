# `type_text` writes beside a standing selection instead of over it — **CLOSED** (#1040, 2026-10-20)

**Symptom.** Found by the review of #273 task 4.5, which reproduced it. The calls below are #1040's reproduction,
read in code and not run live. In a module with no `Attribute` lines, with `E` the code editor's path from
`dump_visual_tree` and the caret after `Debug.Print 1` on the line `    Debug.Print 1, 2`:

```
press_key {"target":E,"key":"End","modifiers":"Shift"}      selects ", 2"
type_text {"target":E,"text":", 3"}
→ success; the line now reads "    Debug.Print 1, 2, 3"
```

A person's typing replaces the selection. `UiAutomationDriver.TypeText` inserts at the caret on every surface (a
`TextEditor`, a `TextArea`, and a `TextBox` at its `CaretIndex`), never reads the selection, and, away from a
described line's end, replies only with how many characters it typed. Its description says it inserts "at the
caret", so it does what it says; what it does not do is what a person's typing does. `press_key` already judges a
key with a selection standing as a write over it, so the two tools disagree. The code window's own Enter has the
same shape, which is a product defect in the same issue.

**Workaround.** Delete the selection first with `press_key {"target":E,"key":"Delete"}`, then call `type_text`.

**Suggested fix.** Write over the selection, as a person's typing does and as `press_key` already assumes, and say
in the reply how much was replaced. [#1040](https://github.com/hexide-io/HexIDE/issues/1040).

> **Fixed.** `type_text` writes over a standing selection, as a person's typing does, as one change, and says so:
> "typed 3 char(s) into TextEditor, replacing the 3 selected char(s)". The selection is the span judged, as `press_key`
> judges a key over one, so a selection reaching the header or a member's Attribute lines is refused, with a reply that
> names how far it reaches rather than where it begins. That is stricter than a typed key, which writes over what the
> read-only sections let it; aligning the two is [#1055](https://github.com/hexide-io/HexIDE/issues/1055). At the end of
> a described declaration the text is placed past its run with the selection it replaces in hand, as Shift+Enter places
> a line break, and no selection is left on what replaced it. Typing is not a deletion, so a selected declaration's run
> is not taken with it. Over a box selection the text is written on each of the box's lines, as typing there writes it,
> and nothing outside the box is taken. A TextBox's selection is replaced in one assignment. The code window's own Enter
> had the same shape and now replaces the selection too: a selected declaration's text as Shift+Enter does, the tail of
> a declaration's line as Enter at its end does, re-cased and closed, and a box's text before the line break. Writing
> over a box whose lines a folded section starts on, such as a declaration's folded Attribute run, hung the IDE in
> AvaloniaEdit's height tree, for a person's key as for these; such folds are opened for the write and folded again.
> Ctrl+V was missed, and still pasted over the box with the library's own replacement until
> [#1251](https://github.com/hexide-io/HexIDE/issues/1251) routed it the same way. A plain Enter still writes a bare
> line feed into a CRLF file, which is [#530](https://github.com/hexide-io/HexIDE/issues/530). Guarded by
> `MemberRunFollowsItsLineTests`, whose typing and Ctrl+V cases over a folded declaration each hang with their guard
> removed. Until #1251 the typing case wrote `()`, which does not hang, so removing the guard turned nothing red.
