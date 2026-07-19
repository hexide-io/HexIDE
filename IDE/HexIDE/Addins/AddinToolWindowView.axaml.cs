using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace HexIDE.Addins;

public partial class AddinToolWindowView : UserControl
{
    public AddinToolWindowView() => InitializeComponent();

    // An addin tool window's Content is a single, stateful Control (created once by the addin's factory).
    // Dock re-materialises this view when the tool window is dragged to a new dock, so two views can briefly
    // want the same control — and a control can only have one visual parent. A declarative
    // ContentControl.Content bind crashed there ("already has a visual parent"). Instead we host the control
    // ourselves and DETACH it from any prior parent first, so a re-host just relocates the (stateful) control.

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        HostContent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        HostContent();
    }

    private void HostContent()
    {
        var host = this.FindControl<Border>("PART_Host");
        if (host is null) return;

        if (DataContext is not AddinToolWindowViewModel { Content: Control control })
        {
            host.Child = null;
            return;
        }

        if (ReferenceEquals(host.Child, control))
            return; // already hosting it

        Detach(control);
        host.Child = control;
    }

    /// <summary>Remove a control from whatever parent currently holds it, so it can be re-hosted without
    /// hitting Avalonia's "already has a visual parent" check.</summary>
    private static void Detach(Control control)
    {
        switch (control.GetVisualParent())
        {
            case Border b when ReferenceEquals(b.Child, control):
                b.Child = null;
                break;
            case Decorator d when ReferenceEquals(d.Child, control):
                d.Child = null;
                break;
            case ContentPresenter cp when ReferenceEquals(cp.Content, control):
                cp.Content = null;
                break;
            case ContentControl cc when ReferenceEquals(cc.Content, control):
                cc.Content = null;
                break;
            case Panel p:
                p.Children.Remove(control);
                break;
        }
    }
}
