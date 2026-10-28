# A context-menu action bound to a method cannot be driven — **CLOSED** (#947, 2026-10-20)

> **Fixed.** It can be driven through the UI, as a person does it: `select` the form's node, `expand` the Project
> Explorer's tree, which opens its flyout, then `invoke` **Remove Form1**. Measured on Windows on #947's branch, by an
> agent that was told nothing about the route's history: `invoke` replied `invoked MenuItem 'Remove Form1' and closed
> the menu, as a click does`, the flyout was gone, and Form1 had left the project with no prompt. Before #947 the same
> `invoke` left the flyout open. `invoke_command` still cannot reach a public method, which
> [#836](https://github.com/hexide-io/HexIDE/issues/836) keeps open as a convenience; it is no longer the only route.


**Symptom.** Live-checking #831 needed a project with no startup form, so Form1 had to be removed. Both of the UI's
routes are out of reach:

```
invoke_menu_item {"path":"Project/Remove Form1"}
→ {"success":false,"error":"'Project/Remove Form1' is a submenu or has no command"}
interact {"target":".../Pane[Project Group - Group1]/Custom/Tree/Pane/TreeItem[Project1 (Project1.vbp)]/TreeItem[Form1 (Form1.frm)]","action":"select"}
→ {"success":true, ... "selected 'Form1 (Form1.frm)'"}
interact {"target":".../Pane[Project Group - Group1]/Custom","action":"invoke_command","value":"DeleteForm"}
→ {"success":false,"mechanism":"reflection","error":"no public property 'DeleteForm' on ProjectToolViewModel"}
```

The Project menu's item is inert, which is a product defect (#835). The Project Explorer's context menu binds
**Remove Form1** to a method, `ProjectToolViewModel.DeleteForm()`, rather than to an `ICommand`. So `inspect_element`
does not list it and `invoke_command` cannot reach it. This entry first said that no tool opens a context menu; that
was wrong (below). The refusal also names a *property* when a command was asked for.

**Corrected 2026-10-13: `expand` opens the context menu.** `interact` `expand` on a control that owns a ContextMenu
or flyout opens it, and this tree's flyout was measured that way when #343 closed (`interact expand …/Custom/Tree →
"opened flyout on 'Tree'"`, in the archive). No tool description says so, which is how this entry missed it
([#949](https://github.com/hexide-io/HexIDE/issues/949)). Remove binds `Command="{CompiledBinding DeleteForm}"`,
which Avalonia turns into a command, so `invoke` on that item with the flyout open should run it and, since #947, close
the flyout as a click does. That is read in code, not run. Remove acts on the selected form
and asks nothing, so check that `select`'s reply names that form, and that the item's header reads **Remove** and its
name, before invoking it. Until the route is checked live, the workaround below stands.

**Workaround.** Load a `.vbp` whose `Startup` names no form in the project, by passing it as the positional argument
at launch.

**Suggested fix.** `invoke_command` falling back to a public parameterless method, listed by `inspect_element`, and
a refusal that names what it looked for rather than a property. An action to open a context menu is not needed:
`expand` is it. [#836](https://github.com/hexide-io/HexIDE/issues/836).
