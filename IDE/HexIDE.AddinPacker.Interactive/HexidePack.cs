using Spectre.Console;

namespace HexIDE.AddinPacker.Interactive;

/// <summary>
/// hexide-pack (#5555). Given a packer command, it asks for whatever wasn't supplied, shows the whole command and asks
/// to go ahead, then hands the argument list to <paramref name="run"/>, which is <c>PackerCli.Run</c>: it gathers
/// answers and never signs anything itself. It asks only when a required option is missing or invalid, so a complete
/// command runs straight away, and only when <paramref name="terminalBlocker"/> is null and <c>--no-input</c> wasn't
/// given, so the release and a script never stop to wait for input.
/// </summary>
/// <param name="output">Where help, usage and the finished command line go, unwrapped.</param>
/// <param name="terminalBlocker">Why it can't ask on this run, such as "the CI environment variable is set", or
/// null on an interactive terminal.</param>
public sealed class HexidePack(IAnsiConsole console, TextWriter output, TextWriter error, Func<string[], int> run,
    string? terminalBlocker, bool windows)
{
    public int Run(string[] args)
    {
        var commandGiven = args.Length > 0 && !args[0].StartsWith('-');
        var command = commandGiven ? PackerCommands.Find(args[0]) : null;
        if (commandGiven && command is null)
        {
            error.WriteLine($"hexide-pack: there's no command '{args[0]}'. The commands are "
                + string.Join(", ", PackerCommands.All.Select(c => c.Name)) + ".");
            return 1;
        }

        var parsed = Parse(command, commandGiven ? args[1..] : args);
        if (parsed.Errors.Count > 0)
        {
            foreach (var problem in parsed.Errors)
                error.WriteLine($"hexide-pack{(command is null ? "" : " " + command.Name)}: {problem}");
            error.WriteLine(command is null ? "hexide-pack --help lists the commands."
                : $"hexide-pack {command.Name} --help lists its options.");
            return 1;
        }

        var blocker = parsed.NoInput ? "--no-input was given" : terminalBlocker;
        if (parsed.Help)
        {
            // Written plainly, so a terminal or a pipe wraps it rather than the console's idea of the width.
            output.Write(command is null ? Usage() : Help(command));
            return 0;
        }

        if (command is null)
        {
            if (blocker is not null)
            {
                error.Write(Usage());
                error.WriteLine($"It didn't offer the list of commands because {blocker}.");
                return 1;
            }
            command = console.Prompt(new SelectionPrompt<PackerCommand>()
                .Title("Which command?")
                .PageSize(PackerCommands.All.Count)
                .UseConverter(c => $"{Markup.Escape(c.Name),-19}[grey]{Markup.Escape(c.Summary)}[/]")
                .AddChoices(PackerCommands.All));
        }

        var answers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var problems = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var option in command.Options)
        {
            if (!parsed.Given.TryGetValue(option.Name, out var values))
                continue;
            values = values.Select(value => AnswerChecks.Normalise(option, value)).ToList();
            var problem = values.Select(value => AnswerChecks.Check(option, value, AnswerOf(answers))).FirstOrDefault(p => p is not null);
            if (problem is null)
                answers[option.Name] = values;
            else
                problems[option.Name] = problem;
        }

        var missing = command.Options
            .Where(option => (option.Required || AnswerChecks.NeededWhenSkipped(option, AnswerOf(answers)) is not null)
                && !answers.ContainsKey(option.Name) && !problems.ContainsKey(option.Name)).ToArray();
        if (missing.Length == 0 && problems.Count == 0)
            return run(Arguments(command, answers));

        if (blocker is not null)
        {
            foreach (var (name, problem) in problems)
                error.WriteLine($"hexide-pack {command.Name}: {command.Options.Single(o => o.Name == name).Display}: {problem}");
            if (missing.Length > 0)
                error.WriteLine($"hexide-pack {command.Name}: missing {string.Join(", ", missing.Select(o => o.Display))}");
            foreach (var option in missing)
                if (AnswerChecks.NeededWhenSkipped(option, AnswerOf(answers)) is { } needed)
                    error.WriteLine($"hexide-pack {command.Name}: {option.Display}: {needed}");
            error.WriteLine($"It didn't ask {(missing.Length > 0 ? "for them" : "again")} because {blocker}. "
                + $"hexide-pack {command.Name} --help lists the options.");
            return 1;
        }

        console.MarkupLine($"[bold]hexide-pack {Markup.Escape(command.Name)}[/]: {Markup.Escape(command.Summary)}");
        console.MarkupLine("[grey]Enter skips an optional question.[/]");
        foreach (var option in command.Options)
        {
            // A typed answer is checked again here: a file in --src typed before --src was answered couldn't be.
            if (answers.TryGetValue(option.Name, out var typed))
            {
                if (typed.Select(value => AnswerChecks.Check(option, value, AnswerOf(answers))).FirstOrDefault(p => p is not null)
                    is not { } stale)
                    continue;
                answers.Remove(option.Name);
                problems[option.Name] = stale;
            }
            if (problems.TryGetValue(option.Name, out var problem))
                console.MarkupLine($"[red]{Markup.Escape(option.Display)}: {Markup.Escape(problem)}[/]");
            var given = Ask(option, AnswerOf(answers));
            if (given.Count > 0)
                answers[option.Name] = given;
        }

        var arguments = Arguments(command, answers);
        console.WriteLine();
        console.MarkupLine("[grey]As one command, for next time or a script:[/]");
        // Plainly, not through the console, which would break a long line at its width and spoil the paste.
        output.WriteLine(CommandLineText.Format(arguments, windows));
        if (!console.Prompt(new ConfirmationPrompt("Go ahead?") { DefaultValue = true }))
        {
            console.MarkupLine("Nothing was done.");
            return 1;
        }
        return run(arguments);
    }

    private static Func<string, string?> AnswerOf(Dictionary<string, List<string>> answers) =>
        name => answers.TryGetValue(name, out var values) ? values[0] : null;

    private List<string> Ask(PackerOption option, Func<string, string?> answerOf)
    {
        var label = $"[bold]{Markup.Escape(option.Display)}[/] [grey]{Markup.Escape(option.Summary)}[/]";
        if (option.Kind == OptionKind.Capability)
        {
            return console.Prompt(new MultiSelectionPrompt<string>()
                .Title(label)
                .NotRequired()
                .PageSize(PackerCommands.Capabilities.Count)
                .InstructionsText("[grey](Space ticks one, Enter accepts; none is fine)[/]")
                .AddChoices(PackerCommands.Capabilities));
        }

        var answers = new List<string>();
        while (true)
        {
            var prompt = new TextPrompt<string>(option.Repeatable && answers.Count > 0
                    ? $"[bold]{Markup.Escape(option.Display)}[/] [grey]another one[/]" : label)
                .Validate(value => string.IsNullOrEmpty(value) && !option.Required
                    ? AnswerChecks.NeededWhenSkipped(option, answerOf) is { } needed
                        ? ValidationResult.Error($"[red]{Markup.Escape(needed)}[/]")
                        : ValidationResult.Success()
                    : AnswerChecks.Check(option, AnswerChecks.Normalise(option, value), answerOf) is { } problem
                        ? ValidationResult.Error($"[red]{Markup.Escape(problem)}[/]")
                        : ValidationResult.Success());
            if (!option.Required)
                prompt.AllowEmpty();
            if (option.SuggestOnlyDll && answerOf("src") is { } src && Directory.Exists(src)
                && Directory.GetFiles(src, "*.dll", SearchOption.TopDirectoryOnly) is [var only])
                prompt.DefaultValue(Path.GetFileName(only));

            var answer = console.Prompt(prompt);
            if (string.IsNullOrEmpty(answer))
                return answers;
            answers.Add(AnswerChecks.Normalise(option, answer));
            if (!option.Repeatable)
                return answers;
        }
    }

    /// <summary>The command's argument list, in the table's order, each single-valued option once.</summary>
    public static string[] Arguments(PackerCommand command, IReadOnlyDictionary<string, List<string>> answers)
    {
        var arguments = new List<string> { command.Name };
        foreach (var option in command.Options)
        {
            if (!answers.TryGetValue(option.Name, out var values))
                continue;
            foreach (var value in option.Repeatable ? values : values.Take(1))
            {
                if (!option.Positional)
                    arguments.Add(option.Display);
                arguments.Add(value);
            }
        }
        return [.. arguments];
    }

    private sealed record Parsed(Dictionary<string, List<string>> Given, List<string> Errors, bool NoInput, bool Help);

    // Option names match the packer's own: case-insensitively. Unlike the packer, hexide-pack refuses an option it
    // doesn't know and a single-valued option given twice, since either is more likely a slip than a choice.
    private static Parsed Parse(PackerCommand? command, string[] tokens)
    {
        var given = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var errors = new List<string>();
        bool noInput = false, help = false;
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token == "--no-input") { noInput = true; continue; }
            if (token is "--help" or "-h") { help = true; continue; }
            if (command is null)
            {
                errors.Add($"'{token}' isn't something hexide-pack takes before a command");
                continue;
            }

            PackerOption? option;
            string value;
            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                option = command.Options.FirstOrDefault(o => !o.Positional
                    && string.Equals(o.Name, token[2..], StringComparison.OrdinalIgnoreCase));
                if (i + 1 >= tokens.Length)
                {
                    errors.Add($"{token} needs a value");
                    continue;
                }
                value = tokens[++i];
                if (option is null)
                {
                    errors.Add($"there's no option {token}");
                    continue;
                }
            }
            else
            {
                option = command.Options.FirstOrDefault(o => o.Positional);
                value = token;
                if (option is null || given.ContainsKey(option.Name))
                {
                    errors.Add($"'{token}' isn't an option or its value");
                    continue;
                }
            }

            if (!given.TryGetValue(option.Name, out var values))
                given[option.Name] = values = [];
            else if (!option.Repeatable)
            {
                errors.Add($"{option.Display} is given twice");
                continue;
            }
            values.Add(value);
        }
        return new Parsed(given, errors, noInput, help);
    }

    public static string Usage() =>
        "usage: hexide-pack <command> [options] [--no-input]\n"
        + "On a terminal it asks for any option you leave out. --no-input turns that off.\n\n"
        + string.Concat(PackerCommands.All.Select(c => $"  {c.Name,-19}{c.Summary}\n"))
        + "\nhexide-pack <command> --help lists a command's options.\n";

    public static string Help(PackerCommand command)
    {
        string Line(PackerOption o) =>
            $"  {(o.Positional ? o.Display : $"{o.Display} <{Placeholder(o.Kind)}>"),-38}{o.Summary}"
            + (o.Repeatable ? " (any number)" : "")
            + (o.Kind == OptionKind.Capability ? $": {string.Join(", ", PackerCommands.Capabilities)}" : "") + "\n";
        var required = command.Options.Where(o => o.Required).ToArray();
        var optional = command.Options.Where(o => !o.Required).ToArray();
        return $"hexide-pack {command.Name}: {command.Summary}\n\nrequired:\n{string.Concat(required.Select(Line))}"
            + (optional.Length > 0 ? $"\noptional:\n{string.Concat(optional.Select(Line))}" : "");
    }

    private static string Placeholder(OptionKind kind) => kind switch
    {
        OptionKind.ExistingFile or OptionKind.SigningKey => "file",
        OptionKind.ExistingFolder or OptionKind.TrustFolder or OptionKind.OutputFolder or OptionKind.KeyFolder => "folder",
        OptionKind.FileInSource => "file in --src",
        OptionKind.PublisherId => "id",
        OptionKind.DisplayName => "name",
        OptionKind.Namespaces => "namespaces",
        OptionKind.HexideVersion => "version",
        OptionKind.Capability => "capability",
        _ => "text",
    };
}
