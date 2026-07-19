using System;
using System.Collections.Generic;
using Avalonia.Controls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

public static class AvaloniaMethodsInteroperability
{
    public static Vb6Value Call(this Control c, string method, IReadOnlyList<Vb6Value> args)
    {
        if (c is ItemsControl itemsControl)
        {
            if (method.Equals("additem", StringComparison.OrdinalIgnoreCase))
            {
                itemsControl.Items.Add(args[0].Value);
                return Vb6Value.Nothing;
            }
            else if (method.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                itemsControl.Items.Clear();
                return Vb6Value.Nothing;
            }
        }

        throw new Exception($"Unknown method {method} on {c}");
    }
}