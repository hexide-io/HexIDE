using System;
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
    private static (ObjectBrowserToolViewModel Vm, ILspClient Client, IEditorService Editor, string Uri) Create(bool form, ModuleKind moduleKind = ModuleKind.StandardModule)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "TestProject");
        var module = new ModuleDefinition(project, "Module1", moduleKind);
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

    // Measured in VB6 SP6: docs/vb6-fidelity-oracle/the-object-browsers-library-list.md,
    // "A form's members in the Object Browser"; arrays and menus: issue #1968, 2026-11-22.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FormListsControlsAndPropertiesAlongsideCodeInNameOrder(bool withCode)
    {
        var (vm, client, _, uri) = Create(true);
        var type = vm.FilteredClasses.Single();
        var form = type.FormDefinition!;
        form.UpdateComponents([..form.Components,
            new ComponentInstance(CommandButtonComponentClass.Instance, "Command1")]);
        client.RequestDocumentSymbolsAsync(uri, Arg.Any<CancellationToken>())
            .Returns(withCode ? [Symbol("NewThing"), Symbol("aaa"), Symbol("Caption")] : []);

        vm.SelectedClass = type;

        var control = vm.FilteredMembers.Single(m => m.Name == "Command1");
        control.Kind.Should().Be(OBMemberKind.Property);
        control.Signature.Should().Be("WithEvents Command1 As CommandButton");
        control.DeclarationPosition.Should().BeNull();
        vm.FilteredMembers.Should().Contain(m => m.Name == "Caption" && m.Signature == "Property Caption");
        // VB6 lists the form's Name, not the Properties window's (Name) (oracle, "A form with no code").
        vm.FilteredMembers.Should().Contain(m => m.Name == "Name" && m.Signature == "Property Name");
        vm.FilteredMembers.Should().NotContain(m => m.Name.Contains('('));
        vm.FilteredMembers.Should().NotContain(m => m.Name == "Load" || m.Name == "Click" || m.Name == "Form1");
        vm.FilteredMembers.Select(m => m.Name).Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
        if (withCode)
        {
            vm.FilteredMembers.Should().Contain(m => m.Name == "NewThing");
            vm.FilteredMembers.Count(m => m.Name == "Caption").Should().Be(2);
        }
    }

    [AvaloniaFact]
    public void ControlArraysAppearOnceAndEveryMenuLevelAppears()
    {
        var (vm, _, _, _) = Create(true);
        var type = vm.FilteredClasses.Single();
        var form = type.FormDefinition!;
        var child = new ComponentInstance(MenuComponentClass.Instance, "mnuOpen");
        var parent = new ComponentInstance(MenuComponentClass.Instance, "mnuFile")
            .SetProperty(MenuComponentClass.SubItemsProperty, new List<ComponentInstance> { child });
        form.UpdateComponents([..form.Components,
            new ComponentInstance(CommandButtonComponentClass.Instance, "Command1").SetProperty(VBProperties.DesignIndexProperty, "0"),
            new ComponentInstance(CommandButtonComponentClass.Instance, "command1").SetProperty(VBProperties.DesignIndexProperty, "1"),
            parent, child]);

        vm.SelectedClass = type;

        vm.FilteredMembers.Count(m => m.Name.Equals("Command1", StringComparison.OrdinalIgnoreCase)).Should().Be(1);
        foreach (var name in new[] { "Command1", "mnuFile", "mnuOpen" })
        {
            var member = vm.FilteredMembers.Single(m => m.Name == name);
            member.Kind.Should().Be(OBMemberKind.Property);
            member.Signature.Should().Be($"WithEvents {name} As {(name == "Command1" ? "CommandButton" : "Menu")}");
        }
    }

    [AvaloniaTheory]
    [InlineData(ModuleKind.UserControl)]
    [InlineData(ModuleKind.PropertyPage)]
    public void DesignerModuleUsesItsOwnRootProperties(ModuleKind kind)
    {
        var (vm, client, _, _) = Create(false, kind);
        var type = vm.FilteredClasses.Single();
        var module = type.ModuleDefinition!;
        var root = kind == ModuleKind.UserControl
            ? (IComponentClass)UserControlRootClass.Instance : PropertyPageRootClass.Instance;
        var form = FormDefinition.NewDesignerHalf(module.Owner, root, module.Name, root.VBTypeName);
        form.UpdateComponents([..form.Components, new ComponentInstance(TextBoxComponentClass.Instance, "Text1")]);
        module.UpdateFormPart(form);
        client.RequestDocumentSymbolsAsync(DocumentWireName.For(module), Arg.Any<CancellationToken>()).Returns([Symbol("Ping")]);

        vm.SelectedClass = type;

        // Code names, as VB6 lists them: Name rather than (Name), and no Properties-window pseudo-rows.
        var rootNames = root.Properties
            .Where(p => p.Category != PropertyCategory.Internal)
            .Select(p => p == VBProperties.NameProperty ? "Name" : p.Name)
            .Where(n => !n.Contains('(') && !n.Contains(')'));
        vm.FilteredMembers.Select(m => m.Name).Should().BeEquivalentTo(rootNames.Concat(["Text1", "Ping"]));
        vm.FilteredMembers.Should().Contain(m => m.Name == "Name");
        vm.FilteredMembers.Should().Contain(m => m.Signature == "WithEvents Text1 As TextBox");
        vm.FilteredMembers.Should().NotContain(m => m.Name == "Initialize" || m.Name == "ControlBox");
        vm.FilteredMembers.Select(m => m.Name).Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReselectingProjectTypeReplacesMembersWithCurrentSymbols(bool form)
    {
        var (vm, client, _, uri) = Create(form);
        client.RequestDocumentSymbolsAsync(uri, Arg.Any<CancellationToken>()).Returns([Symbol("OldThing")]);
        var type = vm.FilteredClasses.Single();
        vm.SelectedClass = type;
        if (form) vm.FilteredMembers.Should().Contain(m => m.Name == "OldThing");
        else vm.FilteredMembers.Select(m => m.Name).Should().Equal("OldThing");
        vm.SelectedClass = null;
        client.RequestDocumentSymbolsAsync(uri, Arg.Any<CancellationToken>()).Returns([Symbol("NewThing")]);
        vm.SelectedClass = type;
        if (form)
        {
            vm.FilteredMembers.Should().NotContain(m => m.Name == "OldThing");
            vm.FilteredMembers.Should().Contain(m => m.Name == "NewThing");
            type.Members.Should().Contain(m => m.Name == "Caption");
        }
        else
        {
            vm.FilteredMembers.Select(m => m.Name).Should().Equal("NewThing");
            type.Members.Select(m => m.Name).Should().Equal("NewThing");
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DoubleClickNavigatesToSelectedMembersDeclaration(bool form)
    {
        var (vm, client, editor, uri) = Create(form);
        client.RequestDocumentSymbolsAsync(uri, Arg.Any<CancellationToken>()).Returns([Symbol("Thing")]);
        vm.SelectedClass = vm.FilteredClasses.Single();
        vm.SelectedMember = vm.FilteredMembers.Single(m => m.Name == "Thing");
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
