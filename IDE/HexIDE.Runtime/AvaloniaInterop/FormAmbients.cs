using System.Runtime.CompilerServices;
using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

// #4448, vb6.exe SP6 native/p-code: form changes notify every placed UserControl in file order.
internal static class FormAmbients
{
    private static readonly ConditionalWeakTable<Control, FormControls> Forms = new();

    internal static void Register(Control window, FormControls controls) => Forms.AddOrUpdate(window, controls);

    internal static void Changed(Control window, string property)
    {
        if (!Forms.TryGetValue(window, out var controls))
            return;
        foreach (var control in controls.All.OfType<VBUserControl>())
            control.ExecuteSub("UserControl_AmbientChanged", [new Vb6Value(property)]);
    }

    internal static bool TryForm(object? container, VbObject? form,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Control? window)
    {
        // A PictureBox or a Frame passes the form's ambients through (measured for both, #4448); others stay unmeasured.
        while (container is VBPictureBox or VBFrame
            && ContainerMembers.TryGet((Avalonia.Controls.Control)container, "Container", out var parent))
            container = parent.Value;
        window = ReferenceEquals(container, form) ? form?.FormWindow : null;
        return window is not null;
    }
}
