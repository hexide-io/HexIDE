using System.Reflection;
using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Runtime.ProjectElements;
using LspRange = HexIDE.Lsp.Messages.Range;

namespace HexIDE.Tests.Addins;

/// <summary>
/// An add-in asking for one document's diagnostics is told when its name found no single document, rather than
/// handed an empty list it cannot tell from a clean file (hexide-io/HexIDE#273 task 3.21, the addin-system delta's
/// "Two projects with a document of the same name").
/// </summary>
/// <remarks>
/// Through <see cref="AddinDiagnosticsService"/>, which is the host's <see cref="IDiagnosticsAccess"/>, with the
/// diagnostics arriving as a server publishes them. <c>AddinWholeFileTests</c> holds the editor members to the same
/// scenario.
/// </remarks>
public sealed class AddinDiagnosticsAccessTests : IDisposable
{
    private readonly ILspClient lsp = Substitute.For<ILspClient>();
    private readonly IProjectManager projects = Substitute.For<IProjectManager>();
    private readonly List<ProjectDefinition> loaded = [];
    private readonly AddinDiagnosticsService sut;

    public AddinDiagnosticsAccessTests()
    {
        projects.LoadedProjects.Returns(_ => loaded);
        sut = new AddinDiagnosticsService(lsp, projects);
    }

    public void Dispose() => sut.Dispose();

    /// <summary>A module in a project, loading the project the first time it is named.</summary>
    private ModuleDefinition Module(string projectName, string name)
    {
        var project = loaded.FirstOrDefault(p => p.Name == projectName);
        if (project is null)
        {
            project = TestHelpers.CreateProject(projectName);
            loaded.Add(project);
        }
        var module = TestHelpers.CreateModule(project, name);
        project.AddModule(module);
        return module;
    }

    /// <summary>A server's publication for a document, under the name the IDE gives it on the wire.</summary>
    private void Publish(ModuleDefinition module, string message, int line = 2) =>
        lsp.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(lsp,
            new PublishDiagnosticsParams(DocumentWireName.For(DocumentIdentity.For(module)),
                [new Diagnostic(new LspRange(new Position(line, 4), new Position(line, 9)), message, DiagnosticSeverity.Error)]));

    // ── The addin-system delta's scenario ───────────────────────────────────────────────────────────────────

    [Fact]
    public void TwoProjectsWithADocumentOfTheSameName()
    {
        var shop = Module("Shop", "Module1");
        var stock = Module("Stock", "Module1");
        Publish(shop, "in Shop");
        Publish(stock, "in Stock");

        var bare = sut.TryGetFor("Module1", null);

        bare.Refused.Should().BeTrue("the add-in is told the name is ambiguous");
        bare.Reason.Should().Contain("more than one document").And.Contain("Shop/Module1").And.Contain("Stock/Module1")
            .And.Contain("project", "the reason says how to name the one meant");
        bare.Diagnostics.Should().BeEmpty("neither project's diagnostics are answered for a name both have");
        sut.GetFor("Module1", null).Should().BeEmpty("the member an earlier add-in calls answers as it always has");
        sut.GetFor("Module1").Should().BeEmpty();

        var named = sut.TryGetFor("module1", "STOCK");

        named.Refused.Should().BeFalse();
        named.Reason.Should().BeNull();
        named.Diagnostics.Should().ContainSingle().Which.Should().Match<AddinDiagnostic>(d =>
            d.Message == "in Stock" && d.Project == "Stock" && d.FileName == "Module1" && d.Line == 3 && d.Column == 5);
        sut.GetFor("Module1", "Shop").Should().ContainSingle().Which.Message.Should().Be("in Shop",
            "naming the project reaches that project's document, and only it");
    }

    // ── The other refusals, worded as automation words them ────────────────────────────────────────────────

    [Fact]
    public void ANameNoLoadedProjectHasIsRefusedWithWhatTheProjectsHold()
    {
        Publish(Module("Shop", "Helpers"), "in Helpers");

        var result = sut.TryGetFor("Helpres", null);

        result.Refused.Should().BeTrue();
        result.Reason.Should().Contain("No form or module named 'Helpres'").And.Contain("Helpers");
        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void AProjectThatIsNotLoadedIsRefusedWithTheProjectsThatAre()
    {
        Publish(Module("Shop", "Module1"), "in Shop");

        var result = sut.TryGetFor("Module1", "Stock");

        result.Refused.Should().BeTrue();
        result.Reason.Should().Contain("No loaded project named 'Stock'").And.Contain("Shop");
        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void ADocumentWithNoDiagnosticsIsAnEmptyListAndNotARefusal()
    {
        // The distinction the member exists to make. GetFor answers this and the two refusals above identically.
        var clean = Module("Shop", "Module1");
        Publish(Module("Shop", "Module2"), "in Module2");

        var result = sut.TryGetFor(clean.Name, null);

        result.Refused.Should().BeFalse("the name found one document, which has nothing reported against it");
        result.Reason.Should().BeNull();
        result.Diagnostics.Should().BeEmpty();
    }

    // ── Add-ins already built keep working ──────────────────────────────────────────────────────────────────

    [Fact]
    public void TheMembersEarlierAddinsCallKeepTheirShapes()
    {
        // Add-ins are loaded as built assemblies, so a member they call must keep its exact signature.
        Member("GetAll").ReturnType.Should().Be(typeof(IReadOnlyList<AddinDiagnostic>));
        Member("GetFor", typeof(string)).ReturnType.Should().Be(typeof(IReadOnlyList<AddinDiagnostic>));
        Member("GetFor", typeof(string), typeof(string)).ReturnType.Should().Be(typeof(IReadOnlyList<AddinDiagnostic>));

        static MethodInfo Member(string name, params Type[] parameters) =>
            typeof(IDiagnosticsAccess).GetMethod(name, parameters)
            ?? throw new InvalidOperationException($"IDiagnosticsAccess.{name}({string.Join(", ", parameters.Select(p => p.Name))}) is gone");
    }

    /// <summary>
    /// A host or test double written before <see cref="IDiagnosticsAccess.TryGetFor"/> existed, implementing only
    /// what it had to then. That this compiles is half the test.
    /// </summary>
    private sealed class EarlierHost(IReadOnlyList<AddinDiagnostic> answer) : IDiagnosticsAccess
    {
        public IReadOnlyList<AddinDiagnostic> GetAll() => answer;
        public IReadOnlyList<AddinDiagnostic> GetFor(string fileName) => answer;
        public IReadOnlyList<AddinDiagnostic> GetFor(string fileName, string? project) => answer;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void AHostThatOnlyAnswersAListStillAnswersTheReasonGivingMember(int count)
    {
        var list = Enumerable.Range(1, count)
            .Select(i => new AddinDiagnostic("Module1", i, 1, "said", AddinDiagnosticSeverity.Error)).ToList();
        IDiagnosticsAccess host = new EarlierHost(list);

        var result = host.TryGetFor("Module1", null);

        result.Diagnostics.Should().Equal(list);
        result.Refused.Should().BeFalse("such a host never refuses, so no refusal is invented for it");
        result.Reason.Should().BeNull();
    }
}
