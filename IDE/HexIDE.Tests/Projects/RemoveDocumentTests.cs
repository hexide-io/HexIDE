using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

public class RemoveDocumentTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-remove-" + Guid.NewGuid().ToString("N"));
    private readonly IWindowManager windows = Substitute.For<IWindowManager>();
    private readonly IEventBus events = Substitute.For<IEventBus>();
    private readonly List<ProjectDefinition> loaded = new();
    private readonly ProjectService service;
    private SaveChangesViewModel? prompt;
    private string answer = "No";

    public RemoveDocumentTests()
    {
        Directory.CreateDirectory(dir);
        var manager = Substitute.For<IProjectManager>();
        manager.LoadedProjects.Returns(_ => loaded);
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns(ci =>
            Task.FromResult<string?>(Path.Join(dir, ci.Arg<FilePickerSaveOptions>().SuggestedFileName)));
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(ci =>
        {
            if (ci.Arg<IDialog>() is SaveChangesViewModel vm)
            {
                prompt = vm;
                if (answer == "Cancel") return Task.FromResult(false);
                if (answer == "Yes") vm.Yes(); else vm.No();
            }
            return Task.FromResult(true);
        });
        service = new ProjectService(() => throw new InvalidOperationException(), windows, events, manager,
            Substitute.For<IRecentProjectsService>(), Substitute.For<IReferenceLibraryService>(),
            Substitute.For<IUserSidecarService>(), new FileBaselineStore(),
            Substitute.For<HexIDE.Localization.ILocalizationService>(), Substitute.For<IHeaderRefresher>());
    }

    public void Dispose()
    {
        Directory.Delete(dir, true);
        GC.SuppressFinalize(this);
    }

    private async Task<DocumentIdentity> Add(string kind)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1")
        { AbsolutePath = Path.Join(dir, "Project1.vbp") };
        loaded.Add(project);
        return kind switch
        {
            "Form" => DocumentIdentity.For(await service.AddNewForm(project, "Form1", false)),
            "UserControl" => DocumentIdentity.For(await service.AddNewUserControl(project, "UserControl1", false)),
            "PropertyPage" => DocumentIdentity.For(await service.AddNewPropertyPage(project, "PropertyPage1", false)),
            _ => DocumentIdentity.For(await service.AddNewModule(project, kind + "1",
                kind == "Class" ? ModuleKind.ClassModule : ModuleKind.StandardModule, false))
        };
    }

    [AvaloniaTheory]
    [InlineData("UserControl")]
    [InlineData("PropertyPage")]
    public async Task Removal_closes_the_modules_designer(string kind)
    {
        var bus = new EventBus();
        var document = await Add(kind);
        var toolbox = new HexIDE.VisualDesigner.ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(),
            Substitute.For<IComponentRegistry>(),
            Substitute.For<HexIDE.Localization.ILocalizationService>());
        using var designer = new HexIDE.VisualDesigner.FormEditViewModel(toolbox, bus, service, null!, null!,
            Substitute.For<ISettingsService>(), Substitute.For<HexIDE.Localization.ILocalizationService>())
            .Initialize(document.Module!.FormPart!);
        var closes = 0;
        designer.CloseRequest += _ => closes++;

        bus.Publish(new ModuleUnloadedEvent(new ModuleDefinition(document.Project, "Other", ModuleKind.StandardModule)));
        closes.Should().Be(0);
        bus.Publish(new ModuleUnloadedEvent(document.Module));
        closes.Should().Be(1);
    }

    [Theory]
    [InlineData("Form")]
    [InlineData("Module")]
    [InlineData("Class")]
    [InlineData("UserControl")]
    [InlineData("PropertyPage")]
    public async Task Removal_keeps_the_file_but_drops_the_tree_member_and_saved_project_entry(string kind)
    {
        var document = await Add(kind);
        await service.SaveProject(document.Project, false);
        var path = document.AbsolutePath!;
        var bytes = File.ReadAllBytes(path);
        var explorer = new HexIDE.Tools.ProjectToolViewModel(Substitute.For<IProjectManager>(), events, service,
            Substitute.For<IEditorService>(), Substitute.For<HexIDE.Localization.ILocalizationService>());
        using var tree = new HexIDE.Tools.ProjectViewModel(explorer, document.Project);

        await service.RemoveDocument(document);

        DocumentLookup.DocumentsOf(document.Project).Should().NotContain(document);
        if (document.Form is { } form)
            events.Received(1).Publish(Arg.Is<FormUnloadedEvent>(e => e.Form == form));
        else
            events.Received(1).Publish(Arg.Is<ModuleUnloadedEvent>(e => e.Module == document.Module));
        tree.Elements.OfType<HexIDE.Tools.IProjectFileNode>().Should().BeEmpty();
        File.ReadAllBytes(path).Should().Equal(bytes);
        prompt.Should().BeNull("an unchanged saved file needs no save prompt");
        await service.UnloadProject(document.Project);
        prompt.Should().NotBeNull("removing a saved member changes the project");
        prompt!.ChangedFiles.Should().Contain(f => f.Project == document.Project);
        await service.SaveProject(document.Project, false);
        File.ReadAllText(document.Project.AbsolutePath!).Should().NotContain(Path.GetFileName(path));
    }

    [Theory]
    [InlineData("Form", "No")]
    [InlineData("Module", "No")]
    [InlineData("Class", "No")]
    [InlineData("UserControl", "No")]
    [InlineData("PropertyPage", "No")]
    [InlineData("Form", "Yes")]
    [InlineData("Module", "Yes")]
    [InlineData("Class", "Yes")]
    [InlineData("UserControl", "Yes")]
    [InlineData("PropertyPage", "Yes")]
    public async Task A_pathless_member_is_offered_for_save_before_removal(string kind, string choice)
    {
        var document = await Add(kind);
        answer = choice;

        await service.RemoveDocument(document);

        prompt.Should().NotBeNull();
        prompt!.ChangedFiles.Should().ContainSingle(f => f.Form == document.Form && f.Module == document.Module);
        DocumentLookup.DocumentsOf(document.Project).Should().NotContain(document);
        Directory.GetFiles(dir).Should().HaveCount(choice == "Yes" ? 1 : 0);
    }

    [Theory]
    [InlineData("Form", "Yes")]
    [InlineData("Module", "Yes")]
    [InlineData("Form", "No")]
    [InlineData("Module", "No")]
    public async Task Removal_flushes_buffers_and_saves_only_when_requested(string kind, string choice)
    {
        var document = await Add(kind);
        await service.SaveProject(document.Project, false);
        var path = document.AbsolutePath!;
        var original = File.ReadAllBytes(path);
        events.When(e => e.Publish(Arg.Any<ApplyAllUnsavedChangesEvent>())).Do(_ =>
        {
            if (document.Form is { } form) form.UpdateCode("' changed");
            else document.Module!.UpdateCode("' changed");
        });
        answer = choice;

        await service.RemoveDocument(document);

        prompt.Should().NotBeNull();
        DocumentLookup.DocumentsOf(document.Project).Should().NotContain(document);
        if (choice == "Yes") File.ReadAllText(path).Should().Contain("' changed");
        else File.ReadAllBytes(path).Should().Equal(original);
    }

    [Fact]
    public async Task A_refused_save_preserves_membership_and_editors()
    {
        var document = await Add("Form");
        document.Form!.MarkUnfaithfulToSave(UnfaithfulSaveCause.NestedContainers, "fixture");
        answer = "Yes";

        try { await service.RemoveDocument(document); }
        catch (OperationCanceledException) { }

        prompt.Should().NotBeNull();
        DocumentLookup.DocumentsOf(document.Project).Should().Contain(document);
        events.DidNotReceive().Publish(Arg.Any<FormUnloadedEvent>());
        Directory.GetFiles(dir).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Form")]
    [InlineData("Module")]
    public async Task Cancellation_preserves_membership(string kind)
    {
        var document = await Add(kind);
        answer = "Cancel";

        var remove = () => service.RemoveDocument(document);

        await remove.Should().ThrowAsync<OperationCanceledException>();
        DocumentLookup.DocumentsOf(document.Project).Should().Contain(document);
        events.DidNotReceive().Publish(Arg.Any<FormUnloadedEvent>());
    }

    [Theory]
    [InlineData("Form")]
    [InlineData("Module")]
    public async Task Cancelled_save_picker_preserves_membership(string kind)
    {
        var document = await Add(kind);
        answer = "Yes";
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns(Task.FromResult<string?>(null));

        try { await service.RemoveDocument(document); }
        catch (OperationCanceledException) { }

        prompt.Should().NotBeNull();
        DocumentLookup.DocumentsOf(document.Project).Should().Contain(document);
        Directory.GetFiles(dir).Should().BeEmpty();
    }
}
