using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Dock.Model.Core;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Tools.ObjectBrowser;
using LspRange = HexIDE.Lsp.Messages.Range;

namespace HexIDE.Integration.Tests.Views;

public class ObjectBrowserBehaviourTests
{
    private static (ObjectBrowserToolViewModel Vm, ILspClient Client, IEditorService Editor, string Uri) Create(bool form)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "TestProject");
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        var definition = new FormDefinition(project, FormComponentClass.Instance, "Form1");
        if (form) project.AddForm(definition);
        else project.AddModule(module);
        var pm = Substitute.For<IProjectManager>();
        pm.LoadedProjects.Returns(new List<ProjectDefinition> { project });
        var client = Substitute.For<ILspClient>();
        client.IsRunning.Returns(true);
        var editor = Substitute.For<IEditorService>();
        var vm = new ObjectBrowserToolViewModel(pm, client, editor, Substitute.For<IComponentRegistry>(),
            Substitute.For<ITypeLibraryService>(), Substitute.For<IFocusedProjectUtil>(),
            Substitute.For<ILocalizationService>(), Substitute.For<ILanguageConnectionRegistry>());
        return (vm, client, editor, form ? DocumentWireName.For(definition) : DocumentWireName.For(module));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServerStartingReplacesVbaStatusWithoutReselecting(bool allLibraries)
    {
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns(new List<ProjectDefinition>());
        var client = Substitute.For<ILspClient>();
        client.RequestBuiltinSymbolsAsync(Arg.Any<CancellationToken>())
            .Returns([new VbaBuiltinSymbol("Abs", "Function Abs(number)", "Absolute value")]);
        var registry = Substitute.For<ILanguageConnectionRegistry>();
        registry.Connections.Returns([]);
        var vm = new ObjectBrowserToolViewModel(projects, client, Substitute.For<IEditorService>(),
            Substitute.For<IComponentRegistry>(), Substitute.For<ITypeLibraryService>(),
            Substitute.For<IFocusedProjectUtil>(), Substitute.For<ILocalizationService>(), registry);
        var vba = vm.Libraries.Single(l => l.Name == "VBA");
        if (!allLibraries) vm.SelectedLibrary = vba;
        var selection = vm.SelectedLibrary;
        vba.Classes.Should().ContainSingle().Which.IsStatus.Should().BeTrue();

        using var caps = System.Text.Json.JsonDocument.Parse(
            """{"experimental":{"vbBuiltinSymbols":true}}""");
        registry.Connections.Returns([new LanguageServerConnection("server", "Server",
            LanguageConnectionKind.LanguageServer, LanguageConnectionState.Running, [".bas"], "vb6",
            caps.RootElement.Clone())]);
        await Task.Run(() => registry.ConnectionsChanged += Raise.Event());
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.SelectedLibrary.Should().BeSameAs(selection);
        vba.IsLoaded.Should().BeTrue();
        vba.Classes.Should().ContainSingle().Which.IsStatus.Should().BeFalse();
        vm.FilteredClasses.Should().ContainSingle(c => c.LibraryName == "VBA");
        vm.SelectedClass = vba.Classes.Single();
        vm.FilteredMembers.Should().ContainSingle().Which.Name.Should().Be("Abs");
    }

    // The IDE raises LanguageChanged on the UI thread (Apply swaps Application.Resources), and the browser refreshes there
    // at once rather than queuing it (#4382).
    [AvaloniaFact]
    public void LanguageSwitchRefreshesStatusAtOnceOnTheUiThread()
    {
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns(new List<ProjectDefinition>());
        var loc = Substitute.For<ILocalizationService>();
        loc.GetString(Arg.Any<string>()).Returns(ci => "Old " + ci.Arg<string>());
        var vm = new ObjectBrowserToolViewModel(projects, Substitute.For<ILspClient>(),
            Substitute.For<IEditorService>(), Substitute.For<IComponentRegistry>(),
            Substitute.For<ITypeLibraryService>(), Substitute.For<IFocusedProjectUtil>(), loc,
            Substitute.For<ILanguageConnectionRegistry>());
        vm.SelectedLibrary = vm.Libraries.Single(l => l.Name == "VBA");
        vm.SelectedClass = vm.FilteredClasses.Single();
        var cls = vm.SelectedClass;
        var changedOnUiThread = false;
        cls.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(cls.Name))
                changedOnUiThread = Avalonia.Threading.Dispatcher.UIThread.CheckAccess();
        };
        loc.GetString(Arg.Any<string>()).Returns(ci => "New " + ci.Arg<string>());

        loc.LanguageChanged += Raise.Event<System.Action>();

        cls.Name.Should().Be("New Str.Tool.ObjectBrowser.Vba.NoServer");
        vm.SelectedClass.Should().BeSameAs(cls);
        changedOnUiThread.Should().BeTrue();
    }

    private static DocumentSymbol Symbol(string name) => new(name, SymbolKind.Method,
        new LspRange(new Position(7, 0), new Position(12, 0)),
        new LspRange(new Position(8, 11), new Position(8, 11 + name.Length)));

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReselectingProjectTypeReplacesMembersWithCurrentSymbols(bool form)
    {
        var (vm, client, _, uri) = Create(form);
        client.RequestDocumentSymbolsAsync(uri, Arg.Any<CancellationToken>()).Returns([Symbol("OldThing")]);
        var type = vm.FilteredClasses.Single();
        vm.SelectedClass = type;
        vm.FilteredMembers.Select(m => m.Name).Should().Equal("OldThing");
        vm.SelectedClass = null;
        client.RequestDocumentSymbolsAsync(uri, Arg.Any<CancellationToken>()).Returns([Symbol("NewThing")]);
        vm.SelectedClass = type;
        vm.FilteredMembers.Select(m => m.Name).Should().Equal("NewThing");
        type.Members.Select(m => m.Name).Should().Equal("NewThing");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DoubleClickNavigatesToSelectedMembersDeclaration(bool form)
    {
        var (vm, client, editor, uri) = Create(form);
        client.RequestDocumentSymbolsAsync(uri, Arg.Any<CancellationToken>()).Returns([Symbol("Thing")]);
        vm.SelectedClass = vm.FilteredClasses.Single();
        vm.SelectedMember = vm.FilteredMembers.Single();
        var view = new ObjectBrowserToolView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();
        try
        {
            var members = view.GetVisualDescendants().OfType<ListBox>()
                .Single(list => ReferenceEquals(list.ItemsSource, vm.FilteredMembers));
            members.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent,
                new PointerEventArgs(InputElement.PointerPressedEvent, members, new Pointer(1, PointerType.Mouse, true),
                    members, default, 0, default, KeyModifiers.None)));
            editor.Received(1).NavigateTo(uri, 9, 12);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void OlderMemberReplyCannotOverwriteReselection()
    {
        var (vm, client, _, uri) = Create(false);
        var oldReply = new TaskCompletionSource<DocumentSymbol[]>();
        client.RequestDocumentSymbolsAsync(uri, Arg.Any<CancellationToken>()).Returns(oldReply.Task);
        var type = vm.FilteredClasses.Single();
        vm.SelectedClass = type;
        vm.SelectedClass = null;
        client.RequestDocumentSymbolsAsync(uri, Arg.Any<CancellationToken>()).Returns([Symbol("NewThing")]);
        vm.SelectedClass = type;
        oldReply.SetResult([Symbol("OldThing")]);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.FilteredMembers.Select(m => m.Name).Should().Equal("NewThing");
    }

    [AvaloniaFact]
    public void EscapeFromSearchClosesBrowserThroughDockFactory()
    {
        var (vm, _, _, _) = Create(false);
        var factory = Substitute.For<IFactory>();
        vm.Factory = factory;
        vm.Owner = Substitute.For<IDock>();
        var view = new ObjectBrowserToolView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();
        try
        {
            view.GetVisualDescendants().OfType<TextBox>().Single(box => box.Parent is DockPanel).Focus().Should().BeTrue();
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            factory.Received(1).CloseDockable(vm);
        }
        finally { window.Close(); }
    }
}
