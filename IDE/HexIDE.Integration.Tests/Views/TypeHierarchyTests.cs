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

public class TypeHierarchyTests
{
    private readonly ILspClient client = Substitute.For<ILspClient>();
    private readonly IEditorService editors = Substitute.For<IEditorService>();
    private readonly LocalizationService localization = new();
    // RDCore's measured Implements hierarchy is recorded in hexide-io/HexIDE#4056.
    private static TypeHierarchyItem Item(string name) =>
        new(name, 5, $"file:///{name}.cls", new Range(new(0, 0), new(0, 0)), new Range(new(0, 0), new(0, 0)));

    private TypeHierarchyToolViewModel Model()
    {
        localization.Apply("en");
        client.FindTypeHierarchyAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(new TypeHierarchyResult([new(client, Item("IShape"), "probe")], []));
        client.RequestSubtypesAsync(Arg.Any<TypeHierarchyItem>(), Arg.Any<CancellationToken>())
            .Returns([Item("Circle"), Item("Square")]);
        client.RequestSupertypesAsync(Arg.Any<TypeHierarchyItem>(), Arg.Any<CancellationToken>()).Returns([]);
        return new(client, editors, localization);
    }

    [AvaloniaFact]
    public async Task GroupsRequestTheirLevelOncePerNodeOnFirstExpansion()
    {
        var vm = Model();
        await vm.SearchAsync("file:///IShape.cls", new(0, 0), TestContext.Current.CancellationToken);
        var view = new TypeHierarchyToolView { DataContext = vm };
        var window = new Window { Content = view, Width = 800, Height = 500 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var tree = view.GetVisualDescendants().OfType<TreeView>().Single();
            var root = (TreeViewItem)tree.ContainerFromIndex(0)!;
            root.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            client.ReceivedCalls().Count(c => c.GetMethodInfo().Name is "RequestSubtypesAsync" or "RequestSupertypesAsync").Should().Be(0);
            vm.Roots[0].Children.Select(g => g.Label).Should().Equal("Subtypes", "Supertypes");
            for (var index = 0; index < 2; index++)
            {
                var group = (TreeViewItem)root.ContainerFromIndex(index)!;
                group.IsExpanded = true;
                Dispatcher.UIThread.RunJobs();
                group.IsExpanded = false;
                group.IsExpanded = true;
            }
            vm.Roots[0].Children[0].Children.Select(n => n.Label).Should().Equal("Circle", "Square");
            vm.Roots[0].Children[1].Children.Single().Label.Should().Be("No types found.");
            await client.Received(1).RequestSubtypesAsync(Arg.Is<TypeHierarchyItem>(i => i.Name == "IShape"), Arg.Any<CancellationToken>());
            await client.Received(1).RequestSupertypesAsync(Arg.Is<TypeHierarchyItem>(i => i.Name == "IShape"), Arg.Any<CancellationToken>());
            var subtypes = (TreeViewItem)root.ContainerFromIndex(0)!;
            var circle = (TreeViewItem)subtypes.ContainerFromIndex(0)!;
            circle.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            ((TreeViewItem)circle.ContainerFromIndex(0)!).IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            await client.Received(1).RequestSubtypesAsync(Arg.Is<TypeHierarchyItem>(i => i.Name == "Circle"), Arg.Any<CancellationToken>());
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActivationNavigatesToTheSelectionRange(bool doubleClick)
    {
        var vm = Model();
        // Distinct synthetic ranges prove that selectionRange, rather than range, is used.
        var item = Item("IShape") with { SelectionRange = new Range(new(2, 4), new(2, 10)) };
        client.FindTypeHierarchyAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(new TypeHierarchyResult([new(client, item)], []));
        await vm.SearchAsync(item.Uri, new(0, 0), TestContext.Current.CancellationToken);
        editors.NavigateTo(item.Uri, 3, 5).Returns(true);
        var view = new TypeHierarchyToolView { DataContext = vm };
        var window = new Window { Content = view, Width = 800, Height = 500 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var tree = view.GetVisualDescendants().OfType<TreeView>().Single();
            var root = (TreeViewItem)tree.ContainerFromIndex(0)!;
            root.IsSelected = true;
            root.Focus();
            if (doubleClick)
                root.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent,
                    new PointerEventArgs(InputElement.PointerPressedEvent, root, new Pointer(1, PointerType.Mouse, true),
                        root, default, 0, default, KeyModifiers.None)));
            else
            {
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            }
            editors.Received(1).NavigateTo(item.Uri, 3, 5);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpansionErrorsNameTheOwningServer(bool subtypes)
    {
        var vm = Model();
        client.RequestSubtypesAsync(Arg.Any<TypeHierarchyItem>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<TypeHierarchyItem[]>(new InvalidOperationException("refused")));
        client.RequestSupertypesAsync(Arg.Any<TypeHierarchyItem>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<TypeHierarchyItem[]>(new InvalidOperationException("refused")));
        await vm.SearchAsync("file:///IShape.cls", new(0, 0), TestContext.Current.CancellationToken);
        var group = vm.Roots[0].Children[subtypes ? 0 : 1];
        group.IsExpanded = true;
        group.Children.Single().Label.Should().Be("probe: refused");
        vm.SelectedNode = group;
        vm.NavigateSelected().Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task EveryPreparedItemBecomesARootAndOldExpansionsCannotReplaceANewSearch()
    {
        var vm = Model();
        client.FindTypeHierarchyAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(new TypeHierarchyResult([new(client, Item("IShape")), new(client, Item("Circle"))], []));
        var pending = new TaskCompletionSource<TypeHierarchyItem[]>();
        client.RequestSubtypesAsync(Arg.Any<TypeHierarchyItem>(), Arg.Any<CancellationToken>()).Returns(pending.Task);
        await vm.SearchAsync("file:///IShape.cls", new(0, 0), TestContext.Current.CancellationToken);
        vm.Roots.Select(n => n.Label).Should().Equal("IShape", "Circle");
        var old = vm.Roots[0].Children[0];
        old.IsExpanded = true;
        await vm.SearchAsync("file:///Circle.cls", new(0, 0), TestContext.Current.CancellationToken);
        pending.SetResult([Item("Square")]);
        Dispatcher.UIThread.RunJobs();
        vm.Roots[0].Children[0].Children.Single().Label.Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task EmptyAnswersAndErrorsReplaceThePreviousSearch()
    {
        var vm = Model();
        await vm.SearchAsync("file:///IShape.cls", new(0, 0), TestContext.Current.CancellationToken);
        client.FindTypeHierarchyAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(new TypeHierarchyResult([], []));
        await vm.SearchAsync("file:///Circle.cls", new(0, 0), TestContext.Current.CancellationToken);
        vm.Roots.Single().Label.Should().Be("No types found.");
        vm.SelectedNode = vm.Roots[0];
        vm.NavigateSelected().Should().BeFalse();
        client.FindTypeHierarchyAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(new TypeHierarchyResult([], [new("probe", "refused")]));
        await vm.SearchAsync("file:///IShape.cls", new(0, 0), TestContext.Current.CancellationToken);
        vm.Roots.Single().Label.Should().Be("probe: refused");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void CommandIsGatedAndRequestsTheCaret(bool provider)
    {
        var pane = Model();
        client.SupportsTypeHierarchy(Arg.Any<string>()).Returns(provider);
        var events = new EventBus();
        using var sub = events.Subscribe<TypeHierarchyEvent>(e => pane.SearchAsync(e.Uri, e.Position).GetAwaiter().GetResult());
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
            items[references + 2].Command.Should().BeSameAs(ApplicationCommands.TypeHierarchyCommand);
            var command = ApplicationCommands.TypeHierarchyCommand;
            command.CanExecute(null, editor).Should().Be(provider);
            command.Execute(null, editor);
            client.Received(provider ? 1 : 0).FindTypeHierarchyAsync(vm.GetDocumentUriPublic(), new Position(line, 7), Arg.Any<CancellationToken>());
            pane.Roots.Count.Should().Be(provider ? 1 : 0);
        }
        finally { window.Close(); }
    }
}
