using HexIDE.Lsp;
using Microsoft.Extensions.Logging;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// The <c>workspaceArtifact</c> declaration: what is accepted, what is refused, and what a refusal costs.
/// </summary>
/// <remarks>
/// <para>
/// <b>The invariant under test is that a refusal is never fatal.</b> A server that generates no descriptor
/// may still be useful; one that will not start is not. So every case here asserts two things — that the
/// fault was reported, and that the entry survived it.
/// </para>
/// <para>
/// Driven against real files for the same reason the loader's own tests are: this reads text a person
/// typed, and the interesting failures live at that boundary.
/// </para>
/// </remarks>
public class WorkspaceArtifactSpecTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "hexide-artefact-" + Guid.NewGuid().ToString("N"));

    private readonly ILogger<LanguageServerConfigLoader> _logger =
        Substitute.For<ILogger<LanguageServerConfigLoader>>();

    public WorkspaceArtifactSpecTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private sealed class StubProvider(string name) : IWorkspaceArtifactProvider
    {
        public string Name { get; } = name;
        public string? Produce(WorkspaceArtifactContext context) => "{}";
    }

    private static IWorkspaceArtifactProviderRegistry Registry(params string[] names) =>
        new WorkspaceArtifactProviderRegistry(names.Select(n => new StubProvider(n)));

    private LanguageServerConfigResult Load(string artifactJson, IWorkspaceArtifactProviderRegistry? providers)
    {
        var json = """
        {
          "version": 1,
          "servers": [
            {
              "id": "probe",
              "extensions": [".xyz"],
              "languageId": "xyz",
              "transport": "stdio",
              "command": "probe-server",
              "workspaceArtifact": ARTIFACT
            }
          ]
        }
        """.Replace("ARTIFACT", artifactJson);

        var path = Path.Combine(_dir, "lsp-servers.json");
        File.WriteAllText(path, json);
        return new LanguageServerConfigLoader(path, _logger).Load([], artifactProviders: providers);
    }

    /// <summary>The entry is still there, whatever was said about its artefact.</summary>
    private static void EntrySurvived(LanguageServerConfigResult result) =>
        result.Entries.Should().ContainSingle().Which.Id.Should().Be("probe");

    // ── the registry ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AProviderIsFoundWhateverTheCaseItWasWrittenIn()
    {
        // The name is typed by hand into a configuration file. A provider that failed to resolve over
        // letter case would present as a server that starts and says nothing, which is the exact symptom
        // this whole capability exists to remove.
        var registry = Registry("clangd.compileCommands");

        registry.Find("clangd.compileCommands").Should().NotBeNull();
        registry.Find("CLANGD.COMPILECOMMANDS").Should().NotBeNull();
        registry.Find("clangd.compilecommands").Should().NotBeNull();
    }

    [Fact]
    public void AnUnregisteredNameFindsNothingRatherThanThrowing()
    {
        Registry("a.b").Find("nobody.here").Should().BeNull();
    }

    [Fact]
    public void TheLastRegistrationOfANameWins()
    {
        // Matching how a user entry replaces a default elsewhere in this configuration, and silent for the
        // same reason: refusing to start over a duplicate would let one extension stop the IDE.
        var first = new StubProvider("dup");
        var second = new StubProvider("dup");

        new WorkspaceArtifactProviderRegistry([first, second]).Find("dup").Should().BeSameAs(second);
    }

    // ── what is accepted ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AWellFormedDeclarationIsReadAndReportsNothing()
    {
        var result = Load("""{ "path": "compile_commands.json", "provider": "p" }""", Registry("p"));

        EntrySurvived(result);
        result.Problems.Should().NotContain(p => p.EntryId == "probe");

        WorkspaceArtifactSpecReader.TryRead(
            "probe", result.Entries[0].WorkspaceArtifact, Registry("p"), out var spec, out _)
            .Should().BeTrue();
        spec!.RelativePath.Should().Be("compile_commands.json");
        spec.ProviderName.Should().Be("p");
    }

    [Fact]
    public void ASubdirectoryPathIsFine()
    {
        // Only climbing out is refused. A descriptor that belongs in a build directory is ordinary.
        WorkspaceArtifactSpecReader.TryRead(
            "probe", new WorkspaceArtifactEntry { Path = "build/compile_commands.json", Provider = "p" },
            Registry("p"), out var spec, out var problems)
            .Should().BeTrue();

        spec!.RelativePath.Should().Be("build/compile_commands.json");
        problems.Should().BeEmpty();
    }

    [Fact]
    public void AnEntryDeclaringNoArtefactIsNotAFault()
    {
        WorkspaceArtifactSpecReader.TryRead("probe", null, Registry("p"), out var spec, out var problems)
            .Should().BeFalse();

        spec.Should().BeNull();
        problems.Should().BeEmpty();
    }

    // ── what is refused, and at what cost ─────────────────────────────────────────────────────────────

    [Fact]
    public void ADeclarationWithNoPathIsReportedAndTheServerSurvives()
    {
        var result = Load("""{ "provider": "p" }""", Registry("p"));

        EntrySurvived(result);
        var problem = result.Problems.Should().ContainSingle(p => p.EntryId == "probe").Subject;
        problem.EntryRejected.Should().BeFalse("a bad artefact must not cost the server");
        problem.Message.Should().Contain("no path");
    }

    [Fact]
    public void ADeclarationNamingNoProviderIsReportedAndTheServerSurvives()
    {
        var result = Load("""{ "path": "x.json" }""", Registry("p"));

        EntrySurvived(result);
        var problem = result.Problems.Should().ContainSingle(p => p.EntryId == "probe").Subject;
        problem.EntryRejected.Should().BeFalse();
        problem.Message.Should().Contain("names no provider");
    }

    [Fact]
    public void AnUnknownProviderIsReportedWithWhatWasAvailable()
    {
        // Naming the alternatives, because the whole failure mode here is a user who cannot tell a
        // misspelling from an unsupported feature.
        var result = Load("""{ "path": "x.json", "provider": "typo" }""", Registry("real.one"));

        EntrySurvived(result);
        var problem = result.Problems.Should().ContainSingle(p => p.EntryId == "probe").Subject;
        problem.EntryRejected.Should().BeFalse();
        problem.Message.Should().Contain("typo").And.Contain("real.one");
    }

    [Fact]
    public void AnUnknownProviderIsNotReportedWhenNobodyKnowsTheProviders()
    {
        // The registry is optional: a caller that does not know what is registered must not conclude that
        // nothing is. Every structural rule still applies.
        WorkspaceArtifactSpecReader.TryRead(
            "probe", new WorkspaceArtifactEntry { Path = "x.json", Provider = "whatever" },
            providers: null, out var spec, out var problems)
            .Should().BeTrue();

        spec.Should().NotBeNull();
        problems.Should().BeEmpty();
    }

    [Fact]
    public void AnUnrecognisedFieldInsideTheDeclarationIsReportedButSurvivable()
    {
        var result = Load("""{ "path": "x.json", "provider": "p", "pathh": "typo" }""", Registry("p"));

        EntrySurvived(result);
        result.Problems.Should().Contain(p =>
            p.EntryId == "probe"
            && p.Kind == LanguageServerConfigProblemKind.UnrecognisedField
            && p.Message.Contains("pathh"));
    }

    // ── the two separator traps, which is why these are Theories ──────────────────────────────────────

    [Theory]
    [InlineData("/etc/hexide.json")]
    [InlineData("\\\\server\\share\\x.json")]
    [InlineData("C:\\Windows\\x.json")]
    [InlineData("Z:/x.json")]
    public void AnAbsolutePathIsRefusedOnEveryHost(string path)
    {
        // Path.IsPathRooted answers about the HOST: on Linux `C:\Windows\x.json` is not rooted and
        // `\\server\share` is an ordinary relative filename. Checked on the text so the same file means
        // the same thing wherever it is opened — and this file syncs between machines.
        WorkspaceArtifactSpecReader.TryRead(
            "probe", new WorkspaceArtifactEntry { Path = path, Provider = "p" },
            Registry("p"), out var spec, out var problems)
            .Should().BeFalse();

        spec.Should().BeNull();
        problems.Should().ContainSingle().Which.Message.Should().Contain("absolute path");
    }

    [Theory]
    [InlineData("../elsewhere.json")]
    [InlineData("..\\elsewhere.json")]
    [InlineData("build/../../elsewhere.json")]
    [InlineData("build\\..\\..\\elsewhere.json")]
    public void APathClimbingOutOfTheWorkspaceIsRefusedWithEitherSeparator(string path)
    {
        // The separator trap in reverse, and the reason this is a Theory rather than one case. A backslash
        // is an ordinary filename character on Linux, so splitting on the host separator alone would let
        // `..\..\x.json` through THERE while catching it on Windows — a write outside the workspace on
        // exactly the platform CI runs.
        WorkspaceArtifactSpecReader.TryRead(
            "probe", new WorkspaceArtifactEntry { Path = path, Provider = "p" },
            Registry("p"), out var spec, out var problems)
            .Should().BeFalse();

        spec.Should().BeNull();
        problems.Should().ContainSingle().Which.Message.Should().Contain("outside the workspace");
    }

    // ── the registration carries it ───────────────────────────────────────────────────────────────────

    [Fact]
    public void AValidDeclarationReachesTheRegistration()
    {
        var registrations = new LanguageServerRegistrationFactory(
            Substitute.For<ILoggerFactory>(), artifactProviders: Registry("p"))
            .Create([new LanguageServerEntry
            {
                Id = "probe",
                Extensions = [".xyz"],
                Transport = "stdio",
                Command = "probe-server",
                WorkspaceArtifact = new WorkspaceArtifactEntry { Path = "x.json", Provider = "p" },
            }]);

        registrations.Should().ContainSingle()
            .Which.WorkspaceArtifact.Should().Be(new WorkspaceArtifactSpec("x.json", "p"));
    }

    [Fact]
    public void ARefusedDeclarationReachesTheRegistrationAsNothing()
    {
        // The registration is still built — that is the "never fatal" rule, observed one layer down.
        var registrations = new LanguageServerRegistrationFactory(
            Substitute.For<ILoggerFactory>(), artifactProviders: Registry("p"))
            .Create([new LanguageServerEntry
            {
                Id = "probe",
                Extensions = [".xyz"],
                Transport = "stdio",
                Command = "probe-server",
                WorkspaceArtifact = new WorkspaceArtifactEntry { Path = "../x.json", Provider = "p" },
            }]);

        var registration = registrations.Should().ContainSingle().Subject;
        registration.Id.Should().Be("probe");
        registration.WorkspaceArtifact.Should().BeNull();
    }
}
