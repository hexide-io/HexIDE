using Avalonia.Input;
using Dock.Model.Mvvm.Controls;
using HexIDE.Forms.ViewModels;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.TwinBasic;
using HexIDE.Projects;
using HexIDE.Localization;

namespace HexIDE.Tests.IDE;

public class RecentlyClosedDocumentsTests
{
    private static (DISetup composition, IDocumentDockService dock, IEditorService editors, MainViewViewModel root) Shell()
    {
        var composition = new DISetup();
        var root = composition.Root;
        return (composition, composition.DocumentDockService, composition.EditorService, root);
    }

    [Fact]
    public void ClosedToolsReopenNewestFirstAndHistoryIsSessionOnly()
    {
        using var history = new RecentlyClosedDocuments();
        var first = new Document { Title = "First" };
        var second = new Document { Title = "Second" };
        history.Record(first);
        history.Record(second);
        var reopened = new List<Dock.Model.Core.IDockable>();
        history.Reopen(Substitute.For<IEditorService>(), reopened.Add);
        history.Reopen(Substitute.For<IEditorService>(), reopened.Add);
        reopened.Should().Equal(second, first);
        history.Entries.Should().BeEmpty();
        history.Record(first);
        history.Record(second);
        history.Reopen(Substitute.For<IEditorService>(), reopened.Add, history.Entries[1]);
        reopened.Last().Should().BeSameAs(first);
        history.Entries.Should().ContainSingle().Which.Identity.Should().BeSameAs(second);
        using var nextSession = new RecentlyClosedDocuments();
        nextSession.Entries.Should().BeEmpty();
    }

    [Fact]
    public void OnlyTwentyMostRecentlyClosedTabsAreKept()
    {
        using var history = new RecentlyClosedDocuments();
        for (var i = 0; i < 21; i++) history.Record(new Document { Title = i.ToString() });
        history.Entries.Select(e => e.Title).Should().Equal(Enumerable.Range(1, 20).Reverse().Select(i => i.ToString()));
    }

    [AvaloniaFact]
    public void CodeReopensWithCaretAndDesignerReopensAsDesigner()
    {
        var (_, dock, editors, _) = Shell();
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");
        project.AddForm(form);
        form.UpdateCode("Option Explicit" + Environment.NewLine);
        editors.EditCode(form);
        var code = dock.OpenDocuments.OfType<CodeEditorViewModel>().Single();
        code.CaretOffset = 12;
        dock.CloseDocument(code);
        dock.ReopenClosedDocument(editors);
        var reopened = dock.OpenDocuments.OfType<CodeEditorViewModel>().Single();
        reopened.Should().NotBeSameAs(code);
        reopened.CaretOffset.Should().Be(12);
        dock.CloseDocument(reopened);
        editors.EditForm(form);
        dock.CloseDocument(dock.OpenDocuments.OfType<HexIDE.VisualDesigner.FormEditViewModel>().Single());
        dock.ReopenClosedDocument(editors);
        dock.ActiveDocument.Should().BeOfType<HexIDE.VisualDesigner.FormEditViewModel>();
        dock.CloseAll();
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "carried text buffer");
            var related = new RelatedDocumentDefinition(project, "Notes", path);
            project.AddRelatedDocument(related);
            editors.EditRelatedDocument(related);
            dock.OpenDocuments.OfType<RelatedDocumentEditorViewModel>().Single().CaretOffset = 7;
            dock.CloseAll();
            dock.ReopenClosedDocument(editors);
            dock.OpenDocuments.OfType<RelatedDocumentEditorViewModel>().Single().CaretOffset.Should().Be(7);
            dock.CloseAll();
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public void RemovingDocumentPrunesItsCodeAndDesignerImmediately()
    {
        var (_, dock, editors, _) = Shell();
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");
        project.AddForm(form);
        form.UpdateCode("Option Explicit" + Environment.NewLine);
        editors.EditCode(form);
        editors.EditForm(form);
        dock.CloseAll();
        dock.RecentlyClosed.Entries.Should().HaveCount(2);
        project.DeleteForm(form);
        dock.RecentlyClosed.Entries.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovingModuleOrCarriedFilePrunesHistoryImmediately(bool carried)
    {
        var (_, dock, editors, _) = Shell();
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        var related = new RelatedDocumentDefinition(project, "Notes");
        project.AddModule(module);
        project.AddRelatedDocument(related);
        if (carried) editors.EditRelatedDocument(related);
        else editors.EditCode(module);
        dock.CloseAll();
        dock.RecentlyClosed.Entries.Should().ContainSingle();
        if (carried) project.DeleteRelatedDocument(related);
        else project.DeleteModule(module);
        dock.RecentlyClosed.Entries.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void UnloadingProjectPrunesItsHistoryAndDoesNotRecordItsClosingTabs()
    {
        var (composition, dock, editors, root) = Shell();
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        project.AddModule(module);
        editors.EditCode(module);
        dock.CloseAll();
        dock.RecentlyClosed.Entries.Should().HaveCount(1);
        editors.EditCode(module);
        var sibling = new ProjectDefinition(VBProjectType.EXE, "Sibling");
        var siblingModule = new ModuleDefinition(sibling, "SiblingModule", ModuleKind.StandardModule);
        sibling.AddModule(siblingModule);
        editors.EditCode(siblingModule);
        composition.EventBus.Publish(new HexIDE.Events.ProjectUnloadedEvent(project));
        dock.RecentlyClosed.Entries.Should().BeEmpty();
        dock.OpenDocuments.Should().ContainSingle().Which.OpenDocument.Should().Be(DocumentIdentity.For(siblingModule));
        dock.CloseAll();
    }

    [AvaloniaFact]
    public async Task ShellCommandAndRecentMenuFollowToolTabHistory()
    {
        var (composition, dock, editors, root) = Shell();
        root.ReopenClosedDocumentCommand.CanExecute(null).Should().BeFalse();
        root.OpenLanguageServers();
        dock.TryCloseAny(t => ReferenceEquals(t, root.LanguageServers)).Should().BeTrue();
        root.ReopenClosedDocumentCommand.CanExecute(null).Should().BeTrue();
        root.ClosedDocumentMenuItems.Should().ContainSingle();
        root.ClosedDocumentMenuItems[0].Command.Execute(null);
        dock.AllTabs.Should().Contain(root.LanguageServers);
        root.ReopenClosedDocumentCommand.CanExecute(null).Should().BeFalse();
        root.ClosedDocumentMenuItems.Should().BeEmpty();
        await CheckArchiveHistory(dock, editors);
    }

    private static string Fixture(string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.Parent is not null && !Directory.Exists(Path.Combine(root.FullName, "openspec"))) root = root.Parent;
        return Path.Combine(root.FullName, "IDE", "HexIDE.TwinBasic.Tests", "Fixtures", name);
    }

    private static IEnumerable<ArchiveProjectDocument> Documents(ArchiveProjectFolder folder) =>
        folder.Children.SelectMany(entry => entry is ArchiveProjectFolder child
            ? Documents(child) : new[] { (ArchiveProjectDocument)entry });

    private static async Task CheckArchiveHistory(IDocumentDockService dock, IEditorService editors)
    {
        var path = Path.Combine(Path.GetTempPath(), "hexide-history-" + Guid.NewGuid().ToString("N") + ".twinproj");
        var loc = Substitute.For<ILocalizationService>();
        loc.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        var archives = new ArchiveProjectService(dock, Substitute.For<IRecentProjectsService>(), loc,
            Substitute.For<IWindowManager>());
        try
        {
            File.Copy(Fixture("sample.twinpack"), path);
            await archives.Open(path);
            using var input = File.OpenRead(Fixture("impexp.twinproj"));
            var replacement = ArchiveProjectSnapshot.Read(input, path);
            var source = Documents(archives.Projects.Single().Root).First(doc =>
                doc.ReadText().Text is { Length: > 12 } && !replacement.TryGetEntry(doc.Identity, out _));
            archives.OpenDocument(source);
            var original = dock.OpenDocuments.OfType<ArchiveDocumentViewModel>().Single();
            original.CaretOffset = 12;
            dock.CloseDocument(original);
            dock.ReopenClosedDocument(editors);
            var reopened = dock.OpenDocuments.OfType<ArchiveDocumentViewModel>().Single();
            reopened.Should().NotBeSameAs(original);
            reopened.CaretOffset.Should().Be(12);
            dock.CloseDocument(reopened);
            File.Copy(Fixture("impexp.twinproj"), path, overwrite: true);
            await archives.Reload(path);
            dock.RecentlyClosed.Entries.Should().BeEmpty();
            archives.OpenDocument(Documents(archives.Projects.Single().Root).First());
            dock.CloseAll();
            dock.RecentlyClosed.Entries.Should().ContainSingle();
            await archives.Close(path);
            dock.RecentlyClosed.Entries.Should().BeEmpty();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ShortcutUsesPlatformCommandModifierAndIsRebindable()
    {
        var command = ApplicationCommands.ReopenClosedDocumentCommand;
        var gesture = command.Gestures.Single();
        gesture.Key.Should().Be(Key.T);
        gesture.KeyModifiers.Should().Be(ApplicationCommands.NewProjectCommand.Gestures.Single().KeyModifiers | KeyModifiers.Shift);
        HexIDE.Keymaps.CommandKeyMapping.Table["ReopenClosedDocumentCommand"]
            .Should().BeSameAs(command);
    }
}
