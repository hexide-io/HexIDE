using System;
using System.Threading.Tasks;

namespace HexIDE.IDE;

/// <summary>Which of a running program's own boxes a window holds: its MsgBox or its InputBox.</summary>
public enum ProgramBox
{
    /// <summary>The program's MsgBox.</summary>
    Message,

    /// <summary>The program's InputBox.</summary>
    Input,
}

/// <summary>
/// Where a run engine shows its program's MsgBox and InputBox (#5616): windows the IDE makes and records as the program's.
/// </summary>
/// <remarks>
/// <para>
/// <b>The IDE keeps the record.</b> A window made here is the program's because the IDE made it for the engine, so the IDE
/// tells its own prompts from a program's boxes without asking the engine (#3513).
/// </para>
/// <para>
/// Windows and their content are Avalonia objects, passed as <see cref="object"/> because Core has no Avalonia.
/// </para>
/// </remarks>
public interface IProgramDialogWindows
{
    /// <summary>
    /// A desktop dialog window around <paramref name="content"/>, captioned <paramref name="caption"/>, recorded as the
    /// program's: laid out left to right, whatever the IDE's language, and without the IDE's dialog chrome.
    /// </summary>
    object MakeWindow(string caption, object content, ProgramBox kind);

    /// <summary>The window a program's box is shown over on the desktop: the topmost of the IDE's and the program's.</summary>
    object? Owner { get; }

    /// <summary>
    /// In a single-view IDE, shows <paramref name="content"/> in a window inside the IDE's view, recorded as the program's,
    /// until <paramref name="close"/> is called. The task completes once it has closed.
    /// </summary>
    Task ShowInView(string caption, object content, ProgramBox kind, out Action close);
}
