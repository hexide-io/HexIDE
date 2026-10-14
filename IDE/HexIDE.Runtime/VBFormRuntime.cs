using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime;

public class VBFormRuntime : Window, IModuleExecutionRoot
{

    private VBWindowContext windowContext;

    public VBWindowContext Context => windowContext;
    
    public VBFormRuntime()
    {
        windowContext = new VBWindowContext(new StandaloneStandardLib(this)) { Window = this };
    }

    // Form_Load is not raised here: the program raises Initialize and Load before the window is shown (#822, #994).

    protected override void OnResized(WindowResizedEventArgs e)
    {
        base.OnResized(e);
        windowContext.ExecuteSub("Form_Resize");
    }

    public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null)
    {
        windowContext.ExecuteSub(name, args);
    }

    public bool Handles(string name) => windowContext.Handles(name);

    public void ExecuteEvent(string name, Func<IReadOnlyList<Vb6Value>> args, EventCoalescing coalescing = EventCoalescing.None) =>
        windowContext.ExecuteEvent(name, args, coalescing);

    public void ExecuteTimer(string name, IReadOnlyList<Vb6Value>? args = null) => windowContext.ExecuteTimer(name, args);

    public Task<bool> UnloadAsync(int mode) => windowContext.UnloadAsync(mode);

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // The close box asks the program first, as VB6's does: QueryUnload with UnloadMode 0, then Unload, and either may
        // cancel. The window closes once the form has unloaded (#973). A close from code, End, Stop or the IDE shutting
        // down is not the program's to refuse.
        if (e.CloseReason == WindowCloseReason.WindowClosing && !e.IsProgrammatic && windowContext.UnloadFromCloseBox())
            e.Cancel = true;
        base.OnClosing(e);
    }
}