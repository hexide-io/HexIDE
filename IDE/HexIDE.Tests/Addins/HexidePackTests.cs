using System.Diagnostics;
using HexIDE.AddinPacker.Interactive;
using Spectre.Console.Testing;

namespace HexIDE.Tests.Addins;

/// <summary>hexide-pack (#5555): it asks for what's missing on a terminal, refuses at once anywhere else, and hands the
/// packer the same argument list it shows.</summary>
public sealed class HexidePackTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hexide-pack-").FullName;
    private readonly List<string[]> _runs = [];
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public void Dispose() => Directory.Delete(_root, true);

    private string Src => Path.Combine(_root, "src");
    private string Trust => Path.Combine(_root, "trust");
    private string Out => Path.Combine(_root, "out");

    private HexidePack Pack(TestConsole? console, string? blocker = null, int exitCode = 0) =>
        new(console ?? new TestConsole(), _output, _error, args => { _runs.Add(args); return exitCode; }, blocker,
            windows: false);

    private void MakeAddinFolders(params string[] dlls) => MakeAddinFolders(keyInTrust: true, dlls);

    private void MakeAddinFolders(bool keyInTrust, params string[] dlls)
    {
        Directory.CreateDirectory(Src);
        foreach (var dll in dlls)
            File.WriteAllText(Path.Combine(Src, dll), "");
        Directory.CreateDirectory(Trust);
        foreach (var name in PackerCommands.TrustFiles)
            File.WriteAllText(Path.Combine(Trust, name), "");
        if (keyInTrust)
            File.WriteAllText(Path.Combine(Trust, "firstparty.key"), "");
    }

    private string KeyElsewhere()
    {
        var key = Path.Combine(_root, "private", "acme.key");
        Directory.CreateDirectory(Path.GetDirectoryName(key)!);
        File.WriteAllText(key, "");
        return key;
    }

    private string[] Complete => ["pack", "--src", Src, "--entry", "A.dll", "--id", "com.acme.a", "--title", "A",
        "--version", "1.0.0", "--trust", Trust, "--out", Out];

    [Fact]
    public void A_complete_command_runs_at_once_without_asking()
    {
        MakeAddinFolders("A.dll");
        var console = new TestConsole().Interactive();

        Pack(console, exitCode: 2).Run(Complete).Should().Be(2, "the packer's exit code comes back unchanged");

        _runs.Should().ContainSingle().Which.Should().Equal(Complete);
        console.Output.Should().BeEmpty();
    }

    [Theory]
    [InlineData("input or output isn't a terminal", false)]
    [InlineData(null, true)]
    public void Off_a_terminal_it_lists_every_missing_option_and_says_why_it_did_not_ask(string? blocker, bool noInput)
    {
        MakeAddinFolders("A.dll");
        string[] args = noInput ? ["pack", "--src", Src, "--no-input"] : ["pack", "--src", Src];

        Pack(new TestConsole().Interactive(), blocker).Run(args).Should().Be(1);

        _runs.Should().BeEmpty();
        _error.ToString().Should().Contain("missing --entry, --id, --title, --version, --trust, --out")
            .And.Contain($"It didn't ask for them because {blocker ?? "--no-input was given"}.");
    }

    [Fact]
    public void It_refuses_an_unknown_option_and_a_repeated_one()
    {
        Pack(null, "input or output isn't a terminal").Run([.. Complete, "--capabilty", "ui.contribute", "--id", "b"])
            .Should().Be(1);

        _runs.Should().BeEmpty();
        _error.ToString().Should().Contain("there's no option --capabilty").And.Contain("--id is given twice");
    }

    [Fact]
    public void On_a_terminal_it_asks_for_each_missing_option_then_shows_the_command_and_asks_to_go_ahead()
    {
        MakeAddinFolders("A.dll");
        var console = new TestConsole().Interactive();
        console.Input.PushTextWithEnter(Src);
        console.Input.PushKey(ConsoleKey.Enter);          // --entry: the only .dll, offered as the default
        console.Input.PushTextWithEnter("com.acme.a");
        console.Input.PushTextWithEnter("A");
        console.Input.PushTextWithEnter("1.0.0");
        console.Input.PushTextWithEnter(Trust);
        console.Input.PushKey(ConsoleKey.Enter);          // --key: skipped, as the trust folder holds firstparty.key
        console.Input.PushTextWithEnter(Out);
        console.Input.PushKey(ConsoleKey.Enter);          // --description: skipped
        console.Input.PushKey(ConsoleKey.Enter);          // --author: skipped
        console.Input.PushKey(ConsoleKey.Spacebar);       // --capability: tick project.read
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);          // --include: none
        console.Input.PushKey(ConsoleKey.Enter);          // --logo: none
        console.Input.PushKey(ConsoleKey.Enter);          // --min-hexide-version: none
        console.Input.PushKey(ConsoleKey.Enter);          // Go ahead? [y/n] (y)

        Pack(console).Run(["pack"]).Should().Be(0);

        string[] expected = [.. Complete, "--capability", "project.read"];
        _runs.Should().ContainSingle().Which.Should().Equal(expected);
        _output.ToString().Should().Contain(CommandLineText.Format(expected, windows: false));
    }

    [Fact]
    public void A_typed_option_that_fails_its_check_is_asked_for_again()
    {
        var console = new TestConsole().Interactive();
        console.Input.PushTextWithEnter("BAD");           // the typed id fails again here, and is asked for once more
        console.Input.PushTextWithEnter("hexide-x");
        console.Input.PushKey(ConsoleKey.Enter);

        var outside = Path.Combine(_root, "keys");
        Pack(console).Run(["genkeys-release", "--root-out", Path.Combine(outside, "root"), "--intermediate-out",
            Path.Combine(outside, "intermediate"), "--firstparty-out", Path.Combine(outside, "firstparty"),
            "--public-out", Path.Combine(outside, "public"), "--intermediate-id", "Bad Id", "--intermediate-name",
            "HexIDE Marketplace", "--firstparty-id", "com.hexide.firstparty", "--firstparty-name", "HexIDE"]).Should().Be(0);

        console.Output.Should().Contain("--intermediate-id: expected 3 to 64 lowercase letters");
        _runs.Should().ContainSingle().Which.Should().ContainInConsecutiveOrder("--intermediate-id", "hexide-x");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void It_refuses_a_folder_for_private_keys_inside_a_git_clone(bool worktree)
    {
        var clone = Path.Combine(_root, "clone");
        Directory.CreateDirectory(clone);
        var stray = Path.Combine(_root, "stray");
        Directory.CreateDirectory(Path.Combine(stray, ".git"));
        if (worktree)
            File.WriteAllText(Path.Combine(clone, ".git"), "gitdir: elsewhere");
        else
        {
            Directory.CreateDirectory(Path.Combine(clone, ".git"));
            File.WriteAllText(Path.Combine(clone, ".git", "HEAD"), "ref: refs/heads/main");
        }

        Pack(null, "input or output isn't a terminal").Run(["genkeys", Path.Combine(clone, "signing", "dev")])
            .Should().Be(1);

        _runs.Should().BeEmpty();
        _error.ToString().Should().Contain("inside a git clone").And.Contain("use HexIDE.AddinPacker genkeys directly");
        AnswerChecks.KeyFolderProblem(Path.Combine(stray, "keys")).Should().BeNull("an empty .git folder isn't a clone to git");
    }

    [Fact]
    public void With_two_dlls_it_suggests_no_entry()
    {
        MakeAddinFolders("A.dll", "B.dll");
        var console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.Enter);          // no default, so an empty --entry is refused
        console.Input.PushTextWithEnter("B.dll");
        for (var skipped = 0; skipped < 6; skipped++)     // the optional ones not typed: --key, then --author to --min-hexide-version
            console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);          // Go ahead?

        Pack(console).Run(["pack", "--src", Src, "--id", "com.acme.a", "--title", "A", "--version", "1.0.0", "--trust",
            Trust, "--out", Out, "--description", "d"]).Should().Be(0);

        _runs.Should().ContainSingle().Which.Should().ContainInConsecutiveOrder("--entry", "B.dll")
            .And.ContainInConsecutiveOrder("--description", "d");
    }

    [Fact]
    public void Declining_to_go_ahead_runs_nothing()
    {
        var console = new TestConsole().Interactive();
        console.Input.PushTextWithEnter(Path.Combine(_root, "keys"));
        console.Input.PushTextWithEnter("n");

        Pack(console).Run(["genkeys"]).Should().Be(1);

        _runs.Should().BeEmpty();
        console.Output.Should().Contain("Nothing was done.");
    }

    [Fact]
    public void With_no_command_on_a_terminal_it_offers_the_commands()
    {
        var console = new TestConsole().Interactive();
        for (var i = 0; i < PackerCommands.All.Count - 1; i++)
            console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);          // the last command, genkeys
        console.Input.PushTextWithEnter(Path.Combine(_root, "keys"));
        console.Input.PushKey(ConsoleKey.Enter);

        Pack(console).Run([]).Should().Be(0);

        _runs.Should().ContainSingle().Which.Should().Equal("genkeys", Path.Combine(_root, "keys"));
    }

    [Fact]
    public void Off_a_terminal_with_no_command_it_prints_the_commands_and_fails()
    {
        Pack(null, "the CI environment variable is set").Run([]).Should().Be(1);

        _error.ToString().Should().Contain("usage: hexide-pack <command>")
            .And.Contain("It didn't offer the list of commands because the CI environment variable is set.");
    }

    [Theory]
    [InlineData(@"C:\Program Files\Acme", @"""C:\Program Files\Acme""")]
    [InlineData("Cost$ave", "'Cost$ave'")]
    [InlineData(@"say ""hi""", @"'say ""hi""'")]
    [InlineData("it's", @"""it's""")]
    [InlineData("plain-1.0", "plain-1.0")]
    public void The_Windows_line_quotes_for_PowerShell_and_cmd(string value, string quoted) =>
        CommandLineText.Quote(value, windows: true).Should().Be(quoted);

    public static bool HasPosixShell => !OperatingSystem.IsWindows() && File.Exists("/bin/sh");

    [Fact(Skip = "Needs a POSIX shell", SkipUnless = nameof(HasPosixShell))]
    public void The_POSIX_line_pastes_back_to_the_same_arguments()
    {
        string[] arguments = ["pack", "--title", "it's \"A\" $HOME `x` \\ * ?", "--src", "/tmp/a b"];
        var start = new ProcessStartInfo("/bin/sh") { RedirectStandardOutput = true };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("printf '%s\\n' " + CommandLineText.Format(arguments, windows: false)["hexide-pack ".Length..]);
        using var process = Process.Start(start)!;
        var echoed = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        echoed.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().Equal(arguments);
    }

    // #5318: the private key can live outside the trust folder, so a trust folder of public files is enough with --key.
    [Fact]
    public void Off_a_terminal_a_trust_folder_without_a_key_needs_key_and_says_why()
    {
        MakeAddinFolders(keyInTrust: false, "A.dll");

        Pack(null, "input or output isn't a terminal").Run(Complete).Should().Be(1);

        _runs.Should().BeEmpty();
        _error.ToString().Should().Contain("missing --key").And.Contain($"'{Trust}' has no firstparty.key, so give your key's file");
    }

    [Fact]
    public void With_key_given_a_trust_folder_of_public_files_runs_at_once()
    {
        MakeAddinFolders(keyInTrust: false, "A.dll");
        var key = KeyElsewhere();
        var console = new TestConsole().Interactive();

        Pack(console).Run([.. Complete, "--key", key]).Should().Be(0);

        _runs.Should().ContainSingle().Which.Should().ContainInConsecutiveOrder("--trust", Trust, "--key", key);
        console.Output.Should().BeEmpty();
    }

    [Fact]
    public void On_a_terminal_key_cannot_be_skipped_without_firstparty_key_in_the_trust_folder()
    {
        MakeAddinFolders(keyInTrust: false, "A.dll");
        var key = KeyElsewhere();
        var console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.Enter);          // --key: refused, the trust folder holds no key
        console.Input.PushTextWithEnter(Path.Combine(_root, "private", "missing.key"));   // refused: no such file
        console.Input.PushTextWithEnter(key);
        for (var skipped = 0; skipped < 6; skipped++)     // --description to --min-hexide-version
            console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);          // Go ahead?

        Pack(console).Run(Complete).Should().Be(0);

        // The console wraps at its width, so each check is a part that fits on one line.
        console.Output.Should().Contain("firstparty.key, so give your key's file").And.Contain("there's no file at");
        _runs.Should().ContainSingle().Which.Should().ContainInConsecutiveOrder("--key", key);
    }
}
