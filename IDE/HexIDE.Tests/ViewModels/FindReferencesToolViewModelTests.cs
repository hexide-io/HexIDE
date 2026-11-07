using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Tools;
using LspRange = HexIDE.Lsp.Messages.Range;

namespace HexIDE.Tests.ViewModels;

public class FindReferencesToolViewModelTests
{
    private readonly ILspClient client = Substitute.For<ILspClient>();
    private readonly IEditorService editors = Substitute.For<IEditorService>();
    private readonly IProjectManager projects = Substitute.For<IProjectManager>();
    private readonly IDocumentDockService documents = Substitute.For<IDocumentDockService>();
    private readonly ILocalizationService localization = Substitute.For<ILocalizationService>();
    private readonly ModuleDefinition first;
    private readonly ModuleDefinition second;

    public FindReferencesToolViewModelTests()
    {
        var project = TestHelpers.CreateProjectWithModule(moduleName: "First");
        first = project.Modules.Single();
        second = TestHelpers.CreateModule(project, "Second");
        project.AddModule(second);
        first.UpdateCode("Call Greet\r\n");
        second.UpdateCode("Call Greet\n");
        projects.LoadedProjects.Returns([project]);
        documents.OpenDocuments.Returns([]);
        localization.GetString("Str.Tool.FindReferences.Empty").Returns("No references");
        localization.GetString("Str.Tool.FindReferences.Error").Returns("{0}: {1}");
    }

    private FindReferencesToolViewModel Create() => new(client, editors, projects, documents, localization);
    private static Location At(ModuleDefinition module) => new(DocumentWireName.For(DocumentIdentity.For(module)),
        new LspRange(new Position(FormCodeText.LineBreaks(FormCodeText.Prefix(module)), 5),
            new Position(FormCodeText.LineBreaks(FormCodeText.Prefix(module)), 10)));

    [Fact]
    public void GivenLocationsGroupInProjectOrderAndHighlightTheRangeWithoutRequesting()
    {
        var vm = Create();
        vm.ShowLocations([At(second), At(first), At(second)]);
        vm.Rows.Select(r => r.Module).Should().Equal("First", "", "Second", "");
        vm.Rows[1].Line.Should().Be(At(first).Range.Start.Line + 1);
        vm.Rows[1].Before.Should().Be("Call ");
        vm.Rows[1].Highlight.Should().Be("Greet");
        client.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void EmptyLocationsReplaceThePreviousSearch()
    {
        var vm = Create();
        vm.ShowLocations([At(first)]);
        vm.ShowLocations([]);
        vm.Rows.Should().ContainSingle().Which.Before.Should().Be("No references");
    }

    [Fact]
    public async Task SearchShowsSuccessfulLocationsAndServerError()
    {
        var location = At(first);
        client.FindReferencesAsync(location.Uri, Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(new ReferenceSearchResult([location], [new("broken", "failed")]));
        var vm = Create();
        await vm.SearchAsync(location.Uri, new Position(0, 5), TestContext.Current.CancellationToken);
        vm.Rows.Should().HaveCount(3);
        vm.Rows[1].Location.Should().Be(location);
        vm.Rows[2].Before.Should().Be("broken: failed");
    }

    [Fact]
    public void NavigateSelectedUsesTheLocationAndOneBasedPosition()
    {
        var vm = Create();
        vm.ShowLocations([At(second)]);
        vm.SelectedRow = vm.Rows[1];
        editors.NavigateTo(At(second).Uri, At(second).Range.Start.Line + 1, 6).Returns(true);
        vm.NavigateSelected().Should().BeTrue();
        editors.Received(1).NavigateTo(At(second).Uri, At(second).Range.Start.Line + 1, 6);
    }

    [Fact]
    public async Task GivenLocationsSupersedeAnUnfinishedSearch()
    {
        var answer = new TaskCompletionSource<ReferenceSearchResult>();
        client.FindReferencesAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>()).Returns(answer.Task);
        var vm = Create();
        var pending = vm.SearchAsync(At(first).Uri, new Position(0, 5), TestContext.Current.CancellationToken);
        vm.ShowLocations([At(second)]);
        answer.SetResult(new([At(first)], []));
        await pending;
        vm.Rows[1].Location.Should().Be(At(second));
    }
}
