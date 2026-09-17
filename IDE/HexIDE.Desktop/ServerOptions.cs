using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HexIDE.IDE;

namespace HexIDE.Desktop;

/// <summary>
/// One command-line option: how it is spelled, whether it takes a value, and what it does.
/// </summary>
/// <param name="Name">The flag without its prefix. Matched as both <c>--name</c> and <c>/name</c>.</param>
/// <param name="ValueName">The value's name for the help text, or null when the flag takes none.</param>
/// <param name="Summary">One line, as it appears in <c>--help</c>.</param>
/// <param name="Apply">
/// Consumes the flag. Receives the value when <paramref name="ValueName"/> is set, and returns whether it
/// took one, so the parser can skip it.
/// </param>
/// <param name="Aliases">
/// Other spellings that mean the same flag, written in full including their prefix.
/// </param>
internal sealed record CommandLineOption(
    string Name,
    string? ValueName,
    string Summary,
    Func<string?, bool> Apply,
    string[]? Aliases = null)
{
    public bool TakesValue => ValueName is not null;

    public IReadOnlyList<string> AllAliases => Aliases ?? [];
}

internal static class ServerOptions
{
    public static int? Port { get; private set; }
    public static bool NewProject { get; private set; }
    public static string? ProjectPath { get; private set; }
    public static string? Personality { get; private set; }

    /// <summary><c>--user-data-dir</c>: the directory this session keeps per-user files in, already made
    /// absolute against the directory HexIDE was started from. Null when the flag was not given.</summary>
    public static string? UserDataDirectory { get; private set; }

    /// <summary><c>--user-data-dir</c> was given with nothing after it that could be a directory.</summary>
    /// <remarks>
    /// <b>The one flag here that refuses to start rather than being skipped.</b> Every other malformed
    /// argument is ignored and the IDE starts normally, which is harmless when the flag was a convenience.
    /// This one is asked for precisely to keep a session away from the user's real settings, so ignoring it
    /// would quietly do the one thing it was given to prevent: a demo or an automation run writing into
    /// the live configuration.
    /// </remarks>
    public static bool UserDataDirectoryMissing { get; private set; }

    /// <summary>
    /// Where HexIDE was started from, captured before startup moves the working directory to the
    /// executable's folder, and what every path argument is relative to.
    /// </summary>
    private static string _invocationDirectory = Environment.CurrentDirectory;

    /// <summary><c>--developer-mode</c>: enable session developer mode (unlocks the Developer options
    /// node + the dev-gated capabilities, and shows the title-bar suffix).</summary>
    public static bool DeveloperMode { get; private set; }

    /// <summary><c>--capture-lsp</c>: arm the protocol capture for every connection before any of them is
    /// made, so a conversation is recorded in full from the first handshake.</summary>
    /// <remarks>
    /// <b>Not a debug-only flag, unlike <c>--developer-mode</c> beside it.</b> The capture ships in
    /// distributed builds because its audience is somebody writing a language server against a HexIDE they
    /// downloaded, so a flag that was inert in Release would put the feature out of reach of exactly the
    /// people it is for.
    /// </remarks>
    public static bool CaptureLsp { get; private set; }

    /// <summary><c>--help</c>: print the usage banner and exit without starting the IDE.</summary>
    public static bool HelpRequested { get; private set; }

    /// <summary>
    /// Every option the IDE accepts, in the order <c>--help</c> lists them.
    /// </summary>
    /// <remarks>
    /// <b>One list, read by both the parser and the help text.</b> They were two before, and the help text
    /// did not exist; the moment it did, a flag added to one and not the other would have produced a
    /// program that documents itself incorrectly — which is worse than one that documents itself not at
    /// all, because the reader has no reason to doubt it. Adding a flag here is what makes it parse AND
    /// appear in <c>--help</c>; <c>CommandLineDocumentationTests</c> then fails the build until
    /// <c>docs/command-line.md</c> mentions it too.
    /// </remarks>
    /// <remarks>
    /// <b>A summary has a width budget, and it is not a matter of taste.</b> The mark sits beside the
    /// option list only while the widest rendered row fits, with the mark's 24 cells and the gutter's 2
    /// in front of it, inside a 120-column console — the default width of both Windows consoles and
    /// Windows Terminal. So no row may exceed 94 visible columns. A row is
    /// <c>2 + the value column + 2 + the summary</c>, and the value column is the longest
    /// <c>--name &lt;value&gt;</c> spelling in the list, so a wider value name costs every row at once.
    /// <c>CommandLineDocumentationTests</c> computes this exactly as <see cref="HelpText"/> does and fails
    /// the build naming the offending option — the alternative being a green build that has silently
    /// demoted every default-sized window to the stacked layout.
    /// </remarks>
    public static IReadOnlyList<CommandLineOption> Options { get; } =
    [
        // /? is what a Windows user tries first and -h is what everyone else does. Neither cost anything,
        // and without them both land in the parser's silent-ignore arm — so asking for help would START
        // THE IDE, which is the worst answer available.
        new("help", null,
            "Print this message and exit. Also -h and /?.",
            _ => { HelpRequested = true; return false; },
            Aliases: ["-h", "/?", "-?"]),

        new("newproject", null,
            "Create a Standard EXE and skip the startup dialog.",
            _ => { NewProject = true; return false; }),

        // "from its first handshake" is the whole flag, not decoration. Envelopes are recorded for every
        // connection already and bodies can be armed at any time from the inspector; arming BEFORE the
        // first connection exists is the one thing only this flag can do.
        new("capture-lsp", null,
            "Record every language-server conversation from its first handshake.",
            _ => { CaptureLsp = true; return false; }),

        // A value beginning `--` is another flag, not a directory: `--user-data-dir --newproject` must not
        // create a profile called --newproject. Only `--`, because `/` begins every absolute Unix path.
        new("user-data-dir", "<path>",
            "Keep settings and other per-user files in this directory instead.",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value) || value.StartsWith("--", StringComparison.Ordinal))
                {
                    UserDataDirectoryMissing = true;
                    return false;
                }

                UserDataDirectory = Path.GetFullPath(value, _invocationDirectory);
                return true;
            }),

        // <name>, not <vb6|vbaode|vba>: the value column is the longest spelling in the list, so naming
        // the values here made every other row ten cells wider and put the beside layout out of reach of
        // a default console. They live in the summary instead, where they cost one row rather than six.
        new("personality", "<name>",
            "IDE personality for the session: vb6, vbaode or vba.",
            value => { Personality = value; return true; }),

        new("server-port", "<port>",
            "Start the automation server on this port. Debug builds only.",
            value =>
            {
                if (int.TryParse(value, out var port)) Port = port;
                return true;
            }),

        new("developer-mode", null,
            "Turn on session developer mode. Debug builds only.",
            _ => { DeveloperMode = true; return false; }),
    ];

    /// <param name="args">The command line.</param>
    /// <param name="invocationDirectory">
    /// The working directory HexIDE was started in, read before anything changes it. Relative paths on the
    /// command line are relative to this, as they are for every other program run from that shell.
    /// </param>
    public static void ParseArgs(string[] args, string invocationDirectory)
    {
        _invocationDirectory = invocationDirectory;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (Match(arg) is { } option)
            {
                var value = option.TakesValue && i + 1 < args.Length ? args[i + 1] : null;
                if (option.Apply(value) && value is not null) i++;
            }
            else if (!arg.StartsWith('-') && !arg.StartsWith('/') &&
                     arg.EndsWith(".vbp", StringComparison.OrdinalIgnoreCase))
            {
                ProjectPath = Path.GetFullPath(arg, _invocationDirectory);
            }
        }
    }

    /// <summary>
    /// The usage text, rendered from <see cref="Options"/> so it cannot fall behind them, laid out for the
    /// console it is going to.
    /// </summary>
    /// <remarks>
    /// Two independent choices, made from <paramref name="target"/>: which mark (colour where the console
    /// can show it, ASCII otherwise), and which layout (the mark beside the text where the rows fit, above
    /// it where they would wrap). Beside keeps the whole of <c>--help</c> on one 24-row screen; stacked
    /// costs a couple of rows more than the text alone, which is the right trade against slicing the mark
    /// into bands — note that between about 94 and 120 columns the option lines do not themselves wrap,
    /// so those rows are a real cost rather than a free one.
    /// </remarks>
    public static string HelpText(ConsoleTarget target)
    {
        var width = Options.Max(o => Spelling(o).Length);

        string[] text =
        [
            "",
            "HexIDE - an open, cross-platform IDE for Visual Basic 6 & VBA.",
            "",
            "Usage:",
            "  HexIDE.Desktop [options] [<project>.vbp]",
            "",
            "Options:",
            .. Options.Select(o => $"  {Spelling(o).PadRight(width)}  {o.Summary}"),
        ];

        var mark = target.Colour ? HelpMark.Colour() : HelpMark.Mono();
        var body = ConsoleLayout.FitsBeside(target.Width, HelpMark.Width, text)
            ? ConsoleLayout.Beside(mark, text, HelpMark.Width)
            : ConsoleLayout.Stacked(mark, text);

        string[] tail =
        [
            "",
            "Both prefixes work: --newproject and /newproject are the same flag, matched",
            "case-insensitively. An argument HexIDE does not recognise is ignored without",
            "complaint, so a flag that appears to do nothing may simply be misspelled.",
            "",
            "Full reference: docs/command-line.md",
        ];

        return string.Join(Environment.NewLine, [.. body, .. tail]);
    }

    private static string Spelling(CommandLineOption option) =>
        option.TakesValue ? $"--{option.Name} {option.ValueName}" : $"--{option.Name}";

    private static CommandLineOption? Match(string arg) =>
        Options.FirstOrDefault(o => IsFlag(arg, o));

    private static bool IsFlag(string arg, CommandLineOption option) =>
        arg.Equals($"--{option.Name}", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals($"/{option.Name}", StringComparison.OrdinalIgnoreCase) ||
        option.AllAliases.Any(alias => arg.Equals(alias, StringComparison.OrdinalIgnoreCase));
}
