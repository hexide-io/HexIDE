# `select` and `type_text` applied #1069's not-showing rule unevenly (#1256)

Three paths still acted on, or gave advice from, rows and template parts that do not show:

- **`select` with a value on a list** looked among unrealized rows whenever the item count exceeded the rows that showed, which a hidden row (the Properties window's collapsed category) made true even with every row realized. It scrolled nothing, and the miss said the rows not on screen had been brought into view. The rows it did scroll in were matched with no hidden filter.
- **`select` under nested collapsed tree nodes** named only the nearest collapsed node to expand, and `expand` on it was refused as not showing while the one above it was collapsed too.
- **`type_text`** picked a non-editable ComboBox's hidden template TextBox ahead of a TextBox after it, as in the Object Browser's toolbar, and was refused as aimed at a hidden control, where `press_key` passed it over.

> **Fixed.** `select` looks among unrealized rows only when a row has no container. It matches the rows it scrolls in
> among those that show. One that only a hidden row matches is refused as not showing, worded as it is found, since
> the container is recycled for another row once the list scrolls on. A miss counts the rows that show and says how
> many more do not, those scrolled in included. A row container a virtualizing panel has let go of, which keeps the
> last item it showed and, with its visibility bound to that item, shows, no longer stands in for the item. Under nested
> collapsed nodes, the refusal names each one to expand, outermost first ("'expand' 'Outer', then 'Inner', which hold
> it"), when the outermost shows. `type_text`, in both modes:
> - passes over a template part hidden within its own control's template, whatever that control's own state;
> - prefers a surface that shows, and still refuses one hidden by anything else as not showing;
> - refuses a target holding more than one surface of the kind it would pick, naming them, rather than typing into the
>   first (aimed at the Properties pane, that renamed the form);
> - refuses a non-editable ComboBox with nothing else to type into as having no text surface, saying to expand it and
>   select its item.
>
> Guarded in `UiAutomationDriverTests` and `SelectScrolledOutRowTests`.

> **Live, through the hexide tools (2026-10-27, Windows at 150%, a Debug build of main at f9b5649c, headless
> `claude -p`, #1330).** The review of #1293 said it was checked live without saying how, so it was checked again, with Form1
> selected in the designer. `type_text {"text":"x"}` on the Properties window's
> `…/Tab[TabProperties]/List[AlphabeticProperties]` answered `'ListBox[AlphabeticProperties]' holds 10 text boxes that
> show, so which one to type into is not clear; nothing was typed. Address the one you mean: …`, naming each, and the
> form kept its name. Keyboard `type_text` into the Immediate window still typed: `typed 5 key(s) into TextEditor`.
