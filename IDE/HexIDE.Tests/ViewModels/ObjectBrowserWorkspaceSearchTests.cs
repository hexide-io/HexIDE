using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Tools.ObjectBrowser;
using LspRange = HexIDE.Lsp.Messages.Range;   // `Range` collides with System.Range

namespace HexIDE.Tests.ViewModels;

/// <summary>
/// The Object Browser's search box asking every capable server where a name lives.
///
/// <para>
/// <b>Most of what is asserted here is the status line, and that is the point.</b> Servers start lazily —
/// one starts when a document of its language is opened, because that is the first moment the language is
/// known to be present — so a search run before anything is open reaches nobody and gets back an empty
/// list. So does a search that genuinely found nothing, and so does a search against a server that never
/// offered the feature. Three quite different situations, one indistinguishable result, and the line of
/// text is the only thing that separates them.
/// </para>
/// </summary>
public class ObjectBrowserWorkspaceSearchTests
{
    // Returned verbatim so an assertion names the key rather than an English sentence that a translator
    // is free to change.
    private static ILocalizationService Loc()
    {
        var loc = Substitute.For<ILocalizationService>();
        loc.GetString(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        loc.GetString("Str.Tool.ObjectBrowser.Search.NoMatches").Returns("no-matches:{0}");
        loc.GetString("Str.Tool.ObjectBrowser.Search.Matches").Returns("matches:{0}");
        return loc;
    }

    private static SymbolInformation Symbol(string name, string uri, int line, string? container = null) =>
        new(name, SymbolKind.Function,
            new Location(uri, new LspRange(new Position(line, 4), new Position(line, 4 + name.Length))),
            container);

    private static LanguageServerConnection Connection(LanguageConnectionState state, string? capabilities) =>
        new("server", "Server", LanguageConnectionKind.LanguageServer, state, [".bas"], "vb6",
            capabilities is null ? null : JsonDocument.Parse(capabilities).RootElement.Clone());

    private sealed record Sut(
        ObjectBrowserToolViewModel ViewModel, ILspClient Client, IEditorService Editor);

    private static Sut Make(
        bool clientRunning,
        string? capabilities,
        LanguageConnectionState state = LanguageConnectionState.Running,
        params SymbolInformation[] found)
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(new List<ProjectDefinition>());

        var client = Substitute.For<ILspClient>();
        client.IsRunning.Returns(clientRunning);
        client.RequestWorkspaceSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(found);

        var connections = Substitute.For<ILanguageConnectionRegistry>();
        connections.Connections.Returns([Connection(state, capabilities)]);

        var editor = Substitute.For<IEditorService>();

        var vm = new ObjectBrowserToolViewModel(
            projectManager, client, editor,
            Substitute.For<IComponentRegistry>(),
            Substitute.For<ITypeLibraryService>(),
            Substitute.For<IFocusedProjectUtil>(),
            Loc(),
            connections);

        return new Sut(vm, client, editor);
    }

    // ── The three empty lists ─────────────────────────────────────────────────

    [Fact]
    public async Task NothingStartedYetSaysSoRatherThanNoMatches()
    {
        // The whole reason the status line exists. A developer who searches before opening any code gets
        // an empty list, and "nothing matched" would be a confident wrong answer to the question they
        // asked — the search never left the building.
        var sut = Make(clientRunning: false, capabilities: null, LanguageConnectionState.NotStarted);

        await sut.ViewModel.SearchWorkspaceAsync("Calc");

        sut.ViewModel.SearchStatus.Should().Be("Str.Tool.ObjectBrowser.Search.NoServer");
        sut.ViewModel.SearchResults.Should().BeEmpty();
        sut.ViewModel.IsShowingSearchResults.Should().BeTrue("the explanation has to be somewhere visible");
        await sut.Client.DidNotReceive().RequestWorkspaceSymbolsAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AServerThatDoesNotOfferTheFeatureIsNamedAsTheReason()
    {
        var sut = Make(clientRunning: true, capabilities: """{"documentSymbolProvider":true}""");

        await sut.ViewModel.SearchWorkspaceAsync("Calc");

        sut.ViewModel.SearchStatus.Should().Be("Str.Tool.ObjectBrowser.Search.NoProvider");
        await sut.Client.DidNotReceive().RequestWorkspaceSymbolsAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnHonestlyEmptyAnswerCarriesTheQuery()
    {
        var sut = Make(clientRunning: true, capabilities: """{"workspaceSymbolProvider":true}""");

        await sut.ViewModel.SearchWorkspaceAsync("Nowhere");

        sut.ViewModel.SearchStatus.Should().Be("no-matches:Nowhere");
    }

    [Fact]
    public async Task ARunningServerThatRefusedExplicitlyIsStillARefusal()
    {
        var sut = Make(clientRunning: true, capabilities: """{"workspaceSymbolProvider":false}""");

        await sut.ViewModel.SearchWorkspaceAsync("Calc");

        sut.ViewModel.SearchStatus.Should().Be("Str.Tool.ObjectBrowser.Search.NoProvider");
    }

    [Fact]
    public async Task ACapabilitySentAsOptionsCountsAsOffered()
    {
        // Most capabilities are `boolean | XxxOptions` and a conformant server may send either; modelling
        // one narrowly is what caused a total silent blackout once already (#238).
        var sut = Make(clientRunning: true,
            capabilities: """{"workspaceSymbolProvider":{"resolveProvider":true}}""",
            found: Symbol("CalcTotal", "vb6://module/Orders", 9));

        await sut.ViewModel.SearchWorkspaceAsync("Calc");

        sut.ViewModel.SearchResults.Should().ContainSingle();
    }

    [Fact]
    public async Task AServerThatIsRegisteredButNotUpIsNotCountedAsCapable()
    {
        // Capabilities survive a connection dying, so reading them without checking the state would keep
        // offering a feature that has nothing behind it.
        var sut = Make(clientRunning: true,
            capabilities: """{"workspaceSymbolProvider":true}""",
            state: LanguageConnectionState.Stopped);

        await sut.ViewModel.SearchWorkspaceAsync("Calc");

        sut.ViewModel.SearchStatus.Should().Be("Str.Tool.ObjectBrowser.Search.NoProvider");
    }

    // ── Results ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task HitsAreListedWithACount()
    {
        var sut = Make(clientRunning: true, capabilities: """{"workspaceSymbolProvider":true}""",
            found: [Symbol("CalcTotal", "vb6://module/Orders", 9),
                    Symbol("CalcTax", "vb6://module/Tax", 2)]);

        await sut.ViewModel.SearchWorkspaceAsync("Calc");

        sut.ViewModel.SearchResults.Should().HaveCount(2);
        sut.ViewModel.SearchStatus.Should().Be("matches:2");
    }

    [Fact]
    public async Task HitsAreOrderedByNameRatherThanByWhichServerAnsweredFirst()
    {
        // The list is several servers' answers concatenated, so arrival order is registration order —
        // which means nothing to a reader scanning for a name.
        var sut = Make(clientRunning: true, capabilities: """{"workspaceSymbolProvider":true}""",
            found: [Symbol("Zebra", "vb6://module/A", 0),
                    Symbol("apple", "vb6://module/B", 0),
                    Symbol("Mango", "vb6://module/C", 0)]);

        await sut.ViewModel.SearchWorkspaceAsync("a");

        sut.ViewModel.SearchResults.Select(r => r.Name).Should().Equal("apple", "Mango", "Zebra");
    }

    [Fact]
    public async Task AHitSaysWhereItIsUsingTheContainerTheServerNamed()
    {
        var sut = Make(clientRunning: true, capabilities: """{"workspaceSymbolProvider":true}""",
            found: Symbol("CalcTotal", "vb6://module/Orders", 9, container: "Orders"));

        await sut.ViewModel.SearchWorkspaceAsync("Calc");

        sut.ViewModel.SearchResults[0].Where.Should().Contain("Orders").And.Contain("10",
            "the protocol counts lines from zero and an editor counts from one");
    }

    [Fact]
    public async Task AHitWithNoContainerFallsBackToTheDocument()
    {
        // containerName is optional and plenty of servers omit it. A result that says only "CalcTotal" is
        // useless for choosing between several hits of the same name, which is the case the list exists for.
        var sut = Make(clientRunning: true, capabilities: """{"workspaceSymbolProvider":true}""",
            found: Symbol("CalcTotal", "vb6://module/Orders", 9));

        await sut.ViewModel.SearchWorkspaceAsync("Calc");

        sut.ViewModel.SearchResults[0].Where.Should().Be("Orders:10");
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ActivatingAHitOpensItWhereTheServerSaidItWas()
    {
        var sut = Make(clientRunning: true, capabilities: """{"workspaceSymbolProvider":true}""",
            found: Symbol("CalcTotal", "vb6://module/Orders", 9));

        await sut.ViewModel.SearchWorkspaceAsync("Calc");
        sut.ViewModel.SelectedSearchResult = sut.ViewModel.SearchResults[0];
        sut.ViewModel.GoToSearchResultCommand.Execute(null);

        // Line 10, not 9: an editor counts lines from one and the protocol counts them from zero. Getting
        // this backwards lands the caret one line off every single time, which reads as an editor bug.
        sut.Editor.Received(1).NavigateTo("vb6://module/Orders", 10, 4);
    }

    [Fact]
    public async Task NothingIsSelectedSoThereIsNowhereToGo()
    {
        var sut = Make(clientRunning: true, capabilities: """{"workspaceSymbolProvider":true}""",
            found: Symbol("CalcTotal", "vb6://module/Orders", 9));

        await sut.ViewModel.SearchWorkspaceAsync("Calc");

        sut.ViewModel.GoToSearchResultCommand.CanExecute(null).Should().BeFalse();
    }

    // ── The pane's own lifecycle ──────────────────────────────────────────────

    [Fact]
    public async Task AnEmptyQueryAsksNobodyAndShowsNothing()
    {
        var sut = Make(clientRunning: true, capabilities: """{"workspaceSymbolProvider":true}""",
            found: Symbol("CalcTotal", "vb6://module/Orders", 9));

        await sut.ViewModel.SearchWorkspaceAsync("");

        sut.ViewModel.IsShowingSearchResults.Should().BeFalse();
        sut.ViewModel.SearchStatus.Should().BeEmpty();
        await sut.Client.DidNotReceive().RequestWorkspaceSymbolsAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClearingTheSearchPutsThePaneAway()
    {
        var sut = Make(clientRunning: true, capabilities: """{"workspaceSymbolProvider":true}""",
            found: Symbol("CalcTotal", "vb6://module/Orders", 9));

        await sut.ViewModel.SearchWorkspaceAsync("Calc");
        sut.ViewModel.IsShowingSearchResults.Should().BeTrue();

        sut.ViewModel.ClearSearchCommand.Execute(null);

        sut.ViewModel.IsShowingSearchResults.Should().BeFalse();
        sut.ViewModel.SearchResults.Should().BeEmpty();
        sut.ViewModel.SearchText.Should().BeEmpty();
    }
}
