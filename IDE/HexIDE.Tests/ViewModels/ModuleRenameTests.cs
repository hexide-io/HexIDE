using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Tools;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.ViewModels;

public class ModuleRenameTests
{
    private readonly EventBus bus = new();
    private readonly IDocumentDockService dock = Substitute.For<IDocumentDockService>();
    private readonly IWindowManager windows = Substitute.For<IWindowManager>();
    private readonly ILocalizationService loc = Substitute.For<ILocalizationService>();
    private readonly ProjectDefinition project = TestHelpers.CreateProjectWithForm("P", "Form1");

    private PropertiesToolViewModel Grid()
    {
        dock.OpenDocuments.Returns(new List<HexIDE.Forms.ViewModels.BaseEditorWindowViewModel>());
        loc.GetString("Str.Naming.Msg.NotAVb6Name").Returns("Invalid: {0}");
        loc.GetString("Str.Naming.Msg.DocumentNameTaken").Returns("Taken: {0}");
        _ = new HeaderRefresher(bus, dock, new BreakpointService(), new BookmarkService());
        return new PropertiesToolViewModel(dock, bus, windows, loc);
    }

    private ModuleDefinition Module(ModuleKind kind)
    {
        var module = kind is ModuleKind.UserControl or ModuleKind.PropertyPage
            ? ModuleDefinition.NewDesigner(project, "Original", kind,
                FormComponentClass.ForRoot(kind == ModuleKind.UserControl ? "VB.UserControl" : "VB.PropertyPage"))
            : new ModuleDefinition(project, "Original", kind);
        project.AddModule(module);
        return module;
    }

    [AvaloniaTheory]
    [InlineData(ModuleKind.StandardModule)]
    [InlineData(ModuleKind.ClassModule)]
    [InlineData(ModuleKind.UserControl)]
    [InlineData(ModuleKind.PropertyPage)]
    public void Explorer_selection_offers_a_name_and_renames_the_document(ModuleKind kind)
    {
        var module = Module(kind);
        module.AbsolutePath = "/unchanged/Original.bas";
        var grid = Grid();
        using var editor = new CodeEditorViewModel(windows, Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(), bus, Substitute.For<HexIDE.Lsp.ILspClient>(),
            Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(), new BookmarkService(),
            new BreakpointService(), Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            Substitute.For<IRunScope>(), loc).Initialize(module);
        dock.OpenDocuments.Returns(new List<BaseEditorWindowViewModel> { editor });
        editor.Document.Insert(editor.Document.TextLength, "Dim x As Long\r\n");
        var manager = Substitute.For<IProjectManager>();
        manager.LoadedProjects.Returns(new[] { project });
        var explorer = new ProjectToolViewModel(manager, bus, Substitute.For<IProjectService>(),
            Substitute.For<IEditorService>(), loc);
        explorer.SelectedModule = new ModuleViewModel(explorer.LoadedProjects[0], module);

        grid.Properties.Should().ContainSingle().Which.PropertyClass.Should().Be(VBProperties.NameProperty);
        grid.CategorizedProperties.Should().ContainSingle();
        grid.Properties[0].Value = "Renamed";

        module.Name.Should().Be("Renamed");
        editor.Document.Text.Should().Contain("Attribute VB_Name = \"Renamed\"");
        editor.UndoRequested(() => editor.Document.UndoStack.Undo());
        editor.BufferBody.Should().NotContain("Dim x As Long");
        editor.Document.Text.Should().Contain("Attribute VB_Name = \"Renamed\"");
        explorer.SelectedModule.Name.Should().Be("Renamed");
        module.AbsolutePath.Should().Be("/unchanged/Original.bas");
        if (module.FormPart is { } form)
        {
            form.Name.Should().Be("Renamed");
            module.Code.Should().Contain("Attribute VB_Name = \"Renamed\"");
        }
        dock.ActiveDocument.Returns(editor);
        dock.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(dock, new PropertyChangedEventArgs(nameof(dock.ActiveDocument)));
        grid.Properties.Should().ContainSingle().Which.Value.Should().Be("Renamed");
        dock.ActiveDocument.Returns((BaseEditorWindowViewModel?)null);
        explorer.SelectedItem = explorer.LoadedProjects[0];
        grid.Properties.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData("Form1", "Taken: Form1")]
    [InlineData("Other", "Taken: Other")]
    [InlineData("bad name", "Invalid: bad name")]
    [InlineData("", "Invalid: ")]
    public void Refused_module_names_restore_the_row(string proposed, string message)
    {
        var module = Module(ModuleKind.StandardModule);
        project.AddModule(new ModuleDefinition(project, "Other", ModuleKind.ClassModule));
        var grid = Grid();
        bus.Publish(new ModulePropertiesSelectedEvent(module));
        grid.Properties.Should().ContainSingle();
        grid.Properties[0].Value = proposed;
        Dispatcher.UIThread.RunJobs();
        module.Name.Should().Be("Original");
        grid.Properties[0].Value.Should().Be("Original");
        windows.Received().MessageBox(message, icon: MessageBoxIcon.Error);
    }

    [AvaloniaFact]
    public void A_case_change_ignores_itself_and_other_projects()
    {
        var module = Module(ModuleKind.StandardModule);
        var other = new ProjectDefinition(VBProjectType.EXE, "Other");
        other.AddModule(new ModuleDefinition(other, "original", ModuleKind.StandardModule));
        var grid = Grid();
        bus.Publish(new ModulePropertiesSelectedEvent(module));
        grid.Properties.Should().ContainSingle();
        grid.Properties[0].Value = "original";
        module.Name.Should().Be("original");
        windows.DidNotReceiveWithAnyArgs().MessageBox(default!);
    }

    [AvaloniaTheory]
    [InlineData(ModuleKind.UserControl)]
    [InlineData(ModuleKind.PropertyPage)]
    public void Designer_root_rename_and_undo_keep_the_module_name_in_step(ModuleKind kind)
    {
        var module = Module(kind);
        dock.OpenDocuments.Returns(new List<HexIDE.Forms.ViewModels.BaseEditorWindowViewModel>());
        var designer = new FormEditViewModel(null!, bus, Substitute.For<IProjectService>(),
            Substitute.For<IEditorService>(), windows, Substitute.For<ISettingsService>(), loc).Initialize(module.FormPart!);
        var grid = Grid();
        dock.ActiveDocument.Returns(designer);
        dock.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(dock, new PropertyChangedEventArgs(nameof(dock.ActiveDocument)));
        grid.UpdateValue(VBProperties.NameProperty, "Renamed");
        module.Name.Should().Be("Renamed");
        module.FormPart!.Name.Should().Be("Renamed");
        module.Code.Should().Contain("Attribute VB_Name = \"Renamed\"");
        designer.UndoStack.Undo();
        module.Name.Should().Be("Original");
        module.FormPart.Name.Should().Be("Original");
        module.Code.Should().Contain("Attribute VB_Name = \"Original\"");
        bus.Publish(new ModulePropertiesSelectedEvent(module));
        grid.Properties.Should().ContainSingle();
        bus.Publish(new ModulePropertiesSelectedEvent(null));
        grid.Properties.Count.Should().BeGreaterThan(1);
    }
}
