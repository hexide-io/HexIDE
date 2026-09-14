using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace HexIDE.Desktop;

/// <summary>
/// What the console that launched us can show, decided once per write and handed to whatever renders.
/// </summary>
/// <param name="Colour">
/// Whether 24-bit SGR colour and the block glyphs will render: output is going to a terminal rather than a
/// file or pipe, colour has not been refused, and the terminal has said it can take it.
/// </param>
/// <param name="Width">
/// Cells per row before the terminal wraps. <see cref="int.MaxValue"/> when output is redirected, because a
/// file does not wrap.
/// </param>
internal readonly record struct ConsoleTarget(bool Colour, int Width);

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
/// <para>
/// <b>Attached and redirected are different questions, and both get asked.</b> <c>--help &gt; file</c>
/// run from a terminal attaches successfully, answers <c>Console.WindowWidth</c> with the terminal's
/// width, and sends the bytes to the file. So a successful attach says nothing about where the output is
/// going; <see cref="Console.IsOutputRedirected"/> does, and it is checked before either colour or width
/// is trusted.
/// </para>
/// </remarks>
internal static class ConsoleOutput
{
    private const int AttachParentProcess = -1;
    private const int StdOutputHandle = -11;
    private const uint EnableVirtualTerminalProcessing = 0x0004;
    private const int Utf8CodePage = 65001;

    /// <summary>Row width assumed when a console is attached but will not say how wide it is.</summary>
    private const int DefaultWidth = 80;

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
    public static bool Write(string text) => Write(_ => text);

    /// <summary>
    /// Attaches to the launching console, asks what it can show, and writes what <paramref name="render"/>
    /// produces for that. Returns whether anyone could see it, as <see cref="Write(string)"/> does.
    /// </summary>
    /// <remarks>
    /// The console is left as it was found. Showing colour on Windows means switching the console's output
    /// code page to UTF-8 and turning on virtual-terminal processing, and both belong to the shell that
    /// launched us: a code page left at 65001 changes how that shell shows every later command's output,
    /// which is a side effect nobody asked <c>--help</c> for.
    /// </remarks>
    public static bool Write(Func<ConsoleTarget, string> render)
    {
        if (!TryAttachToParentConsole()) return false;

        var redirected = Console.IsOutputRedirected;
        Action? restoreMode = null;
        var colour = !redirected && !ColourRefused() && TryEnableColour(out restoreMode);
        var target = new ConsoleTarget(colour, redirected ? int.MaxValue : WindowWidth());

        var encoding = Console.OutputEncoding;
        var swapEncoding = colour && encoding.CodePage != Utf8CodePage;
        try
        {
            if (swapEncoding) Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

            Console.WriteLine(render(target));

            // A GUI process returns control to the shell the moment it starts, so the prompt has already
            // been printed and our output lands after it. The blank line keeps the next prompt off the
            // last line of ours rather than appended to it.
            Console.WriteLine();
        }
        finally
        {
            if (swapEncoding) Console.OutputEncoding = encoding;
            restoreMode?.Invoke();
        }

        return true;
    }

    /// <summary>
    /// <c>NO_COLOR</c> set to anything but the empty string refuses colour (no-color.org), and a terminal
    /// that calls itself <c>dumb</c> cannot show it.
    /// </summary>
    private static bool ColourRefused() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")) ||
        string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.Ordinal);

    /// <summary>
    /// Whether the terminal will render 24-bit SGR colour, turning it on where that is something a process
    /// has to do. <paramref name="restore"/> undoes any change made.
    /// </summary>
    /// <remarks>
    /// On Windows the console renders SGR only once a process has set
    /// <c>ENABLE_VIRTUAL_TERMINAL_PROCESSING</c> on its output handle, and every console that accepts that
    /// flag (conhost since Windows 10 1703, Windows Terminal, and terminals that host either) also takes
    /// 24-bit colour. A shell running inside those has usually set the flag already, but for its own
    /// handle, not ours. Elsewhere there is nothing to turn on and the question is whether the terminal
    /// has declared truecolour: <c>COLORTERM</c> is the convention, and Windows Terminal announces itself
    /// to a WSL shell with <c>WT_SESSION</c> rather than that.
    /// </remarks>
    private static bool TryEnableColour(out Action? restore)
    {
        restore = null;
        if (!OperatingSystem.IsWindows())
        {
            var colorTerm = Environment.GetEnvironmentVariable("COLORTERM") ?? "";
            return colorTerm.Contains("truecolor", StringComparison.OrdinalIgnoreCase) ||
                   colorTerm.Contains("24bit", StringComparison.OrdinalIgnoreCase) ||
                   !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WT_SESSION"));
        }

        var handle = GetStdHandle(StdOutputHandle);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return false;
        if (!GetConsoleMode(handle, out var mode)) return false;
        if ((mode & EnableVirtualTerminalProcessing) != 0) return true;
        if (!SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing)) return false;

        restore = () => SetConsoleMode(handle, mode);
        return true;
    }

    private static int WindowWidth()
    {
        try
        {
            var width = Console.WindowWidth;
            return width > 0 ? width : DefaultWidth;
        }
        catch (IOException)
        {
            // Attached to something that is not a real console window. Rare, and a guess is all there is.
            return DefaultWidth;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
}
