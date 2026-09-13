using System;
using System.Runtime.InteropServices;

namespace HexIDE.Desktop;

/// <summary>
/// Writes to the console that launched us, on a program that does not own one.
/// </summary>
/// <remarks>
/// <para>
/// HexIDE.Desktop is a <c>WinExe</c>, which on Windows means it starts with no console attached at all —
/// <c>Console.WriteLine</c> succeeds and the text goes nowhere. That is the right setting for a GUI
/// program (an <c>Exe</c> would flash a console window on every launch), so <c>--help</c> has to borrow
/// the caller's console rather than the program having one.
/// </para>
/// <para>
/// Every other platform behaves the way the code reads: standard output is already connected, and
/// attaching is neither possible nor needed.
/// </para>
/// </remarks>
internal static class ConsoleOutput
{
    private const int AttachParentProcess = -1;

    private static bool TryAttachToParentConsole()
    {
        if (!OperatingSystem.IsWindows()) return true;

        try
        {
            return AttachConsole(AttachParentProcess);
        }
        catch (DllNotFoundException)
        {
            // Not Windows after all, or a stripped environment. Nothing to attach to and nothing to fix.
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes <paramref name="text"/> to the launching console, and reports whether anyone could see it.
    /// </summary>
    /// <remarks>
    /// False means the program was started from somewhere with no console — a shortcut, Explorer, a
    /// launcher — where there is no way to show text without opening a window the user did not ask for.
    /// The caller decides what to do about that; printing into the void is not a failure worth crashing
    /// over, but it is worth being able to distinguish.
    /// </remarks>
    public static bool Write(string text)
    {
        if (!TryAttachToParentConsole()) return false;

        Console.WriteLine(text);

        // A GUI process returns control to the shell the moment it starts, so the prompt has already been
        // printed and our output lands after it. The blank line keeps the next prompt off the last line
        // of ours rather than appended to it.
        Console.WriteLine();
        return true;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int dwProcessId);
}
