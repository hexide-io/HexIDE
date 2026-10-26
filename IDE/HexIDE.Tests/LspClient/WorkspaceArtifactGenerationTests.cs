using System.Text.Json;
using HexIDE.Conversations;
using HexIDE.Lsp;
using HexIDE.Lsp.Providers;
using HexIDE.Redaction;
using Microsoft.Extensions.Logging.Abstractions;

namespace HexIDE.Tests.LspClient;

public sealed class WorkspaceArtifactGenerationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hexide-artifact-" + Guid.NewGuid().ToString("N"));
    private readonly Workspace _workspace;
    private readonly IWorkspaceArtifactConsent _consent = Substitute.For<IWorkspaceArtifactConsent>();
    private readonly Provider _provider = new();
    private readonly WorkspaceArtifactService _service;

    public WorkspaceArtifactGenerationTests()
    {
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(_root);
        _consent.AllowAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);
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
            _consent.AllowAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        if (reason == "provider") _provider.Failure = new IOException("provider failed");
        if (reason == "write") Directory.CreateDirectory(Path.Combine(_root, "generated.json"));
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
            _consent.AllowAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(false);
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
        await _consent.Received().AllowAsync("probe", "Probe server", _root, Path.Combine(_root, "generated.json"));
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
        await _consent.DidNotReceiveWithAnyArgs().AllowAsync(default!, default!, default!, default!);
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
            await _consent.DidNotReceiveWithAnyArgs().AllowAsync(default!, default!, default!, default!);
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
        await _consent.Received().AllowAsync("probe", "Probe server", real, Path.Combine(real, "generated.json"));
    }

    [Fact]
    public async Task ExistingForeignFileIsLeftAloneWithoutPrompt()
    {
        var target = Path.Combine(_root, "generated.json");
        File.WriteAllText(target, "CMake owns this");
        await _service.PrepareAsync(Server(), TestContext.Current.CancellationToken);
        File.ReadAllText(target).Should().Be("CMake owns this");
        await _consent.DidNotReceiveWithAnyArgs().AllowAsync(default!, default!, default!, default!);
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
        await _consent.DidNotReceiveWithAnyArgs().AllowAsync(default!, default!, default!, default!);
    }

    [Fact]
    public async Task DenialStillLaunchesServerWithoutWriting()
    {
        _consent.AllowAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        var launched = false;
        var server = Server() with { CreateClient = () => { launched = true; return Substitute.For<ILspClient>(); } };
        await using var registry = new LspClientRegistry([server], NullLogger<LspClientRegistry>.Instance,
            _workspace, artifacts: _service);
        await registry.OpenDocumentAsync(new Uri(Path.Combine(_root, "probe.c")).AbsoluteUri, "int x;", TestContext.Current.CancellationToken);
        launched.Should().BeTrue();
        File.Exists(Path.Combine(_root, "generated.json")).Should().BeFalse();
        await _consent.Received().AllowAsync("probe", "Probe server", _root, Path.Combine(_root, "generated.json"));
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
        await _consent.DidNotReceiveWithAnyArgs().AllowAsync(default!, default!, default!, default!);
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
        await _consent.DidNotReceiveWithAnyArgs().AllowAsync(default!, default!, default!, default!);
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
        await _consent.DidNotReceiveWithAnyArgs().AllowAsync(default!, default!, default!, default!);
    }

    [Fact]
    public async Task FileCreatedDuringConsentIsNotClobbered()
    {
        var target = Path.Combine(_root, "generated.json");
        _consent.AllowAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(_ => { File.WriteAllText(target, "another writer"); return true; });
        await _service.PrepareAsync(Server(), TestContext.Current.CancellationToken);
        File.ReadAllText(target).Should().Be("another writer");
        await _consent.Received().AllowAsync("probe", "Probe server", _root, target);
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
        await _consent.DidNotReceiveWithAnyArgs().AllowAsync(default!, default!, default!, default!);
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
        public string? Produce(WorkspaceArtifactContext context) { Calls++; if (Failure is not null) throw Failure; return Content; }
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
