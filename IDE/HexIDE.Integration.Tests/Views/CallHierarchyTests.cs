using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Bookmarks;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Tools;
using Range = HexIDE.Lsp.Messages.Range;

namespace HexIDE.Integration.Tests.Views;

public class CallHierarchyTests
{
    private readonly ILspClient client = Substitute.For<ILspClient>();
    private readonly IEditorService editors = Substitute.For<IEditorService>();
    private readonly LocalizationService localization = new();
    private static CallHierarchyItem Item(string name, string uri = "file:///Module1.bas") =>
        new(name, 12, uri, new Range(new(2, 1), new(5, 0)), new Range(new(2, 4), new(2, 8)));

    private CallHierarchyToolViewModel Model()
    {
        localization.Apply("en");
        client.FindCallHierarchyAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(new CallHierarchyResult([new(client, Item("Root"), "probe")], []));
        client.RequestIncomingCallsAsync(Arg.Any<CallHierarchyItem>(), Arg.Any<CancellationToken>())
            .Returns([new(Item("Caller", "file:///Caller.bas"), [new Range(new(7, 3), new(7, 7))])]);
        client.RequestOutgoingCallsAsync(Arg.Any<CallHierarchyItem>(), Arg.Any<CancellationToken>())
            .Returns([new(Item("Callee", "file:///Callee.bas"), [new Range(new(9, 2), new(9, 6))])]);
        return new(client, editors, localization);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpeningNodesRequestsOnlyTheSelectedDirection(bool outgoing)
    {
        var vm = Model();
        var view = new CallHierarchyToolView { DataContext = vm };
        var window = new Window { Content = view, Width = 800, Height = 500 };
        try
        {
            window.Show();
            if (outgoing) view.GetVisualDescendants().OfType<RadioButton>().Last().IsChecked = true;
            await vm.SearchAsync("file:///Module1.bas", new(3, 4), TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            client.ReceivedCalls().Count(c => c.GetMethodInfo().Name is "RequestIncomingCallsAsync" or "RequestOutgoingCallsAsync")
                .Should().Be(0);
            var tree = view.GetVisualDescendants().OfType<TreeView>().Single();
            var root = (TreeViewItem)tree.ContainerFromIndex(0)!;
            root.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            vm.Roots[0].Children.Single().Label.Should().Be(outgoing ? "Callee" : "Caller");
            root.IsExpanded = false;
            root.IsExpanded = true;
            client.ReceivedCalls().Count(c => c.GetMethodInfo().Name ==
                (outgoing ? "RequestOutgoingCallsAsync" : "RequestIncomingCallsAsync")).Should().Be(1);
            var modes = view.GetVisualDescendants().OfType<RadioButton>().ToArray();
            modes[outgoing ? 0 : 1].IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            ((TreeViewItem)tree.ContainerFromIndex(0)!).IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            vm.Roots[0].Children.Single().Label.Should().Be(outgoing ? "Caller" : "Callee");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task NodeAndCallSiteNavigateOnEnterOrDoubleClick(bool outgoing, bool site, bool doubleClick)
    {
        var vm = Model();
        vm.CallsFrom = outgoing;
        await vm.SearchAsync("file:///Module1.bas", new(3, 4), TestContext.Current.CancellationToken);
        var view = new CallHierarchyToolView { DataContext = vm };
        var window = new Window { Content = view, Width = 800, Height = 500 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var tree = view.GetVisualDescendants().OfType<TreeView>().Single();
            var root = (TreeViewItem)tree.ContainerFromIndex(0)!;
            root.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            var child = (TreeViewItem)root.ContainerFromIndex(0)!;
            TreeViewItem selected = child;
            if (site)
            {
                child.IsExpanded = true;
                Dispatcher.UIThread.RunJobs();
                selected = (TreeViewItem)child.ContainerFromIndex(0)!;
            }
            selected.IsSelected = true;
            selected.Focus();
            if (doubleClick)
                selected.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent,
                    new PointerEventArgs(InputElement.PointerPressedEvent, selected, new Pointer(1, PointerType.Mouse, true),
                        selected, default, 0, default, KeyModifiers.None)));
            else
            {
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            }
            var uri = site && outgoing ? "file:///Module1.bas" : outgoing ? "file:///Callee.bas" : "file:///Caller.bas";
            editors.Received(1).NavigateTo(uri, site ? outgoing ? 10 : 8 : 3, site ? outgoing ? 3 : 4 : 5);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ALeafKeepsItsCallSitesAndReportsNoFurtherCalls(bool outgoing)
    {
        var vm = Model();
        vm.CallsFrom = outgoing;
        client.RequestIncomingCallsAsync(Arg.Is<CallHierarchyItem>(i => i.Name == "Caller"), Arg.Any<CancellationToken>())
            .Returns([]);
        client.RequestOutgoingCallsAsync(Arg.Is<CallHierarchyItem>(i => i.Name == "Callee"), Arg.Any<CancellationToken>())
            .Returns([]);
        await vm.SearchAsync("file:///Module1.bas", new(3, 4), TestContext.Current.CancellationToken);
        var view = new CallHierarchyToolView { DataContext = vm };
        var window = new Window { Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var tree = view.GetVisualDescendants().OfType<TreeView>().Single();
            var root = (TreeViewItem)tree.ContainerFromIndex(0)!;
            root.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            var child = (TreeViewItem)root.ContainerFromIndex(0)!;
            child.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            var rows = vm.Roots[0].Children[0].Children;
            rows.Should().HaveCount(2);
            rows[0].Label.Should().StartWith("Call at ");
            rows[1].Label.Should().Be("No calls found.");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ExpansionShowsEmptyAnswersAndTheOwningServerError(bool outgoing, bool error)
    {
        var vm = Model();
        vm.CallsFrom = outgoing;
        client.RequestIncomingCallsAsync(Arg.Any<CallHierarchyItem>(), Arg.Any<CancellationToken>())
            .Returns(error ? Task.FromException<CallHierarchyIncomingCall[]>(new InvalidOperationException("refused"))
                : Task.FromResult<CallHierarchyIncomingCall[]>([]));
        client.RequestOutgoingCallsAsync(Arg.Any<CallHierarchyItem>(), Arg.Any<CancellationToken>())
            .Returns(error ? Task.FromException<CallHierarchyOutgoingCall[]>(new InvalidOperationException("refused"))
                : Task.FromResult<CallHierarchyOutgoingCall[]>([]));
        await vm.SearchAsync("file:///Module1.bas", new(3, 4), TestContext.Current.CancellationToken);
        var view = new CallHierarchyToolView { DataContext = vm };
        var window = new Window { Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var tree = view.GetVisualDescendants().OfType<TreeView>().Single();
            ((TreeViewItem)tree.ContainerFromIndex(0)!).IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            vm.Roots.Single().Children.Single().Label.Should().Be(error ? "probe: refused" : "No calls found.");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task RerunReplacesTheTreeInTheSamePaneAndShowsErrorsAndEmptyAnswers()
    {
        var vm = Model();
        var shows = 0;
        vm.ShowRequested += () => shows++;
        var view = new CallHierarchyToolView { DataContext = vm };
        var window = new Window { Content = view };
        try
        {
            window.Show();
            await vm.SearchAsync("file:///Module1.bas", new(3, 4), TestContext.Current.CancellationToken);
            var old = vm.Roots[0];
            client.FindCallHierarchyAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
                .Returns(new CallHierarchyResult([], [new("probe", "refused")]));
            await vm.SearchAsync("file:///Module1.bas", new(8, 2), TestContext.Current.CancellationToken);
            vm.Roots.Should().NotContain(old);
            vm.Roots.Single().Label.Should().Contain("probe").And.Contain("refused");
            client.FindCallHierarchyAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
                .Returns(new CallHierarchyResult([], []));
            await vm.SearchAsync("file:///Module1.bas", new(8, 2), TestContext.Current.CancellationToken);
            vm.Roots.Single().Label.Should().Be("No calls found.");
            view.DataContext.Should().BeSameAs(vm);
            shows.Should().Be(3);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void CommandIsGatedAndRequestsTheCaret(bool provider)
    {
        var pane = Model();
        client.SupportsCallHierarchy(Arg.Any<string>()).Returns(provider);
        var events = new EventBus();
        using var sub = events.Subscribe<CallHierarchyEvent>(e => pane.SearchAsync(e.Uri, e.Position).GetAwaiter().GetResult());
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);
        using var vm = new CodeEditorViewModel(Substitute.For<IWindowManager>(), editors,
            Substitute.For<IProjectService>(), events, client, settings,
            Substitute.For<IStatusBarService>(), Substitute.For<IBookmarkService>(),
            Substitute.For<HexIDE.Debugging.IBreakpointService>(), Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
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
            var line = FormCodeText.LineBreaks(FormCodeText.Prefix(module));
            editor.CaretOffset = editor.Document.GetLineByNumber(line + 1).Offset + 7;
            var items = ((MenuFlyout)editor.ContextFlyout!).Items.OfType<MenuItem>().ToList();
            var references = items.FindIndex(i => ReferenceEquals(i.Command, ApplicationCommands.FindReferencesCommand));
            references.Should().BeGreaterThanOrEqualTo(0);
            items[references + 1].Command.Should().BeSameAs(ApplicationCommands.CallHierarchyCommand);
            var command = ApplicationCommands.CallHierarchyCommand;
            command.CanExecute(null, editor).Should().Be(provider);
            command.Execute(null, editor);
            client.Received(provider ? 1 : 0).FindCallHierarchyAsync(vm.GetDocumentUriPublic(), new Position(line, 7), Arg.Any<CancellationToken>());
            pane.Roots.Count.Should().Be(provider ? 1 : 0);
        }
        finally { window.Close(); }
    }
}
