# get_document_tabs reported three fewer tabs than the user could see — **CLOSED** (moved from the live file 2026-09-22)

> **Closed by #402 (#369 phases 4 and 5).**

**Symptom.** A newly built Protocol Inspector tab was visibly open in the tab strip, and:

```
get_document_tabs      -> only the form designer
activate_document_tab  -> "No document tab with title 'Protocol Inspector'"
```

The Object Browser and the language-server connection list were missing too. Three real tabs, in the same
strip, invisible to automation.

**Cause.** Both tools read `IDocumentDockService.OpenDocuments`, which is typed
`IReadOnlyList<BaseEditorWindowViewModel>` and tracks only the editors the service was asked to open. The
Object Browser, the connection list and the inspector are documents the shell adds straight to the dock, so
they were never in that list. Nothing was wrong with the tools' logic; they were answering a narrower
question than the one asked, and the difference was invisible from outside.

**How it bit.** It blocked verification of the very feature being built. Worse, the failure was
*affirmative*: not "I cannot see that kind of tab" but "no document tab with title X", which reads as the
tab not existing. I had to take a screenshot to establish that the thing I had just built was on screen.

**Fixed.** `IDocumentDockService` gained `AllTabs`, `ActiveTab`, `TryActivateAny` and `TryCloseAny`, reading
the dock's own `VisibleDockables`. The three tools now answer about the strip the user sees, `type` gained
a third value `tool` for documents that are not editors, and the description enumerates all three.

**And the error now names what IS open.** A bare "no tab called X" cannot be told from a typo, and cost a
second call to find out. The tabs were already in hand:

```
No document tab with title 'Protocl Inspector'. Open tabs: Object Browser,
Language & Debug Servers, Project1 - Form1 (Form), Protocol Inspector.
```
