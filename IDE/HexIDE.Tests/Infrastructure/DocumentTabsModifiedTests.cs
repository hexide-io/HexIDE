using System.Text.Json;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using HexIDE.Desktop.Server;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// get_document_tabs and get_open_editors say whether an editor has unsaved changes, read from the state its tab's
/// `*` marker draws from (hexide-io/HexIDE#1945). The title leaves the marker out, so before this a client could not
/// tell a modified document from a saved one without a screenshot.
/// </summary>
public class DocumentTabsModifiedTests
{
    private sealed class Fixture : IDisposable
    {
        public readonly ProjectDefinition Project = TestHelpers.CreateProjectWithForm("Project1");
        public readonly IProjectService Service = Substitute.For<IProjectService>();
        public readonly EventBus Bus = new();
        public readonly IDocumentDockService Dock = Substitute.For<IDocumentDockService>();
        public readonly IdeContext Context;
        public readonly HexIdeTools Tools;

        public Fixture()
        {
            var projects = Substitute.For<IProjectManager>();
            projects.LoadedProjects.Returns([Project]);
            Context = new IdeContext(projects, Dock, Substitute.For<ILspClient>(), null!, Substitute.For<IProjectRunnerService>(),
                null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
            Tools = new HexIdeTools(Context);
        }

        public FormDefinition Form => Project.Forms[0];

        public FormEditViewModel Designer() => new FormEditViewModel(null!, Bus, Service, null!, null!,
            Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(Form);

        public HexIDE.Forms.ViewModels.CodeEditorViewModel Code()
        {
            var events = Substitute.For<IEventBus>();
            events.Subscribe<CreateOrNavigateToSubEvent>(Arg.Any<Action<CreateOrNavigateToSubEvent>>())
                .Returns(Substitute.For<IDisposable>());
            return new HexIDE.Forms.ViewModels.CodeEditorViewModel(
                Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(), Service,
                events, Substitute.For<ILspClient>(), Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(),
                Substitute.For<HexIDE.Bookmarks.IBookmarkService>(), Substitute.For<HexIDE.Debugging.IBreakpointService>(),
                Substitute.For<IDebugController>(), Substitute.For<HexIDE.Debugging.IRunScope>(),
                Substitute.For<ILocalizationService>()).Initialize(Form);
        }

        public void Tabs(params IDockable[] tabs)
        {
            Dock.AllTabs.Returns(tabs);
            Dock.OpenDocuments.Returns(tabs.OfType<HexIDE.Forms.ViewModels.BaseEditorWindowViewModel>().ToArray());
            Dock.ActiveTab.Returns(tabs.LastOrDefault());
            Dock.ActiveDocument.Returns(tabs.OfType<HexIDE.Forms.ViewModels.BaseEditorWindowViewModel>().LastOrDefault());
        }

        public void Dispose() => Context.Dispose();
    }

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, ReplyJson.Options);

    private static async Task<JsonElement> TabsAsync(Fixture f) =>
        Json(await f.Tools.GetDocumentTabsAsync(TestContext.Current.CancellationToken)).GetProperty("tabs");

    [AvaloniaFact]
    public async Task Each_editor_tab_says_whether_it_has_unsaved_changes_and_a_tool_tab_says_nothing()
    {
        using var f = new Fixture();
        f.Service.HasUnsavedChanges(f.Form).Returns(true);
        using var designer = f.Designer();
        f.Service.HasUnsavedChanges(f.Form).Returns(false);
        using var code = f.Code();
        f.Tabs(designer, code, new Document { Title = "Object Browser" });

        var tabs = await TabsAsync(f);

        tabs[0].GetProperty("isModified").GetBoolean().Should().BeTrue("the designer's form has unsaved changes");
        tabs[0].GetProperty("title").GetString().Should().NotContain("*",
            "the title names the tab, and activate_document_tab matches it; the state is its own field");
        tabs[1].GetProperty("isModified").GetBoolean().Should().BeFalse("nothing has been typed into the code window");
        tabs[2].TryGetProperty("isModified", out _).Should().BeFalse(
            "a tool tab holds nothing to save, and a false would read as 'saved'");
    }

    [AvaloniaFact]
    public async Task The_answer_follows_an_edit_and_a_save()
    {
        using var f = new Fixture();
        using var designer = f.Designer();
        using var code = f.Code();
        f.Tabs(designer, code);
        (await TabsAsync(f))[1].GetProperty("isModified").GetBoolean().Should().BeFalse();

        code.Document.Insert(code.Document.TextLength, "' typed\r\n");
        (await TabsAsync(f))[1].GetProperty("isModified").GetBoolean().Should().BeTrue("code was just typed");

        f.Service.HasUnsavedChanges(f.Form).Returns(true);
        designer.CommitLayout();
        (await TabsAsync(f))[0].GetProperty("isModified").GetBoolean().Should().BeTrue("the form now has changes");

        f.Service.HasUnsavedChanges(f.Form).Returns(false);
        f.Bus.Publish(new DocumentSavedEvent(f.Form, null));
        (await TabsAsync(f))[0].GetProperty("isModified").GetBoolean().Should().BeFalse("the form was saved");
    }

    [AvaloniaFact]
    public async Task The_editor_listing_says_it_too()
    {
        using var f = new Fixture();
        f.Service.HasUnsavedChanges(f.Form).Returns(true);
        using var designer = f.Designer();
        f.Tabs(designer, new Document { Title = "Object Browser" });

        var editors = Json(await f.Tools.GetOpenEditorsAsync(TestContext.Current.CancellationToken));

        editors.GetProperty("editors")[0].GetProperty("isModified").GetBoolean().Should().BeTrue();
        editors.GetProperty("activeEditor").GetProperty("isModified").GetBoolean().Should().BeTrue();
    }
}
