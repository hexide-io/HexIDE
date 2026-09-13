using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

/// <summary>
/// Telling a launched pipe server where the workspace is.
/// </summary>
/// <remarks>
/// A stdio server inherits the workspace as its working directory and needs no telling. A pipe server that
/// takes the workspace as a command-line argument had no route to it at all: <c>{pipe}</c> was the only
/// substitution, so an entry could hard-code one absolute path and nothing else — serving exactly one
/// project, on one machine.
///
/// <para>
/// The arguments are read back off the launched process rather than asserted against the resolver, because
/// what matters is the command line the server actually received. These use a shell that echoes its own
/// arguments to standard error, which the transport now captures — so the assertion is on the bytes the
/// child was given.
/// </para>
/// </remarks>
public class NamedPipeWorkspacePlaceholderTests
{
    private sealed class Workspace(string? directory) : ILspWorkspace
    {
        public string? Directory { get; } = directory;
        public IReadOnlyList<LspWorkspaceFolder> Folders { get; } = [];
    }

    /// <summary>A shell that echoes the argument it was given to standard error, then exits.</summary>
    /// <remarks>
    /// The echo is the measurement. `%*` on cmd and `"$@"` on sh both reproduce what the child actually
    /// received, which is the thing under test — a resolver that returned the right string and a process
    /// that was launched with a different one would pass any assertion made on the resolver alone.
    /// </remarks>
    private static NamedPipeLaunch Echoing(string arguments) =>
        OperatingSystem.IsWindows()
            ? new NamedPipeLaunch("cmd.exe", $"/c \" >&2 echo GOT {arguments}& exit 0\"")
            : new NamedPipeLaunch("/bin/sh", $"-c \">&2 echo GOT {arguments}; exit 0\"");

    private static async Task<IReadOnlyList<string>> StderrFromLaunchAsync(
        NamedPipeLaunch launch, ILspWorkspace? workspace, string pipeName)
    {
        await using var transport = new NamedPipeLspTransport(
            pipeName,
            NamedPipeRole.Connect,
            Substitute.For<ILogger<NamedPipeLspTransport>>(),
            launch,
            workspace,
            connectTimeout: TimeSpan.FromSeconds(3));

        var seen = new List<string>();
        var gate = new Lock();
        transport.Notice += (_, n) =>
        {
            if (n.Kind != TransportNoticeKind.StandardError) return;
            lock (gate) seen.Add(n.Text);
        };

        await transport.ConnectAsync(
            new SystemTextJsonFormatter(), TestContext.Current.CancellationToken);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            lock (gate) { if (seen.Count > 0) break; }
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        lock (gate) return [.. seen];
    }

    [Fact]
    public async Task TheWorkspaceUriPlaceholderIsFilledWithTheOpenProject()
    {
        var project = Path.Combine(Path.GetTempPath(), "hexide-ws-uri-probe");
        System.IO.Directory.CreateDirectory(project);

        var lines = await StderrFromLaunchAsync(
            Echoing("--workspace {workspaceUri}"), new Workspace(project), "hexide-test-ws-uri");

        var expected = LspWorkspaceUri.For(project);
        expected.Should().NotBeNull();
        lines.Should().Contain(l => l.Contains(expected!, StringComparison.Ordinal),
            "the server is told the workspace it must analyse, not left to guess");
    }

    [Fact]
    public async Task TheWorkspaceUriMatchesTheRootUriTheClientSends()
    {
        // The two must name the same directory or the server loads one workspace and answers about
        // another — which is not a failure from the client's side, just wrong answers. One shared
        // spelling is what makes the agreement a fact rather than a coincidence.
        var project = Path.Combine(Path.GetTempPath(), "hexide-ws-agreement");
        System.IO.Directory.CreateDirectory(project);

        var placeholder = LspWorkspaceUri.For(project);
        var asTheClientSpellsIt = new Uri(Path.GetFullPath(project)).AbsoluteUri;

        placeholder.Should().Be(asTheClientSpellsIt);
    }

    [Fact]
    public async Task TheWorkspaceDirPlaceholderIsThePlainPath()
    {
        var project = Path.Combine(Path.GetTempPath(), "hexide-ws-dir-probe");
        System.IO.Directory.CreateDirectory(project);

        var lines = await StderrFromLaunchAsync(
            Echoing("--root {workspaceDir}"), new Workspace(project), "hexide-test-ws-dir");

        lines.Should().Contain(l => l.Contains(project, StringComparison.OrdinalIgnoreCase),
            "a server that wants a path rather than a URI is served too");
    }

    [Fact]
    public async Task AnExplicitWorkingDirectoryDoesNotBecomeTheWorkspace()
    {
        // THE DISTINCTION THAT MATTERS, and the first draft of this got it wrong. workingDirectory says
        // where the process RUNS — a server with a required install layout is launched from its own
        // directory — while the workspace is the user's project. Filling the placeholder from the launch
        // directory hands such a server its own installation to analyse: it starts, reports cleanly, and
        // is wrong about everything. A wrong answer, not a failure.
        var installDirectory = Path.Combine(Path.GetTempPath(), "hexide-server-install");
        var project = Path.Combine(Path.GetTempPath(), "hexide-actual-project");
        System.IO.Directory.CreateDirectory(installDirectory);
        System.IO.Directory.CreateDirectory(project);

        var echoing = Echoing("--workspace {workspaceUri}");
        var launch = echoing with { WorkingDirectory = installDirectory };

        var lines = await StderrFromLaunchAsync(launch, new Workspace(project), "hexide-test-ws-split");

        var projectUri = LspWorkspaceUri.For(project)!;
        var installUri = LspWorkspaceUri.For(installDirectory)!;

        lines.Should().Contain(l => l.Contains(projectUri, StringComparison.Ordinal),
            "the placeholder is the project the user opened");
        lines.Should().NotContain(l => l.Contains(installUri, StringComparison.Ordinal),
            "and never where the server happens to be installed");
    }

    [Fact]
    public async Task WithNoProjectOpenTheLaunchIsRefusedAndSaysWhy()
    {
        // Substituting nothing would hand the server a flag with no value, and it would then fail in its
        // own vocabulary — a startup error the reader has to decode back into "no project was open".
        await using var transport = new NamedPipeLspTransport(
            "hexide-test-ws-none",
            NamedPipeRole.Connect,
            Substitute.For<ILogger<NamedPipeLspTransport>>(),
            Echoing("--workspace {workspaceUri}"),
            new Workspace(null),
            connectTimeout: TimeSpan.FromSeconds(3));

        var handler = await transport.ConnectAsync(
            new SystemTextJsonFormatter(), TestContext.Current.CancellationToken);

        handler.Should().BeNull();
        transport.LastFailure.Should().Contain("no project is open",
            "the refusal names the actual cause rather than reporting a connect timeout thirty "
            + "seconds later, which would describe the wrong problem");
    }

    [Fact]
    public async Task AnEntryWithNoPlaceholderNeedsNoWorkspaceAtAll()
    {
        // The behaviour every existing pipe entry already has must not change: no placeholder, no
        // requirement, and no new way to fail.
        var lines = await StderrFromLaunchAsync(
            Echoing("--fixed-argument"), new Workspace(null), "hexide-test-ws-absent");

        lines.Should().Contain(l => l.Contains("--fixed-argument", StringComparison.Ordinal),
            "an entry that never mentions the workspace is launched exactly as before");
    }
}
