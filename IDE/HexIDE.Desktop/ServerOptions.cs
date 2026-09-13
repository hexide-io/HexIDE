using System;
using System.Collections.Generic;
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

        // Summaries stay under 58 characters. The option rows are the widest thing in --help, and the mark
        // sits beside them only while the widest fits in 120 columns — the default width of both Windows
        // consoles and Windows Terminal — with the mark's 24 and the gutter's 2 in front of it. One longer
        // summary silently demotes every default-sized window to the stacked layout.
        new("capture-lsp", null,
            "Record every language-server conversation in full.",
            _ => { CaptureLsp = true; return false; }),

        new("personality", "<vb6|vbaode|vba>",
            "Select the IDE personality for this session.",
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

    public static void ParseArgs(string[] args)
    {
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
                ProjectPath = arg;
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
    /// costs a couple of rows more than the text alone, which is the right trade in a window already too
    /// narrow for the option lines.
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
