using System;
using System.Threading.Tasks;

namespace HexIDE.IDE;

/// <summary>What the runtime asks of a new form window, in layout units: its caption, its size with any menu bar's height
/// already added, and its position in the host.</summary>
public sealed record HostedFormRequest(string Caption, double Width, double Height, double Left, double Top);

/// <summary>A hosted window's state, member for member as Avalonia's <c>WindowState</c>, which Core can't name.</summary>
public enum HostedFormState { Normal, Minimized, Maximized, FullScreen }

/// <summary>
/// The host's window for one program form, from <see cref="IProgramHost.CreateForm"/> (#4068, operation 1 of #3411's run
/// side). Only this form's own window crosses the seam: <see cref="Window"/> is it, typed <c>object</c> because Core has no
/// Avalonia, and the runtime uses it as the control it is for the form's chrome. Nothing else of the host's is reachable.
/// The runtime's event root is its own content inside the window, so the host implements no runtime interface.
/// </summary>
public interface IHostedForm
{
    /// <summary>The form's own window. The runtime writes the caption, size and other chrome through it.</summary>
    object Window { get; }
    /// <summary>Puts the runtime's root, holding the form's content, in the window.</summary>
    void SetContent(object content);
    /// <summary>Shows the window, blocking the forms behind it or not; the task completes when the window closes. Showing
    /// it again after <see cref="Hide"/> returns the same task.</summary>
    Task Show(bool blockBehind);
    void Hide();
    /// <summary>Closes the window without asking the program, as the host's own close does.</summary>
    void Close();
    void Activate();
    /// <summary>Shown, not hidden, and not yet closed.</summary>
    bool IsShown { get; }
    /// <summary>Held by a modal form: the window takes no input, and a press on it raises <see cref="HeldPressed"/>.</summary>
    bool IsHeld { get; set; }
    double Width { get; set; }
    double Height { get; set; }
    /// <summary>The size in whole device pixels and the state: a size set but not yet laid out counts at once, and a
    /// minimised window is 0 by 0.</summary>
    (double Width, double Height, HostedFormState State) Extent { get; }
    /// <summary>Its bounds or its state changed; the runtime compares <see cref="Extent"/> to tell a resize.</summary>
    event Action? Resized;
    /// <summary>Its Width or Height was set, before layout.</summary>
    event Action? SizeSet;
    event Action? Shown;
    /// <summary>It became the host's active window while not held.</summary>
    event Action? Activated;
    event Action? HeldPressed;
    /// <summary>Asked when the close box is pressed; true refuses the close, as the program's QueryUnload or Unload can.</summary>
    Func<bool>? Closing { get; set; }
}
