using System.Text.Json;
using HexIDE.Conversations;
using HexIDE.Lsp;
using HexIDE.Lsp.Providers;
using HexIDE.Redaction;
using Microsoft.Extensions.Logging.Abstractions;

namespace HexIDE.Tests.LspClient;

public sealed class WorkspaceArtifactGenerationTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly IWorkspaceArtifactConsent _consent = Substitute.For<IWorkspaceArtifactConsent>();
    private readonly Provider _provider = new();
    private readonly WorkspaceArtifactService _service;

    public WorkspaceArtifactGenerationTests()
    {
        // Canonical, as the service resolves it: macOS's temporary folder runs through a link (/var is /private/var), and
        // the service hands consent and the ownership record the resolved path.
        _root = WorkspaceArtifactService.Resolve(Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "hexide-artifact-" + Guid.NewGuid().ToString("N"))).FullName);
        _workspace = new Workspace(_root);
        _consent.AllowAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<WorkspaceArtifactSpec>()).Returns(true);
        _service = new WorkspaceArtifactService(_workspace,
            new WorkspaceArtifactProviderRegistry([_provider]), _consent,
            NullLogger<WorkspaceArtifactService>.Instance);
    }

    private LanguageServerRegistration Server(string path = "generated.json") => new(
        "probe", "Probe server", [".c"], "c", () => Substitute.For<ILspClient>(),
        WorkspaceArtifact: new(path, "probe"));

    [Fact]
    public async Task IdenticalRegenerationPreservesWriteTime()
    {
        await _service.PrepareAsync(Server(), TestContext.Current.CancellationToken);
        var target = Path.Combine(_root, "generated.json");
        File.SetLastWriteTimeUtc(target, DateTime.UtcNow.AddDays(-1));
        var before = File.GetLastWriteTimeUtc(target);
        await _service.PrepareAsync(Server(), TestContext.Current.CancellationToken);
        File.GetLastWriteTimeUtc(target).Should().Be(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedWritePreservesTargetAndAllowsRetry(bool existing)
    {
        var target = Path.Combine(_root, "generated.json");
        if (existing) await _service.PrepareAsync(Server(), TestContext.Current.CancellationToken);
        _provider.Content = "replacement";
        _service.WriteContent = (stream, bytes) =>
        {
            stream.Write(bytes.AsSpan(0, 2));
            throw new IOException("interrupted write");
        };
        await _service.PrepareAsync(Server(), TestContext.Current.CancellationToken);
        if (existing) File.ReadAllText(target).Should().Be("first");
        else File.Exists(target).Should().BeFalse();
        Directory.GetFiles(_root).Should().HaveCount(existing ? 1 : 0);
        _service.WriteContent = (stream, bytes) => stream.Write(bytes);
        await _service.PrepareAsync(Server(), TestContext.Current.CancellationToken);
        File.ReadAllText(target).Should().Be("replacement");
    }

    [Theory]
    [InlineData("path", "Refused")]
    [InlineData("denial", "denied")]
    [InlineData("provider", "IOException")]
    [InlineData("write", "not written")]
    [InlineData("foreign", "Leaving existing")]
    public async Task RefusalReasonIsRecordedOnTheConnection(string reason, string detail)
    {
        await using var capture = new ConversationLog();
        using var service = new WorkspaceArtifactService(_workspace,
            new WorkspaceArtifactProviderRegistry([_provider]), _consent,
            NullLogger<WorkspaceArtifactService>.Instance, capture: capture);
        if (reason == "denial")
            _consent.AllowAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<WorkspaceArtifactSpec>()).Returns(false);
        if (reason == "provider") _provider.Failure = new IOException("provider failed");
        if (reason == "write") service.WriteContent = (_, _) => throw new IOException("write failed");
        if (reason == "foreign") File.WriteAllText(Path.Combine(_root, "generated.json"), "foreign");
        var launched = false;
        var server = Server(reason == "path" ? "../escape.json" : "generated.json") with
        {
            CreateClient = () => { launched = true; return Substitute.For<ILspClient>(); }
        };
        await using var registry = new LspClientRegistry([server], NullLogger<LspClientRegistry>.Instance,
            _workspace, artifacts: service);
        await registry.OpenDocumentAsync(new Uri(Path.Combine(_root, "probe.c")).AbsoluteUri, "int x;", TestContext.Current.CancellationToken);
        await capture.DrainAsync();
        launched.Should().BeTrue();
        capture.Snapshot("probe").Should().Contain(e => e.Kind == ConversationEntryKind.Note
            && e.Direction == ConversationDirection.Local && e.Fault && e.Detail!.Contains(detail));
    }

    [Theory]
    [InlineData("path", "Refused")]
    [InlineData("denial", "denied")]
    [InlineData("provider", "IOException")]
    [InlineData("write", "IOException")]
    [InlineData("foreign", "Leaving existing")]
    public async Task RefusalExportsDoNotExposeWorkspacePaths(string reason, string detail)
    {
        await using var capture = new ConversationLog();
        using var service = new WorkspaceArtifactService(_workspace,
            new WorkspaceArtifactProviderRegistry([_provider]), _consent,
            NullLogger<WorkspaceArtifactService>.Instance, capture: capture);
        if (reason == "denial")
            _consent.AllowAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<WorkspaceArtifactSpec>()).Returns(false);
        if (reason == "provider") _provider.Failure = new IOException($"Provider failed at {_root}");
        if (reason == "write") service.WriteContent = (_, _) => throw new IOException($"Write failed at {_root}");
        if (reason == "foreign") File.WriteAllText(Path.Combine(_root, "generated.json"), "foreign");
        var relative = reason == "path" ? "../escape.json" : "generated.json";
        await service.PrepareAsync(Server(relative), TestContext.Current.CancellationToken);
        await capture.DrainAsync();
        var export = await ConversationExporter.ExportAsync(capture,
            new ConversationRedactor(new Pseudonymiser(new Random(21)), true));
        export.Messages.Should().NotContain(Path.GetFileName(_root));
        export.Manifest.Should().NotContain(Path.GetFileName(_root));
        capture.Snapshot("probe").Should().Contain(e => e.Kind == ConversationEntryKind.Note
            && e.Detail!.Contains(detail) && e.Detail.Contains(relative));
    }

    [Fact]
    public async Task WritesBeforeTheClientFactoryRunsAndRegeneratesOwnedContent()
    {
        var sawGeneratedFile = false;
        var server = Server() with { CreateClient = () =>
        {
            sawGeneratedFile = File.ReadAllText(Path.Combine(_root, "generated.json")) == "first";
            return Substitute.For<ILspClient>();
        }};
        await using var registry = new LspClientRegistry([server], NullLogger<LspClientRegistry>.Instance,
            _workspace, artifacts: _service);
        await registry.OpenDocumentAsync(new Uri(Path.Combine(_root, "probe.c")).AbsoluteUri, "int x;", TestContext.Current.CancellationToken);
        sawGeneratedFile.Should().BeTrue();
        _provider.Content = "second";
        await _service.PrepareAsync(server, TestContext.Current.CancellationToken);
        File.ReadAllText(Path.Combine(_root, "generated.json")).Should().Be("second");
        await _consent.Received().AllowAsync("probe", "Probe server", _root, Path.Combine(_root, "generated.json"), Arg.Any<WorkspaceArtifactSpec>());
    }

    [Fact]
    public async Task MaterialChangesRegenerateAfterDebounce()
    {
        await _service.PrepareAsync(Server(), TestContext.Current.CancellationToken);
        var initial = _provider.Calls;
        _provider.Content = "changed";
        _workspace.Change();
        _workspace.Change();
        _workspace.Change();
        await Task.Delay(700, TestContext.Current.CancellationToken);
        File.ReadAllText(Path.Combine(_root, "generated.json")).Should().Be("changed");
        _provider.Calls.Should().Be(initial + 1);
    }

    [Theory]
    [InlineData("/tmp/escape.json")]
    [InlineData("C:escape.json")]
    [InlineData("C:\\escape.json")]
    [InlineData("../escape.json")]
    [InlineData("..\\escape.json")]
    public async Task UnsafePathNeverWritesOrPrompts(string path)
    {
        await _service.PrepareAsync(Server(path), TestContext.Current.CancellationToken);
        _provider.Calls.Should().Be(0);
        await _consent.DidNotReceiveWithAnyArgs().AllowAsync(default!, default!, default!, default!, default!);
    }

    [Fact]
    public async Task LinkLeadingOutsideIsRefused()
    {
        var outside = Path.Combine(Path.GetTempPath(), "hexide-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(_root, "link"), outside);
            await _service.PrepareAsync(Server("link/generated.json"), TestContext.Current.CancellationToken);
            File.Exists(Path.Combine(outside, "generated.json")).Should().BeFalse();
            _provider.Calls.Should().Be(0);
            await _consent.DidNotReceiveWithAnyArgs().AllowAsync(default!, default!, default!, default!, default!);
        }
        finally { Directory.Delete(outside, true); }
    }

    [Fact]
    public async Task ConsentIsScopedToTheResolvedWorkspaceRoot()
    {
        var real = Path.Combine(_root, "real");
        var alias = Path.Combine(_root, "alias");
        Directory.CreateDirectory(real);
        Directory.CreateSymbolicLink(alias, real);
        _workspace.Projects = [new(alias, new("project", null, [], []))];
        await _service.PrepareAsync(Server(), TestContext.Current.CancellationToken);
        File.ReadAllText(Path.Combine(real, "generated.json")).Should().Be("first");
        await _consent.Received().AllowAsync("probe", "Probe server", real, Path.Combine(real, "generated.json"), Arg.Any<WorkspaceArtifactSpec>());
    }

    [Fact]
    public async Task ALinkWhoseTargetRunsThroughAnotherLinkResolvesToTheRealFolder()
    {
        // macOS's temporary folder is such a path: /var is a link to /private/var, so a link created there names a
        // target that runs through it. A root resolved only as far as the last link failed its own containment check.
        var real = Path.Combine(_root, "real");
        Directory.CreateDirectory(Path.Combine(real, "inner"));
        var hop = Path.Combine(_root, "hop");
        Directory.CreateSymbolicLink(hop, real);
        var workspace = Path.Combine(_root, "workspace");
        Directory.CreateSymbolicLink(workspace, Path.Combine(hop, "inner"));
        _workspace.Projects = [new(workspace, new("project", null, [], []))];
        await _service.PrepareAsync(Server(), TestContext.Current.CancellationToken);
        var inner = Path.Combine(real, "inner");
        File.ReadAllText(Path.Combine(inner, "generated.json")).Should().Be("first");
        await _consent.Received().AllowAsync("probe", "Probe server", inner, Path.Combine(inner, "generated.json"),
            Arg.Any<WorkspaceArtifactSpec>());
    }

    [Theory]
    [InlineData(false, "first")]
    [InlineData(true, "first")]
    [InlineData(true, null)]
    public async Task LibrariesAreReadOnlyOnceTheWorkspaceHasConsented(bool allowed, string? content)
    {
        // A library's name is read by loading it, from a path the project file names: a project nobody allowed an artefact
        // for, or one with nothing to describe, never gets that far (#2071's security review).
        var names = Substitute.For<IWorkspaceLibraryNames>();
        names.NameOf(Arg.Any<WorkspaceProjectReference>()).Returns("named");
        using var service = new WorkspaceArtifactService(_workspace,
            new WorkspaceArtifactProviderRegistry([_provider]), _consent,
            NullLogger<WorkspaceArtifactService>.Instance, libraryNames: names);
        _consent.AllowAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<WorkspaceArtifactSpec>()).Returns(allowed);
        _provider.Content = content;
        await service.PrepareAsync(Server(), TestContext.Current.CancellationToken);
        var target = Path.Combine(_root, "generated.json");
        if (allowed && content is not null)
        {
            names.ReceivedWithAnyArgs(1).NameOf(default!);
            File.ReadAllText(target).Should().Be("first named");
        }
        else
        {
            names.DidNotReceiveWithAnyArgs().NameOf(default!);
            File.Exists(target).Should().BeFalse();
        }
    }

    [Fact]
    public async Task ExistingForeignFileIsLeftAloneWithoutPrompt()
    {
        var target = Path.Combine(_root, "generated.json");
        File.WriteAllText(target, "CMake owns this");
        await _service.PrepareAsync(Server(), TestContext.Current.CancellationToken);
        File.ReadAllText(target).Should().Be("CMake owns this");
        await _consent.DidNotReceiveWithAnyArgs().AllowAsync(default!, default!, default!, default!, default!);
    }

    [Fact]
    public async Task EditingOwnedFileRelinquishesOwnership()
    {
        var target = Path.Combine(_root, "generated.json");
        await _service.PrepareAsync(Server(), TestContext.Current.CancellationToken);
        File.WriteAllText(target, "user edit");
        _consent.ClearReceivedCalls();
        await _service.PrepareAsync(Server(), TestContext.Current.CancellationToken);
        File.ReadAllText(target).Should().Be("user edit");
        await _consent.DidNotReceiveWithAnyArgs().AllowAsync(default!, default!, default!, default!, default!);
    }

    [Fact]
    public async Task DenialStillLaunchesServerWithoutWriting()
    {
        _consent.AllowAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<WorkspaceArtifactSpec>()).Returns(false);
        var launched = false;
        var server = Server() with { CreateClient = () => { launched = true; return Substitute.For<ILspClient>(); } };
        await using var registry = new LspClientRegistry([server], NullLogger<LspClientRegistry>.Instance,
            _workspace, artifacts: _service);
        await registry.OpenDocumentAsync(new Uri(Path.Combine(_root, "probe.c")).AbsoluteUri, "int x;", TestContext.Current.CancellationToken);
        launched.Should().BeTrue();
        File.Exists(Path.Combine(_root, "generated.json")).Should().BeFalse();
        await _consent.Received().AllowAsync("probe", "Probe server", _root, Path.Combine(_root, "generated.json"), Arg.Any<WorkspaceArtifactSpec>());
    }

    [Fact]
    public void ClangdProviderUsesSettingsAndOnlyCarriedSources()
    {
        using var settings = JsonDocument.Parse("""{"arguments":["clang","-DHEXIDE_PROBE","-Ipath with spaces"]}""");
        var files = new[] { "probe.c", Path.Combine("sub", "other.cpp"), "notes.md", "header.h" }.Select(f =>
            new WorkspaceProjectFile(f, Path.Combine(_root, f), WorkspaceProjectFileKind.Carried))
            .Append(new("module.c", Path.Combine(_root, "module.c"), WorkspaceProjectFileKind.Module)).ToArray();
        var context = new WorkspaceArtifactContext(_root, new("project", null, files, []), settings.RootElement);
        var output = new ClangdCompilationDatabaseProvider().Produce(context);
        using var json = JsonDocument.Parse(output!);
        json.RootElement.GetArrayLength().Should().Be(3);
        json.RootElement[1].GetProperty("directory").GetString().Should().Be(Path.Combine(_root, "sub"));
        var command = json.RootElement[0];
        command.GetProperty("directory").GetString().Should().Be(_root);
        command.GetProperty("file").GetString().Should().Be(Path.Combine(_root, "probe.c"));
        command.GetProperty("arguments").EnumerateArray().Select(a => a.GetString()).Should()
            .Equal("clang", "-DHEXIDE_PROBE", "-Ipath with spaces", Path.Combine(_root, "probe.c"));
    }

    [Fact]
    public async Task ClangdWithNoSourcesPromptsForNothingAndWritesNothing()
    {
        using var service = new WorkspaceArtifactService(_workspace,
            new WorkspaceArtifactProviderRegistry([new ClangdCompilationDatabaseProvider()]), _consent,
            NullLogger<WorkspaceArtifactService>.Instance);
        var notes = new WorkspaceProjectFile("notes.md", Path.Combine(_root, "notes.md"), WorkspaceProjectFileKind.Carried);
        _workspace.Projects = [new(_root, new("project", null, [notes], []))];
        await service.PrepareAsync(Server("compile_commands.json") with { WorkspaceArtifact = new("compile_commands.json", "clangd") },
            TestContext.Current.CancellationToken);
        File.Exists(Path.Combine(_root, "compile_commands.json")).Should().BeFalse();
        await _consent.DidNotReceiveWithAnyArgs().AllowAsync(default!, default!, default!, default!, default!);
    }

    [Fact]
    public async Task RemovingLastSourceDeletesTheOwnedCompilationDatabase()
    {
        await using var capture = new ConversationLog();
        using var service = new WorkspaceArtifactService(_workspace,
            new WorkspaceArtifactProviderRegistry([new ClangdCompilationDatabaseProvider()]), _consent,
            NullLogger<WorkspaceArtifactService>.Instance, capture: capture);
        var source = new WorkspaceProjectFile("probe.c", Path.Combine(_root, "probe.c"), WorkspaceProjectFileKind.Carried);
        _workspace.Projects = [new(_root, new("project", null, [source], []))];
        var server = Server("compile_commands.json") with { WorkspaceArtifact = new("compile_commands.json", "clangd") };
        await service.PrepareAsync(server, TestContext.Current.CancellationToken);
        using (var initial = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "compile_commands.json"))))
            initial.RootElement.GetArrayLength().Should().Be(1);
        _workspace.Projects = [new(_root, new("project", null, [], []))];
        _consent.ClearReceivedCalls();
        await service.PrepareAsync(server, TestContext.Current.CancellationToken);
        File.Exists(Path.Combine(_root, "compile_commands.json")).Should().BeFalse();
        await _consent.DidNotReceiveWithAnyArgs().AllowAsync(default!, default!, default!, default!, default!);
        await capture.DrainAsync();
        capture.Snapshot("probe").Should().Contain(e => e.Kind == ConversationEntryKind.Note && !e.Fault
            && e.Detail!.Contains("Removed workspace artefact compile_commands.json"));
    }

    [Fact]
    public async Task NothingToDescribeLeavesAnEditedFileWithItsReason()
    {
        await using var capture = new ConversationLog();
        using var service = new WorkspaceArtifactService(_workspace,
            new WorkspaceArtifactProviderRegistry([_provider]), _consent,
            NullLogger<WorkspaceArtifactService>.Instance, capture: capture);
        var target = Path.Combine(_root, "generated.json");
        await service.PrepareAsync(Server(), TestContext.Current.CancellationToken);
        File.WriteAllText(target, "user edit");
        _provider.Content = null;
        await service.PrepareAsync(Server(), TestContext.Current.CancellationToken);
        File.ReadAllText(target).Should().Be("user edit");
        await capture.DrainAsync();
        capture.Snapshot("probe").Should().Contain(e => e.Kind == ConversationEntryKind.Note && e.Fault
            && e.Detail!.Contains("Leaving existing workspace artefact generated.json alone"));
    }

    [Fact]
    public async Task NothingToDescribeRefusesALinkLeadingOutsideBeforeDeleting()
    {
        var outside = Path.Combine(Path.GetTempPath(), "hexide-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            var inside = Path.Combine(_root, "link");
            Directory.CreateDirectory(inside);
            await _service.PrepareAsync(Server("link/generated.json"), TestContext.Current.CancellationToken);
            File.Copy(Path.Combine(inside, "generated.json"), Path.Combine(outside, "generated.json"));
            Directory.Delete(inside, true);
            Directory.CreateSymbolicLink(inside, outside);
            _provider.Content = null;
            await _service.PrepareAsync(Server("link/generated.json"), TestContext.Current.CancellationToken);
            File.ReadAllText(Path.Combine(outside, "generated.json")).Should().Be("first");
        }
        finally { Directory.Delete(outside, true); }
    }

    [Fact]
    public async Task NothingToDescribeWithNoFileDoesNothing()
    {
        _provider.Content = null;
        await _service.PrepareAsync(Server(), TestContext.Current.CancellationToken);
        File.Exists(Path.Combine(_root, "generated.json")).Should().BeFalse();
        _provider.Calls.Should().Be(1);
        await _consent.DidNotReceiveWithAnyArgs().AllowAsync(default!, default!, default!, default!, default!);
    }

    [Fact]
    public async Task DirectoryAtTheTargetIsLeftAloneAndTheServerStillLaunches()
    {
        await using var capture = new ConversationLog();
        using var service = new WorkspaceArtifactService(_workspace,
            new WorkspaceArtifactProviderRegistry([_provider]), _consent,
            NullLogger<WorkspaceArtifactService>.Instance, capture: capture);
        Directory.CreateDirectory(Path.Combine(_root, "generated.json"));
        var launched = false;
        var server = Server() with { CreateClient = () => { launched = true; return Substitute.For<ILspClient>(); } };
        await using var registry = new LspClientRegistry([server], NullLogger<LspClientRegistry>.Instance,
            _workspace, artifacts: service);
        await registry.OpenDocumentAsync(new Uri(Path.Combine(_root, "probe.c")).AbsoluteUri, "int x;", TestContext.Current.CancellationToken);
        await capture.DrainAsync();
        launched.Should().BeTrue();
        Directory.Exists(Path.Combine(_root, "generated.json")).Should().BeTrue();
        await _consent.DidNotReceiveWithAnyArgs().AllowAsync(default!, default!, default!, default!, default!);
        capture.Snapshot("probe").Should().Contain(e => e.Kind == ConversationEntryKind.Note && e.Fault
            && e.Detail!.Contains("Leaving existing workspace artefact generated.json alone"));
    }

    [Fact]
    public async Task OversizedFileIsNeverOwnedEvenWhenItsHashIsRecorded()
    {
        var target = Path.Combine(_root, "generated.json");
        using (var file = new FileStream(target, FileMode.CreateNew))
            file.SetLength(WorkspaceArtifactService.MaxArtifactLength + 1);
        string hash;
        using (var file = File.OpenRead(target))
            hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file));
        var state = Path.Combine(_root, "state.json");
        File.WriteAllText(state, JsonSerializer.Serialize(new Dictionary<string, string> { [target] = hash }));
        using var service = new WorkspaceArtifactService(_workspace, new WorkspaceArtifactProviderRegistry([_provider]),
            _consent, NullLogger<WorkspaceArtifactService>.Instance, state);
        await service.PrepareAsync(Server(), TestContext.Current.CancellationToken);
        new FileInfo(target).Length.Should().Be(WorkspaceArtifactService.MaxArtifactLength + 1);
        await _consent.DidNotReceiveWithAnyArgs().AllowAsync(default!, default!, default!, default!, default!);
    }

    [Fact]
    public async Task OversizedContentIsNotWritten()
    {
        await using var capture = new ConversationLog();
        using var service = new WorkspaceArtifactService(_workspace,
            new WorkspaceArtifactProviderRegistry([_provider]), _consent,
            NullLogger<WorkspaceArtifactService>.Instance, capture: capture);
        _provider.Content = new string('x', (int)WorkspaceArtifactService.MaxArtifactLength + 1);
        await service.PrepareAsync(Server(), TestContext.Current.CancellationToken);
        await capture.DrainAsync();
        File.Exists(Path.Combine(_root, "generated.json")).Should().BeFalse();
        capture.Snapshot("probe").Should().Contain(e => e.Kind == ConversationEntryKind.Note && e.Fault
            && e.Detail!.Contains("larger than any artefact"));
    }

    [Fact]
    public async Task EmptyContentIsNothingToDescribe()
    {
        _provider.Content = "";
        await _service.PrepareAsync(Server(), TestContext.Current.CancellationToken);
        File.Exists(Path.Combine(_root, "generated.json")).Should().BeFalse();
        await _consent.DidNotReceiveWithAnyArgs().AllowAsync(default!, default!, default!, default!, default!);
    }

    public static bool OnUnix => !OperatingSystem.IsWindows();

    [Fact(Skip = "FIFOs are a Unix filesystem object.", SkipUnless = nameof(OnUnix), Timeout = 10_000)]
    public async Task FifoAtTheTargetIsLeftAloneWithoutBeingOpened()
    {
        var target = Path.Combine(_root, "generated.json");
        using (var mkfifo = System.Diagnostics.Process.Start("mkfifo", [target])!)
            await mkfifo.WaitForExitAsync(TestContext.Current.CancellationToken);
        var state = Path.Combine(_root, "state.json");
        File.WriteAllText(state, JsonSerializer.Serialize(new Dictionary<string, string> { [target] = "00" }));
        using var service = new WorkspaceArtifactService(_workspace, new WorkspaceArtifactProviderRegistry([_provider]),
            _consent, NullLogger<WorkspaceArtifactService>.Instance, state);
        await service.PrepareAsync(Server(), TestContext.Current.CancellationToken);
        await _consent.DidNotReceiveWithAnyArgs().AllowAsync(default!, default!, default!, default!, default!);
    }

    [Fact]
    public async Task TheSameReasonIsRecordedOnceUntilSomethingIsWritten()
    {
        await using var capture = new ConversationLog();
        using var service = new WorkspaceArtifactService(_workspace,
            new WorkspaceArtifactProviderRegistry([_provider]), _consent,
            NullLogger<WorkspaceArtifactService>.Instance, capture: capture);
        _consent.AllowAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<WorkspaceArtifactSpec>())
            .Returns(false, false, true, false);
        await service.PrepareAsync(Server(), TestContext.Current.CancellationToken);
        await service.PrepareAsync(Server(), TestContext.Current.CancellationToken);
        await capture.DrainAsync();
        capture.Snapshot("probe").Count(e => e.Detail?.Contains("denied") == true).Should().Be(1);
        await service.PrepareAsync(Server(), TestContext.Current.CancellationToken);
        File.Delete(Path.Combine(_root, "generated.json"));
        await service.PrepareAsync(Server(), TestContext.Current.CancellationToken);
        await capture.DrainAsync();
        capture.Snapshot("probe").Count(e => e.Detail?.Contains("denied") == true)
            .Should().Be(2, "a write came between the two denials, so the second is news");
    }

    [Fact]
    public async Task ARefusedDeclarationIsNotedOnTheConnection()
    {
        await using var capture = new ConversationLog();
        using var service = new WorkspaceArtifactService(_workspace,
            new WorkspaceArtifactProviderRegistry([_provider]), _consent,
            NullLogger<WorkspaceArtifactService>.Instance, capture: capture);
        var server = Server() with { WorkspaceArtifact = null, WorkspaceArtifactRefused = true };
        await service.PrepareAsync(server, TestContext.Current.CancellationToken);
        await service.PrepareAsync(server, TestContext.Current.CancellationToken);
        await capture.DrainAsync();
        capture.Snapshot("probe").Should().ContainSingle(e => e.Kind == ConversationEntryKind.Note && e.Fault
            && e.Detail!.Contains("declaration refused") && e.Detail.Contains("Tools > Language Servers"));
        _provider.Calls.Should().Be(0);
        await _consent.DidNotReceiveWithAnyArgs().AllowAsync(default!, default!, default!, default!, default!);
    }

    [Fact]
    public async Task FileCreatedDuringConsentIsNotClobbered()
    {
        var target = Path.Combine(_root, "generated.json");
        _consent.AllowAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<WorkspaceArtifactSpec>())
            .Returns(_ => { File.WriteAllText(target, "another writer"); return true; });
        await _service.PrepareAsync(Server(), TestContext.Current.CancellationToken);
        File.ReadAllText(target).Should().Be("another writer");
        await _consent.Received().AllowAsync("probe", "Probe server", _root, target, Arg.Any<WorkspaceArtifactSpec>());
    }

    [Fact]
    public async Task OwnershipSurvivesRestart()
    {
        var state = Path.Combine(_root, "state.json");
        var providers = new WorkspaceArtifactProviderRegistry([_provider]);
        using (var first = new WorkspaceArtifactService(_workspace, providers, _consent,
                   NullLogger<WorkspaceArtifactService>.Instance, state))
            await first.PrepareAsync(Server(), TestContext.Current.CancellationToken);
        _provider.Content = "after restart";
        using var next = new WorkspaceArtifactService(_workspace, providers, _consent,
            NullLogger<WorkspaceArtifactService>.Instance, state);
        await next.PrepareAsync(Server(), TestContext.Current.CancellationToken);
        File.ReadAllText(Path.Combine(_root, "generated.json")).Should().Be("after restart");
    }

    [Fact]
    public async Task NoDeclarationProducesNothing()
    {
        await _service.PrepareAsync(Server() with { WorkspaceArtifact = null }, TestContext.Current.CancellationToken);
        _provider.Calls.Should().Be(0);
        await _consent.DidNotReceiveWithAnyArgs().AllowAsync(default!, default!, default!, default!, default!);
    }

    public void Dispose()
    {
        _service.Dispose();
        Directory.Delete(_root, true);
    }

    private sealed class Provider : IWorkspaceArtifactProvider
    {
        public string Name => "probe";
        public string? Content { get; set; } = "first";
        public int Calls { get; private set; }
        public Exception? Failure { get; set; }
        public string? Produce(WorkspaceArtifactContext context)
        {
            Calls++;
            if (Failure is not null) throw Failure;
            return Content is null || context.LibraryNames is not { } names ? Content
                : Content + " " + names.NameOf(new WorkspaceProjectReference("library"));
        }
    }

    internal sealed class Workspace(string root) : IWorkspaceArtifactWorkspace
    {
        public string Directory => root;
        public IReadOnlyList<LspWorkspaceFolder> Folders => [new("project", root)];
        public IReadOnlyList<WorkspaceArtifactContext> Projects { get; set; } = [new(root, new("project", null, [], []))];
        public event Action? ProjectChanged;
        public void Change() => ProjectChanged?.Invoke();
    }
}
