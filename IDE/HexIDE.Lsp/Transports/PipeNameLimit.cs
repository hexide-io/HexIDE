using System;
using System.IO;

namespace HexIDE.Lsp;

/// <summary>
/// How long a named-pipe name may be on the host we are running on, and why.
/// </summary>
/// <remarks>
/// <para>
/// <b>The limit is the platform's, not ours, and it is dramatically different per platform.</b> On Windows
/// a named pipe is a kernel object under <c>\\.\pipe\</c> and the name may be long. On Unix .NET has no
/// named pipes to implement one with, so it builds the pipe out of a <b>Unix domain socket</b> at
/// <c>$TMPDIR/CoreFxPipe_&lt;name&gt;</c> — and the <c>sun_path</c> field that carries that path is a fixed
/// 104-byte array in the BSD sockets ABI. The whole of it is spent on the temp directory before the name is
/// reached.
/// </para>
/// <para>
/// <b>Which makes macOS the strict one, by a long way.</b> Linux puts the socket under <c>/tmp/</c>, so
/// about 87 characters survive for a name. macOS gives each session a private temp directory —
/// <c>/var/folders/&lt;2&gt;/&lt;28&gt;/T/</c> — which with the <c>CoreFxPipe_</c> prefix spends 60 of the 104
/// before the name starts. A name that is perfectly ordinary on the other two platforms is impossible
/// there, which is how hexide-io/HexIDE#694 presented: a configuration that worked on Windows and Linux
/// and could not work on a Mac, failing with an exception that mentioned neither pipe names nor a budget.
/// </para>
/// <para>
/// <b>Computed rather than hard-coded</b>, because the answer depends on the host's own temp directory:
/// <c>TMPDIR</c> is settable, a container may have a short one, and a hard-coded 43 would be wrong on both
/// sides of that. Measured against the runner that found the defect, this returns 43 on macOS.
/// </para>
/// <para>
/// <b>103, not 104.</b> The framework rejects a path of exactly 104 with a message that says 104 is
/// allowed, because the limit counts the terminating NUL that <c>sun_path</c> must also hold. Taking the
/// message at its word would leave a budget that is one too generous, and the failure would land at
/// connect time on the one platform nobody develops on.
/// </para>
/// </remarks>
public static class PipeNameLimit
{
    /// <summary>The <c>sun_path</c> field is 104 bytes on macOS, and one of them is the terminator.</summary>
    private const int UnixSocketPathMax = 103;

    /// <summary>What .NET puts in front of the name when it makes the socket. Framework detail, not ours.</summary>
    private const string UnixPipePrefix = "CoreFxPipe_";

    /// <summary>
    /// A Windows pipe path is <c>\\.\pipe\&lt;name&gt;</c> within MAX_PATH. Generous next to the Unix
    /// figure, and stated rather than left unbounded so the refusal below reads the same on every host.
    /// </summary>
    private const int WindowsPipeNameMax = 256 - 9;

    /// <summary>The longest pipe name that can work on this host.</summary>
    public static int Max { get; } = OperatingSystem.IsWindows()
        ? WindowsPipeNameMax
        : Math.Max(1, UnixSocketPathMax - (Path.GetTempPath().Length + UnixPipePrefix.Length));

    /// <summary>True when <paramref name="pipeName"/> cannot be used on this host.</summary>
    public static bool IsTooLong(string? pipeName) => pipeName is not null && pipeName.Length > Max;

    /// <summary>
    /// Why a name was refused, in terms of the thing the reader controls. It names the budget, where the
    /// rest of it went, and that the number is this machine's rather than a property of the configuration
    /// — because the reader's file may well be correct on the machine they wrote it on.
    /// </summary>
    public static string Refusal(string pipeName) => OperatingSystem.IsWindows()
        ? $"pipeName is {pipeName.Length} characters; this host allows {Max}."
        : $"pipeName is {pipeName.Length} characters and this host allows {Max}. On macOS and Linux a named "
        + $"pipe is a Unix domain socket at {Path.GetTempPath()}{UnixPipePrefix}<pipeName>, and the socket "
        + $"path may not exceed {UnixSocketPathMax} characters — the directory and prefix take "
        + $"{Path.GetTempPath().Length + UnixPipePrefix.Length} of them before the name begins. The limit is "
        + "this machine's, not the file's: macOS gives each session a long private temp directory, so the "
        + "same entry can be fine on Windows or Linux and impossible here. Shorten pipeName, and make sure "
        + "whatever is on the other end of the pipe is using the same one.";
}
