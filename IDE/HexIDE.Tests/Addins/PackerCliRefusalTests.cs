using System.Diagnostics;
using HexIDE.AddinPacker;

namespace HexIDE.Tests.Addins;

/// <summary>
/// The packer's dispatch moved from <c>Program.cs</c>'s top-level statements into <c>PackerCli.Run</c> so hexide-pack
/// could call it (#5555). The release's signing job runs that dispatch next to the first-party key, so the move had
/// to be behaviour-neutral. Every expected message and exit code here was read from the packer built from main
/// 9f649b3cd, before the move; the full 43-case comparison is in the PR. Each case refuses before anything is written,
/// which <see cref="Refuses_as_before"/> checks.
/// </summary>
public sealed class PackerCliRefusalTests : IDisposable
{
    private readonly string _work = Directory.CreateTempSubdirectory("packer-refusals-").FullName;

    public void Dispose() => Directory.Delete(_work, true);

    internal static (int ExitCode, string Output, string Error) RunPacker(string workingDirectory, params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
        };
        start.ArgumentList.Add(typeof(PackerCli).Assembly.Location);
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output.Result, error.ReplaceLineEndings("\n"));
    }

    private const string SignPublisherOnce = "error: sign-publisher requires each of --root-public-key, --intermediate-key, "
        + "--intermediate, --publisher-id, --display-name, --public-key and --out exactly once, and takes --namespaces at most once\n";
    private const string VerifyOnce = "error: verify-revocations requires each of --json, --sig and --root exactly once\n";

    private static readonly string[] SignPublisher =
        ["sign-publisher", "--root-public-key", "rp", "--intermediate-key", "ik", "--intermediate", "id",
            "--publisher-id", "acme.tools", "--display-name", "Acme", "--public-key", "pk", "--out", "o"];

    private static readonly string[] Pack =
        ["pack", "--src", "s", "--out", "o", "--id", "com.acme.x", "--title", "T", "--version", "1.0.0",
            "--entry", "e.dll", "--trust", "t"];

    // Expected error text in full, except where it holds a dash a Windows console code page rewrites, or comes from
    // the runtime rather than the packer: those are checked up to that point.
    public static TheoryData<string, string[], string, bool> Cases => new()
    {
        { "no arguments", [], "usage: genkeys <outDir> | sign-publisher --root-public-key .. --intermediate-key .. "
            + "--intermediate .. --publisher-id .. --display-name .. --public-key .. --out .. [--namespaces rdcore.,..] | "
            + "pack --src .. --out .. --id .. --title .. --version .. --entry .. --trust .. [--key ..]\n"
            + "       genkeys-release --root-out .. --intermediate-out .. --firstparty-out .. --public-out ..\n"
            + "         --intermediate-id .. --intermediate-name .. --firstparty-id .. --firstparty-name ..\n"
            + "       sign-revocations --in .. --root-key .. --out .. | verify-revocations --json .. --sig .. --root ..\n", true },
        { "unknown command", ["frob"], "unknown command 'frob'\n", true },
        { "genkeys without a folder", ["genkeys"], "genkeys: missing <outDir>\n", true },
        { "genkeys-release without --public-out", ["genkeys-release", "--root-out", "r", "--intermediate-out", "i",
            "--firstparty-out", "f", "--intermediate-id", "hexide-x", "--intermediate-name", "X", "--firstparty-id",
            "com.x", "--firstparty-name", "Y"], "error: missing required --public-out\n", true },
        { "genkeys-release with a bad id", ["genkeys-release", "--root-out", "r", "--intermediate-out", "i",
            "--firstparty-out", "f", "--public-out", "p", "--intermediate-id", "BAD", "--intermediate-name", "X",
            "--firstparty-id", "com.x", "--firstparty-name", "Y"], "error: invalid publisher or intermediate id\n", true },
        { "genkeys-release with a bad name", ["genkeys-release", "--root-out", "r", "--intermediate-out", "i",
            "--firstparty-out", "f", "--public-out", "p", "--intermediate-id", "hexide-x", "--intermediate-name", " X",
            "--firstparty-id", "com.x", "--firstparty-name", "Y"], "error: invalid display name (expected 1", false },
        { "sign-publisher without --out", SignPublisher[..^2], SignPublisherOnce, true },
        { "sign-publisher with --out twice", [.. SignPublisher, "--out", "o2"], SignPublisherOnce, true },
        { "sign-publisher with an unknown option", [.. SignPublisher, "--colour", "red"], SignPublisherOnce, true },
        { "sign-publisher with a reserved namespace", [.. SignPublisher, "--namespaces", "hexide."],
            "error: 'hexide.' is not a namespace that can be issued: one is a name followed by '.', such as 'rdcore.', "
            + "and 'hexide.' is HexIDE's own\n", true },
        { "sign-publisher with a bad id", ["sign-publisher", "--root-public-key", "rp", "--intermediate-key", "ik",
            "--intermediate", "id", "--publisher-id", "A", "--display-name", "Acme", "--public-key", "pk", "--out", "o"],
            "error: invalid publisher id (expected 3", false },
        { "sign-revocations without --root-key", ["sign-revocations", "--in", "i", "--out", "o"],
            "error: missing required --root-key\n", true },
        { "verify-revocations without --sig", ["verify-revocations", "--json", "j", "--root", "r"], VerifyOnce, true },
        { "verify-revocations with an unknown option", ["verify-revocations", "--json", "j", "--sig", "s", "--root", "r",
            "--x", "y"], VerifyOnce, true },
        { "pack without --trust", Pack[..^2], "error: missing required --trust\n", true },
        { "pack with a bad minimum version", [.. Pack, "--min-hexide-version", "x"], "error: ", false },
        { "pack with an unknown capability", [.. Pack, "--capability", "bogus"],
            "error: invalid capabilities declaration (Parameter 'r')\n", true },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Refuses_as_before(string because, string[] args, string expected, bool whole)
    {
        var (exitCode, output, error) = RunPacker(_work, args);

        exitCode.Should().Be(1, because);
        output.Should().BeEmpty(because);
        if (whole)
            error.Should().Be(expected, because);
        else
            error.Should().StartWith(expected, because);
        Directory.EnumerateFileSystemEntries(_work).Should().BeEmpty("a refusal writes nothing");
    }
}
