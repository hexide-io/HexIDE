using Avalonia.Threading;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Tools.Diagnostics;
using Microsoft.Extensions.Logging;
using LspRange = HexIDE.Lsp.Messages.Range;

namespace HexIDE.Tests.ViewModels;

public class DiagnosticsToolViewModelTests
{
    private const string Uri = "untitled:P/Module1.bas";
    private readonly IDocumentDockService dock = Substitute.For<IDocumentDockService>();
    private readonly IEditorService editors = Substitute.For<IEditorService>();

    private DiagnosticsToolViewModel View(LspClientRegistry registry) => new(registry, registry, dock, editors,
        Substitute.For<ILocalizationService>());

    private static Diagnostic Say(DiagnosticSeverity severity, string message = "problem") =>
        new(new LspRange(new Position(2, 3), new Position(2, 6)), message, severity);

    private static LspClientRegistry Registry() => new([], Substitute.For<ILogger<LspClientRegistry>>());

    [AvaloniaFact]
    public void RowsUseTheConfiguredServerNameAndThePanelHasARegisteredView()
    {
        var registry = Substitute.For<ILanguageConnectionRegistry>();
        registry.Diagnostics.Returns([new OwnedDiagnostic(Uri, "id", Say(DiagnosticSeverity.Error))]);
        registry.Connections.Returns([new LanguageServerConnection("id", "Configured server",
            LanguageConnectionKind.LanguageServer, LanguageConnectionState.Running, [], "vb6", null)]);
        var vm = new DiagnosticsToolViewModel(registry, Substitute.For<ILspClient>(), dock, editors,
            Substitute.For<ILocalizationService>());
        vm.Rows.Should().ContainSingle().Which.Server.Should().Be("Configured server");
        new ViewLocator().Build(vm).Should().BeOfType<DiagnosticsToolView>();
    }

    [AvaloniaFact]
    public async Task OpeningThePanelIncludesPreviouslyReportedUnopenedDocumentsAndTheirOwners()
    {
        await using var registry = Registry();
        await registry.InjectDiagnosticsAsync(Uri, [Say(DiagnosticSeverity.Error)], "first");
        await registry.InjectDiagnosticsAsync(Uri, [Say(DiagnosticSeverity.Warning)], "second");
        var vm = View(registry);
        vm.Rows.Select(r => r.Server).Should().Equal("first", "second");
        vm.Rows.Should().OnlyContain(r => r.Uri == Uri && r.Line == 3);
    }

    [AvaloniaTheory]
    [InlineData(DiagnosticSeverity.Error)]
    [InlineData(DiagnosticSeverity.Warning)]
    [InlineData(DiagnosticSeverity.Information)]
    [InlineData(DiagnosticSeverity.Hint)]
    public async Task EachSeverityCanBeFiltered(DiagnosticSeverity severity)
    {
        await using var registry = Registry();
        await registry.InjectDiagnosticsAsync(Uri, [Say(severity)], "server");
        var vm = View(registry);
        vm.Rows.Should().ContainSingle();
        switch (severity)
        {
            case DiagnosticSeverity.Error: vm.Errors = false; break;
            case DiagnosticSeverity.Warning: vm.Warnings = false; break;
            case DiagnosticSeverity.Information: vm.Information = false; break;
            case DiagnosticSeverity.Hint: vm.Hints = false; break;
        }
        vm.Rows.Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task LiveReplacementAndWithdrawalUpdateAnAlreadyOpenPanel()
    {
        await using var registry = Registry();
        var vm = View(registry);
        await registry.InjectDiagnosticsAsync(Uri, [Say(DiagnosticSeverity.Hint)], "server");
        Dispatcher.UIThread.RunJobs();
        vm.Rows.Should().ContainSingle().Which.Severity.Should().Be(DiagnosticSeverity.Hint);
        await registry.InjectDiagnosticsAsync(Uri, [Say(DiagnosticSeverity.Error, "replacement")], "server");
        Dispatcher.UIThread.RunJobs();
        vm.Rows.Should().ContainSingle().Which.Message.Should().Be("replacement");
        await registry.ClearDiagnosticsFromAsync("server");
        Dispatcher.UIThread.RunJobs();
        vm.Rows.Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task NavigationUsesTheSelectedRowsDocumentAndOneBasedPosition()
    {
        await using var registry = Registry();
        await registry.InjectDiagnosticsAsync(Uri, [Say(DiagnosticSeverity.Error)], "server");
        var vm = View(registry);
        vm.SelectedRow = vm.Rows.Single();
        vm.NavigateSelected();
        editors.Received().NavigateTo(Uri, 3, 4);
    }

    [AvaloniaFact]
    public async Task CurrentDocumentFilterFollowsEditorChangesAndSurvivesPanelFocus()
    {
        await using var registry = Registry();
        using var first = new RelatedDocumentEditorViewModel(registry, Substitute.For<IWindowManager>(), Substitute.For<ILocalizationService>());
        // The filter also covers carried files, whose URI is independent of a VB6 document identity.
        var path = Path.GetFullPath("diagnostics-1991.md");
        var related = new HexIDE.Runtime.ProjectElements.RelatedDocumentDefinition(TestHelpers.CreateProject("P"), "diagnostics-1991.md", path);
        first.Initialize(related);
        dock.ActiveDocument.Returns(first);
        await registry.InjectDiagnosticsAsync(LspDocumentUri.ForFile(path), [Say(DiagnosticSeverity.Error)], "server");
        var otherPath = Path.GetFullPath("other-diagnostics-1991.md");
        await registry.InjectDiagnosticsAsync(LspDocumentUri.ForFile(otherPath), [Say(DiagnosticSeverity.Warning)], "server");
        var vm = View(registry);
        vm.CurrentDocumentOnly = true;
        vm.Rows.Should().ContainSingle().Which.Uri.Should().Be(LspDocumentUri.ForFile(path));
        dock.ActiveDocument.Returns((BaseEditorWindowViewModel?)null);
        vm.Refresh();
        vm.Rows.Should().ContainSingle();
        using var other = new RelatedDocumentEditorViewModel(registry, Substitute.For<IWindowManager>(), Substitute.For<ILocalizationService>());
        other.Initialize(new HexIDE.Runtime.ProjectElements.RelatedDocumentDefinition(related.Owner, "other.md", otherPath));
        dock.ActiveDocument.Returns(other);
        dock.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(dock, new PropertyChangedEventArgs(nameof(IDocumentDockService.ActiveDocument)));
        vm.Rows.Should().ContainSingle().Which.Uri.Should().Be(LspDocumentUri.ForFile(otherPath));
        vm.CurrentDocumentOnly = false;
        vm.Rows.Should().HaveCount(2);
    }
}
