using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
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

public class FindReferencesTests
{
    [AvaloniaFact]
    public void ShiftF12SearchesAtTheCaret()
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
        client.FindReferencesAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(new ReferenceSearchResult([], []));
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);
        var events = new EventBus();
        using var subscription = events.Subscribe<FindReferencesEvent>(e => results.SearchAsync(e.Uri, e.Position).GetAwaiter().GetResult());
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
            window.KeyPress(Key.F12, RawInputModifiers.Shift, PhysicalKey.F12, null);
            window.KeyRelease(Key.F12, RawInputModifiers.Shift, PhysicalKey.F12, null);
            client.Received(1).FindReferencesAsync(vm.GetDocumentUriPublic(), new Position(bodyLine, 7), Arg.Any<CancellationToken>());
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnterAndDoubleClickNavigateTheSelectedLocation(bool doubleClick)
    {
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([]);
        var documents = Substitute.For<IDocumentDockService>();
        documents.OpenDocuments.Returns([]);
        var editors = Substitute.For<IEditorService>();
        editors.NavigateTo("untitled:P/Module1.bas", 3, 6).Returns(true);
        var localization = new LocalizationService();
        localization.Apply("en");
        var vm = new FindReferencesToolViewModel(Substitute.For<ILspClient>(), editors, projects, documents, localization,
            format: new Vb6ProjectFileFormat());
        vm.ShowLocations([new Location("untitled:P/Module1.bas", new HexIDE.Lsp.Messages.Range(new Position(2, 5), new Position(2, 10)))]);
        var view = new FindReferencesToolView { DataContext = vm };
        var window = new Window { Content = view, Width = 800, Height = 400 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var list = (ListBox)view.Content!;
            list.SelectedIndex = 1;
            if (doubleClick)
                list.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent,
                    new PointerEventArgs(InputElement.PointerPressedEvent, list, new Pointer(1, PointerType.Mouse, true),
                        list, default, 0, default, KeyModifiers.None)));
            else
            {
                var row = (ListBoxItem)list.ContainerFromIndex(1)!;
                row.Focus().Should().BeTrue();
                var handledByList = false;
                list.AddHandler(InputElement.KeyDownEvent, (_, e) => handledByList = e.Handled,
                    handledEventsToo: true);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                handledByList.Should().BeTrue("the ListBox consumes Enter before it bubbles to the view");
            }
            editors.Received(1).NavigateTo("untitled:P/Module1.bas", 3, 6);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ResultRowAutomationNameReadsModuleLineAndText()
    {
        var projects = Substitute.For<IProjectManager>();
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode("Call Greet\r\n");
        projects.LoadedProjects.Returns([]);
        var documents = Substitute.For<IDocumentDockService>();
        documents.OpenDocuments.Returns([]);
        var editors = Substitute.For<IEditorService>();
        var localization = new LocalizationService();
        localization.Apply("en");
        using var editor = new CodeEditorViewModel(Substitute.For<IWindowManager>(), editors,
            Substitute.For<IProjectService>(), new EventBus(), Substitute.For<ILspClient>(),
            Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(),
            Substitute.For<IBookmarkService>(), Substitute.For<HexIDE.Debugging.IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            Substitute.For<HexIDE.Debugging.IRunScope>(), localization, new Vb6ProjectFileFormat(), new Vb6ReadOnlyRegionReader());
        editor.Initialize(module);
        // Use the open editor's document so the accessible name includes unsaved line text.
        documents.OpenDocuments.Returns([editor]);
        var uri = editor.GetDocumentUriPublic();
        editors.DocumentNameForUri(uri).Returns("Module1");
        var line = FormCodeText.LineBreaks(FormCodeText.Prefix(module));
        var vm = new FindReferencesToolViewModel(Substitute.For<ILspClient>(), editors, projects, documents, localization,
            format: new Vb6ProjectFileFormat());
        vm.ShowLocations([new Location(uri, new HexIDE.Lsp.Messages.Range(new Position(line, 5), new Position(line, 10)))]);
        var view = new FindReferencesToolView { DataContext = vm };
        var window = new Window { Content = view, Width = 800, Height = 400 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var list = (ListBox)view.Content!;
            var group = (ListBoxItem)list.ContainerFromIndex(0)!;
            var row = (ListBoxItem)list.ContainerFromIndex(1)!;
            ControlAutomationPeer.CreatePeerForElement(group)!.GetName().Should().Be("Module1");
            ControlAutomationPeer.CreatePeerForElement(row)!.GetName().Should().Be($"Module1 {line + 1} Call Greet");
        }
        finally { window.Close(); }
    }
}
