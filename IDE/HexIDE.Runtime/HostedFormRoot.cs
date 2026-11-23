using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using HexIDE.IDE;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Dialogs;

namespace HexIDE.Runtime;

/// <summary>
/// The runtime's event root inside a single-view host window (#4068). It is the window's content and holds the form's own
/// content, menu bar and canvas, so a walk up from any control still finds the form's <see cref="IModuleExecutionRoot"/>
/// and <see cref="IFormView"/>. It draws nothing and adds no padding, border or clip, so the window's BackColor shows
/// through and the client area measures as before.
/// </summary>
public sealed class HostedFormRoot : Decorator, IModuleExecutionRoot, IFormView
{
    private readonly IHostedForm hosted;
    public VBWindowContext Context { get; }

    public HostedFormRoot(IHostedForm hosted, IBasicStandardLibrary standardLibrary)
    {
        MessageBoxFormHold.HoldInput(this);
        this.hosted = hosted;
        Context = new VBWindowContext(standardLibrary) { Window = (Control)hosted.Window };
        hosted.SetContent(this);
        hosted.Resized += () => Context.WindowResized();
        hosted.SizeSet += () => Context.WindowResized(sizeSet: true);
        hosted.Shown += () => Context.Shown();
        hosted.Activated += () =>
        {
            if (HeldBy is null)
                Context.Program?.Activated(Context);
        };
        hosted.HeldPressed += () => HeldBy?.ActivateForm();
        hosted.Closing = Context.UnloadFromCloseBox;
    }

    public void SetContent(Control content) => Child = content;
    public Task ShowInHost(bool blockBehind) => hosted.Show(blockBehind);
    public void ShowForm() => hosted.Show(false);
    public void HideForm() => hosted.Hide();
    public void CloseForm() => hosted.Close();
    public void ActivateForm() => hosted.Activate();
    public bool IsShown => hosted.IsShown;
    private IFormView? heldBy;
    public IFormView? HeldBy
    {
        get => heldBy;
        set { heldBy = value; hosted.IsHeld = value is not null; }
    }
    public (Size Size, WindowState State) Extent
    {
        get
        {
            var extent = hosted.Extent;
            return (new Size(extent.Width, extent.Height), (WindowState)extent.State);
        }
    }
    // Optional event paths retain the interface defaults used by the former MDI root.
    public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null) => Context.ExecuteSub(name, args);
    public bool Handles(string name) => Context.Handles(name);
    public void ExecuteEvent(string name, Func<IReadOnlyList<Vb6Value>> args, EventCoalescing coalescing = EventCoalescing.None, Func<bool>? when = null) =>
        Context.ExecuteEvent(name, args, coalescing, when);
    public void ExecuteTimer(string name, IReadOnlyList<Vb6Value>? args = null) => Context.ExecuteTimer(name, args);
    public Task<bool> UnloadAsync(int mode) => Context.UnloadAsync(mode);
}

/// <summary>
/// A form's runtime root from its own window (#4068): the window itself on the desktop, where <c>VBFormRuntime</c> is the
/// root, or the <see cref="HostedFormRoot"/> a host window holds. For sites that start from the window rather than from a
/// control inside it; a walk up from a control finds the root without this. It looks at no other window.
/// </summary>
public static class FormRoot
{
    public static IFormView? View(object? window) => window as IFormView
        ?? (window as ContentControl)?.Content as HostedFormRoot;

    public static IModuleExecutionRoot? Execution(object? window) => window as IModuleExecutionRoot
        ?? (window as ContentControl)?.Content as HostedFormRoot;
}
