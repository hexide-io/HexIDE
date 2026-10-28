# `invoke_command` reaches only an `ICommand` property, and refuses a method by saying there is no such property

**Symptom.** Recorded on 2026-10-12 under the entry now archived as "A context-menu action bound to a method cannot be
driven", and unchanged at 4789f9a by code reading. With Form1 selected in the Project Explorer:

```
interact {"target":".../Pane[Project Group - Group1]/Custom","action":"invoke_command","value":"DeleteForm"}
→ {"success":false,"mechanism":"reflection","error":"no public property 'DeleteForm' on ProjectToolViewModel"}
```

`DeleteForm` is a public method of `ProjectToolViewModel`, which the Project Explorer's flyout binds as
`Command="{CompiledBinding DeleteForm}"`. `invoke_command` looks only for a public property holding an `ICommand`
(`UiAutomationDriver.ReflectInvokeCommand`), and `inspect_element` lists only commands and properties, so the method
is not listed either. The refusal names a property, which reads as a misspelt name rather than as "a method cannot
be reached this way".

**Workaround.** Drive the control the method is bound to, as a person does. For Remove: `select` the form's node,
`expand` the Project Explorer's tree to open its flyout, then `invoke` **Remove Form1** (measured with #947).

**Suggested fix.** `invoke_command` falling back to a public parameterless method, listed by `inspect_element`, and
a refusal that names what it looked for. [#836](https://github.com/hexide-io/HexIDE/issues/836).

> **Fixed.** `invoke_command` reaches a method a Command is bound to as well as an `ICommand` property. Which methods
> are bound comes from the XAML compiler's own record: Avalonia compiles each `Command="{CompiledBinding X}"` on a
> method into a trampoline named for the type, the method and its arity, and `XamlCommandBindings` reads those. So
> `invoke_command` calls, and `inspect_element` lists as `kind: "method"`, exactly the methods a person can click.
>
> The first version accepted any public parameterless method. Its review found that this reached about thirty more
> that no UI exposes, such as `OnInitialized` and every Options page's `SaveToSettings`. It also reached a
> `MakeProject` that skips the guard on the `MakeProjectCommand` the menu binds.
>
> As Avalonia does, the call is refused when a `Can<name>(object)` method of any visibility returns false. A reply
> with no such guard says so, so the caller checks the effect. The refusals and failures name their cause:
>
> - a method bound with a CommandParameter, which the caller cannot supply;
> - a method no Command is bound to;
> - a name that is neither;
> - an exception from the method or its Can method;
> - a faulted or cancelled task.
>
> A task still running is logged if it fails later. A designer context menu that binds through another control's
> DataContext is still not reached from the menu item, as with a command property.
>
> Measured on Windows through the server's HTTP endpoint:
>
> - The Project Explorer lists six methods, and the main window lists 32 of its 91 members.
> - `invoke_command DeleteForm`, with Form1 selected, removed the form.
> - `OnInitialized` was refused as unbound.
>
> Guarded by `InvokeCommandMethodTests`, and by `XamlCommandBindingsTests`, which read HexIDE's own assembly so an
> Avalonia change to the names fails there.
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools, on
> a new project.
>
> - **`inspect_element` on the Project Explorer.** On `…/Pane[Project Group - Group1]/Custom`, its `ProjectToolView`,
>   it listed six methods, `DeleteForm` among them with `kind: "method"`. The `Tree` under it inherits that
>   DataContext and lists no members of its own.
> - **`invoke_command` `OnInitialized` on that node** replied "no ICommand property or Command-bound method
>   'OnInitialized' on ProjectToolViewModel; …".
> - **`invoke_command` `DeleteForm`, with `TreeItem[Form1 (Form1.frm)]` selected,** replied "called method
>   'DeleteForm' on ProjectToolViewModel; no Can method guards it, so check that it did what was meant". No prompt
>   opened, and `get_project_info` then listed no forms.
