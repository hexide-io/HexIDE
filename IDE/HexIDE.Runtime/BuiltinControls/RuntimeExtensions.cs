using Avalonia.Controls;
using Avalonia.VisualTree;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

internal static class RuntimeExtensions
{
    public static void ExecuteSub(this Control control, EventClass eventClass)
    {
        if (VBProps.GetName(control) is { } name &&
            control.FindAncestorOfType<IModuleExecutionRoot>() is { } executionRoot)
        {
            // A control-array element carries its Index; a shared handler is `Command1_Click(Index As Integer)`,
            // so pass the Index as the leading arg. A standalone control (no Index) dispatches with no args.
            var args = VBProps.GetIndex(control) is { } index
                ? new[] { new Vb6Value(index) }
                : null;
            executionRoot.ExecuteSub($"{name}_{eventClass.Name}", args);
        }
    }
}