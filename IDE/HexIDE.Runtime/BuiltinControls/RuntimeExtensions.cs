using Avalonia.Controls;
using Avalonia.VisualTree;
using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.BuiltinControls;

internal static class RuntimeExtensions
{
    public static void ExecuteSub(this Control control, EventClass eventClass)
    {
        if (VBProps.GetName(control) is { } name &&
            control.FindAncestorOfType<IModuleExecutionRoot>() is { } executionRoot)
            executionRoot.ExecuteSub($"{name}_{eventClass.Name}");
    }
}