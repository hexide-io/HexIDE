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
        windowContext = new VBWindowContext(new StandaloneStandardLib(this));
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        windowContext.ExecuteSub("Form_Load");
    }

    protected override void OnResized(WindowResizedEventArgs e)
    {
        base.OnResized(e);
        windowContext.ExecuteSub("Form_Resize");
    }

    public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null)
    {
        windowContext.ExecuteSub(name, args);
    }
}