using System.Diagnostics;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// Locates a real, third-party language server for the foreign-backend tests.
///
/// <para>
/// These tests exist because a client and a server written by the same hand converge on their shared
/// assumptions rather than on the specification — they work together and are wrong in matching ways.
/// HexIDE's own server advertised no capabilities while HexIDE's own client called every method
/// unconditionally, and each was "correct" only because the other was wrong to match. Three defects hid in
/// that gap and surfaced within hours of driving a server we did not write. So the value here is precisely
/// that the far end does not accommodate us.
/// </para>
///
/// <para>
/// There are two, by different authors, because one server establishes that HexIDE can talk to something
/// foreign and a second establishes that it was not accidentally shaped around that one server's habits.
/// </para>
/// </summary>
internal sealed class ForeignServer
{
    /// <summary>A Markdown linter. Claims <c>.md</c>; publishes diagnostics on open, change and save.</summary>
    public static readonly ForeignServer Markdown = new(
        ForeignServerAcquisition.Markdown,
        pathVariable: "HEXIDE_MARKDOWN_LSP",
        onPath: "rumdl",
        serverArguments: "server",
        languageId: "markdown",
        extensions: [".md", ".markdown"]);

    /// <summary>
    /// A LaTeX server.
    ///
    /// <para>
    /// Its extensions include <c>.cls</c>, which is a LaTeX class file and a VB6 class module both. That
    /// collision is the reason routing does not read a lone <c>.cls</c> claim as "serves VB6", and having
    /// a real LaTeX server here means that rule is exercised rather than argued about.
    /// </para>
    /// </summary>
    public static readonly ForeignServer Latex = new(
        ForeignServerAcquisition.Latex,
        pathVariable: "HEXIDE_LATEX_LSP",
        onPath: "texlab",
        serverArguments: "",
        languageId: "latex",
        extensions: [".tex", ".cls", ".sty", ".bib"]);

    private ForeignServer(
        ForeignServerSource source,
        string pathVariable,
        string onPath,
        string serverArguments,
        string languageId,
        string[] extensions)
    {
        Source = source;
        PathVariable = pathVariable;
        OnPath = onPath;
        ServerArguments = serverArguments;
        LanguageId = languageId;
        Extensions = extensions;
    }

    public ForeignServerSource Source { get; }

    /// <summary>Point this at an executable to use your own build instead of the pinned download.</summary>
    public string PathVariable { get; }

    /// <summary>The name to look for on <c>PATH</c>.</summary>
    public string OnPath { get; }

    /// <summary>The launch arguments that put this server into language-server mode over stdio.</summary>
    public string ServerArguments { get; }

    /// <summary>
    /// What this server calls its language, and which files it claims. Declared here rather than taken
    /// from a HexIDE constant precisely because these are the SERVER's claims — the point of the routing
    /// design is that HexIDE holds no global opinion about what a file is.
    /// </summary>
    public string LanguageId { get; }

    public string[] Extensions { get; }

    /// <summary>
    /// The executable, or null when none is available. Checked in order: an explicitly configured path,
    /// the name on PATH, then the pinned download.
    ///
    /// <para>
    /// The download comes last on purpose. A developer who has pointed at their own build, or has one
    /// installed, means it — this should not silently prefer a different version to the one they chose.
    /// </para>
    /// </summary>
    public string? Find()
    {
        if (Environment.GetEnvironmentVariable(PathVariable) is { Length: > 0 } configured)
            return File.Exists(configured) ? configured : null;

        return FindOnPath(OnPath) ?? ForeignServerAcquisition.EnsureAvailable(Source);
    }

    /// <summary>
    /// Demands that these tests actually run. Set in CI, where a skip would mean the foreign-server proof
    /// quietly stopped happening and nobody noticed.
    /// </summary>
    public const string RequiredVariable = "HEXIDE_REQUIRE_FOREIGN_LSP";

    public static bool IsRequired =>
        Environment.GetEnvironmentVariable(RequiredVariable) is "1" or "true" or "yes";

    private static string? FindOnPath(string command)
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
}

/// <summary>
/// A <see cref="FactAttribute"/> that skips — <b>visibly</b> — when a named foreign server is unavailable.
///
/// <para>
/// Deliberately a skip rather than an early <c>return</c>. A test that returns early passes, and a passing
/// test that asserted nothing is the exact failure this project keeps warning about: verification that
/// fails <em>open</em> produces a confident green meaning nothing. A skip says so in the runner output.
/// </para>
/// </summary>
public sealed class ForeignServerFactAttribute : FactAttribute
{
    /// <param name="server">
    /// Which server this test needs — <c>markdown</c> or <c>latex</c>. A string rather than the type
    /// itself because attribute arguments must be compile-time constants.
    /// </param>
    public ForeignServerFactAttribute(string server = "markdown")
    {
        var needed = server switch
        {
            "latex" => ForeignServer.Latex,
            _ => ForeignServer.Markdown,
        };

        if (needed.Find() is not null) return;

        // Where the proof is mandated, absence is a failure rather than a skip. Everywhere else it is a
        // skip: an offline machine should not fail a suite over a test fixture it could not fetch.
        if (ForeignServer.IsRequired)
        {
            throw new InvalidOperationException(
                $"{ForeignServer.RequiredVariable} is set, but the '{server}' language server could not be "
              + "obtained. The foreign-backend tests are the only check that HexIDE speaks LSP to something "
              + "it did not write, so they are not allowed to skip here.");
        }

        Skip = $"No '{server}' language server available. It is normally downloaded on demand; set "
             + $"{needed.PathVariable} to an executable, or put `{needed.OnPath}` on PATH, to choose your "
             + "own. Offline machines skip these.";
    }
}
