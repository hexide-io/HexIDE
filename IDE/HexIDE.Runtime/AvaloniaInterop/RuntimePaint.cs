using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.AvaloniaInterop;

// #991: console Paint timings in the drawing-methods oracle. Only Refresh discards window drawing;
// native exposure by another application deliberately neither discards it nor raises Paint.
internal static class RuntimePaint
{
    internal static readonly EventClass PaintEvent = new("Paint");
    internal static readonly EventClass ResizeEvent = new("Resize");

    internal static IFormView? Form(Control control) => FormRoot.View(control)
        ?? control.GetLogicalAncestors().OfType<IFormView>().FirstOrDefault();

    internal static void Raise(Control control)
    {
        if (RuntimeDrawing.Of(control) is not { AutoRedraw: false })
            return;
        if (FormRoot.View(control) is { } view)
            view.Context.ExecuteSub("Form_Paint");
        else
            control.ExecuteSub(PaintEvent);
    }

    internal static void Post(Control control) => Dispatcher.UIThread.Post(() =>
    {
        if (Form(control) is { IsShown: true } && VBVisibility.Get(control))
            Raise(control);
    }, DispatcherPriority.Background);

    internal static void PostForm(VBWindowContext context, bool includePictures) => Dispatcher.UIThread.Post(() =>
    {
        if (context.Window is not { } window || FormRoot.View(window) is not { IsShown: true })
            return;
        Raise(window);
        if (includePictures)
            foreach (var picture in window.GetLogicalDescendants().OfType<VBPictureBox>())
                if (VBVisibility.Get(picture))
                    Raise(picture);
    }, DispatcherPriority.Background);
}
