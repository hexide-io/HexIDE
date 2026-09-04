using System.Diagnostics;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// Locates a real, third-party Markdown language server for the foreign-backend tests.
///
/// <para>
/// These tests exist because a client and a server written by the same hand converge on their shared
/// assumptions rather than on the specification — they work together and are wrong in matching ways.
/// HexIDE's own server advertised no capabilities while HexIDE's own client called every method
/// unconditionally, and each was "correct" only because the other was wrong to match. Three defects hid in
/// that gap and surfaced within hours of driving a server we did not write. So the value here is precisely
/// that the far end does not accommodate us.
/// </para>
/// </summary>
internal static class ForeignServer
{
    /// <summary>Point this at a Markdown language server executable to enable the foreign-backend tests.</summary>
    public const string PathVariable = "HEXIDE_MARKDOWN_LSP";

    /// <summary>
    /// The executable, or null when none is available. Checked in order: the environment variable, then the
    /// name on PATH.
    /// </summary>
    public static string? Find()
    {
        if (Environment.GetEnvironmentVariable(PathVariable) is { Length: > 0 } configured)
            return File.Exists(configured) ? configured : null;

        return OnPath("rumdl");
    }

    private static string? OnPath(string command)
    {
        var exeName = OperatingSystem.IsWindows() ? command + ".exe" : command;
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), exeName);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry is not this helper's problem — skip it and keep looking.
            }
        }
        return null;
    }

    /// <summary>The launch arguments that put the server into language-server mode over stdio.</summary>
    public const string ServerArguments = "server";
}

/// <summary>
/// A <see cref="FactAttribute"/> that skips — <b>visibly</b> — when no foreign server is available.
///
/// <para>
/// Deliberately a skip rather than an early <c>return</c>. A test that returns early passes, and a passing
/// test that asserted nothing is the exact failure this project keeps warning about: verification that
/// fails <em>open</em> produces a confident green meaning nothing. A skip says so in the runner output.
/// </para>
/// </summary>
public sealed class ForeignServerFactAttribute : FactAttribute
{
    public ForeignServerFactAttribute()
    {
        if (ForeignServer.Find() is null)
            Skip = $"No Markdown language server found. Set {ForeignServer.PathVariable} to one, "
                 + "or put `rumdl` on PATH, to run the foreign-backend tests.";
    }
}
