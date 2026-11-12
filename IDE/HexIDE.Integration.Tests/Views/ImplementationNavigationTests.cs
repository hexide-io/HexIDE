using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Location = HexIDE.Lsp.Messages.Location;
using Avalonia.Threading;
using HexIDE.Bookmarks;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Tools;

namespace HexIDE.Integration.Tests.Views;

public class ImplementationNavigationTests
{
    [AvaloniaTheory]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    public void RoutedCommandsGateAndNavigateOrShowResults(bool implementation, int count)
    {
        var client = Substitute.For<ILspClient>();
        var localization = new LocalizationService();
        localization.Apply("en");
        var projects = Substitute.For<IProjectManager>();
        var documents = Substitute.For<IDocumentDockService>();
        projects.LoadedProjects.Returns([]);
        documents.OpenDocuments.Returns([]);
        var editors = Substitute.For<IEditorService>();
        var results = new FindReferencesToolViewModel(client, editors, projects, documents, localization, format: new Vb6ProjectFileFormat());
        client.SupportsCapability(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        var answer = Enumerable.Range(0, count).Select(i => new Location("untitled:P/Other.bas",
            new HexIDE.Lsp.Messages.Range(new Position(i + 2, 3), new Position(i + 2, 8)))).ToArray();
        client.RequestImplementationAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>()).Returns(answer);
        client.RequestTypeDefinitionAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>()).Returns(answer);
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);
        var events = new EventBus();
        using var subscription = events.Subscribe<NavigationLocationsEvent>(e => results.ShowLocations(e.Locations));
        var shown = 0;
        results.ShowRequested += () => shown++;
        using var vm = new CodeEditorViewModel(Substitute.For<IWindowManager>(), editors,
            Substitute.For<IProjectService>(), events, client, settings,
            Substitute.For<IStatusBarService>(), Substitute.For<IBookmarkService>(),
            Substitute.For<HexIDE.Debugging.IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            Substitute.For<HexIDE.Debugging.IRunScope>(), localization, new Vb6ProjectFileFormat(), new Vb6ReadOnlyRegionReader());
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode("Call Greet\r\n");
        vm.Initialize(module);
        var view = new CodeEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var editor = view.FindControl<AvaloniaEdit.TextEditor>("TextEditor")!;
            editor.TextArea.Focus();
            var bodyLine = FormCodeText.LineBreaks(FormCodeText.Prefix(module));
            editor.CaretOffset = editor.Document.GetLineByNumber(bodyLine + 1).Offset + 7;
            var command = implementation ? ApplicationCommands.GoToImplementationCommand : ApplicationCommands.GoToTypeDefinitionCommand;
            command.CanExecute(null, editor.TextArea).Should().BeTrue();
            command.Execute(null, editor.TextArea);
            Dispatcher.UIThread.RunJobs();
            if (implementation)
                client.Received(1).RequestImplementationAsync(vm.GetDocumentUriPublic(), new Position(bodyLine, 7), Arg.Any<CancellationToken>());
            else
                client.Received(1).RequestTypeDefinitionAsync(vm.GetDocumentUriPublic(), new Position(bodyLine, 7), Arg.Any<CancellationToken>());
            if (count > 1)
            {
                shown.Should().Be(1);
                results.Rows.Where(r => r.Location is not null).Select(r => r.Location).Should().Equal(answer);
                editors.DidNotReceive().NavigateTo(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>());
            }
            else
            {
                shown.Should().Be(0);
                if (count == 1)
                    editors.Received(1).NavigateTo(answer[0].Uri, 3, 4);
                else
                    editors.DidNotReceive().NavigateTo(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>());
            }
            client.SupportsCapability(Arg.Any<string>(), "implementationProvider").Returns(false);
            ApplicationCommands.GoToImplementationCommand.CanExecute(null, editor.TextArea).Should().BeFalse();
            ApplicationCommands.GoToTypeDefinitionCommand.CanExecute(null, editor.TextArea).Should().BeTrue();
            client.SupportsCapability(Arg.Any<string>(), "typeDefinitionProvider").Returns(false);
            ApplicationCommands.GoToTypeDefinitionCommand.CanExecute(null, editor.TextArea).Should().BeFalse();
        }
        finally { window.Close(); }
    }
}
