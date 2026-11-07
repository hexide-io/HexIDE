using System.Text.Json;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using HexIDE.Desktop.Server;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// get_document_tabs and get_open_editors say whether a tab has unsaved changes, read from the state its `*` marker
/// draws from (hexide-io/HexIDE#1945). The title leaves the marker out, so before this a client could not tell a
/// modified document from a saved one without a screenshot.
/// </summary>
public sealed class DocumentTabsModifiedTests : IDisposable
{
    private readonly ProjectDefinition project = TestHelpers.CreateProjectWithForm("Project1");
    private readonly IProjectService service = Substitute.For<IProjectService>();
    private readonly EventBus bus = new();
    private readonly IDocumentDockService dock = Substitute.For<IDocumentDockService>();
    private readonly IdeContext context;
    private readonly HexIdeTools tools;
    private readonly List<IDisposable> created = [];
    private readonly string dir = Path.Combine(Path.GetTempPath(), "hexide-tabs-" + Guid.NewGuid().ToString("N"));

    public DocumentTabsModifiedTests()
    {
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([project]);
        context = new IdeContext(projects, dock, Substitute.For<ILspClient>(), null!, Substitute.For<IProjectRunnerService>(),
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
        tools = new HexIdeTools(context);
    }

    public void Dispose()
    {
        foreach (var vm in created) vm.Dispose();
        context.Dispose();
        try { Directory.Delete(dir, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    private FormDefinition Form => project.Forms[0];

    private T Made<T>(T vm) where T : IDisposable
    {
        created.Add(vm);
        return vm;
    }

    private FormEditViewModel Designer() => Made(new FormEditViewModel(null!, bus, service, null!, null!,
        Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(Form));

    private CodeEditorViewModel Code() => Made(new CodeEditorViewModel(
        Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(), service,
        bus, Substitute.For<ILspClient>(), Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(),
        Substitute.For<HexIDE.Bookmarks.IBookmarkService>(), Substitute.For<HexIDE.Debugging.IBreakpointService>(),
        Substitute.For<IDebugController>(), Substitute.For<HexIDE.Debugging.IRunScope>(),
        Substitute.For<ILocalizationService>()).Initialize(Form));

    private RelatedDocumentEditorViewModel Carried(string content)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "notes.txt");
        File.WriteAllText(path, content);
        return Made(new RelatedDocumentEditorViewModel(Substitute.For<ILspClient>(), Substitute.For<IWindowManager>(),
            Substitute.For<ILocalizationService>())
            .Initialize(new RelatedDocumentDefinition(project, "notes.txt", path)));
    }

    private ProjectDocumentViewModel ProjectView() =>
        Made(new ProjectDocumentViewModel(Substitute.For<ILocalizationService>()).Initialize(project));

    /// <summary>The tabs as the dock service gives them, the last one in front.</summary>
    private void Tabs(params IDockable[] tabs)
    {
        dock.AllTabs.Returns(tabs);
        dock.OpenDocuments.Returns(tabs.OfType<BaseEditorWindowViewModel>().ToArray());
        dock.ActiveTab.Returns(tabs.LastOrDefault());
        dock.ActiveDocument.Returns(tabs.LastOrDefault() as BaseEditorWindowViewModel);
    }

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, ReplyJson.Options);

    private async Task<JsonElement> TabsAsync() =>
        Json(await tools.GetDocumentTabsAsync(TestContext.Current.CancellationToken)).GetProperty("tabs");

    private static bool? Modified(JsonElement tab) =>
        tab.TryGetProperty("isModified", out var value) ? value.GetBoolean() : null;

    [AvaloniaFact]
    public async Task Each_tab_that_can_be_saved_says_whether_it_has_unsaved_changes()
    {
        service.HasUnsavedChanges(Form).Returns(true);
        var designer = Designer();
        service.HasUnsavedChanges(Form).Returns(false);
        var code = Code();
        var carried = Carried("original");
        carried.Document.Text = "edited";
        Tabs(designer, code, carried);

        var tabs = await TabsAsync();

        Modified(tabs[0]).Should().BeTrue("the designer's form has unsaved changes");
        tabs[0].GetProperty("title").GetString().Should().NotContain("*",
            "the title names the tab, and activate_document_tab matches it; the state is its own field");
        Modified(tabs[1]).Should().BeFalse("nothing has been typed into the code window");
        Modified(tabs[2]).Should().BeTrue("the carried file was edited");
        tabs[2].TryGetProperty("document", out _).Should().BeFalse(
            "a carried file is no form or module, and has isModified without document, as the description says");
    }

    [AvaloniaFact]
    public async Task A_tab_with_nothing_to_save_says_nothing()
    {
        // The project view renders the project, which here has never been saved and so counts as unsaved; its tab
        // still draws no `*`, as there is nothing in it to save. A false would read as "the project is saved".
        Tabs(new Document { Title = "Object Browser" }, ProjectView());

        var tabs = await TabsAsync();

        tabs[0].TryGetProperty("isModified", out _).Should().BeFalse("a tool tab holds nothing to save");
        tabs[1].GetProperty("type").GetString().Should().Be("code");
        tabs[1].TryGetProperty("isModified", out _).Should().BeFalse("the project view is read-only");
    }

    [AvaloniaFact]
    public async Task The_answer_follows_an_edit_and_a_save()
    {
        var designer = Designer();
        var code = Code();
        var carried = Carried("original");
        Tabs(designer, code, carried);
        (await TabsAsync()).EnumerateArray().Select(Modified).Should().Equal(false, false, false);

        code.Document.Insert(code.Document.TextLength, "' typed\r\n");
        service.HasUnsavedChanges(Form).Returns(true);
        designer.CommitLayout();
        carried.Document.Text = "edited";
        (await TabsAsync()).EnumerateArray().Select(Modified).Should().Equal(true, true, true);

        // A save flushes every editor into the model, writes, and announces the document saved.
        bus.Publish(new ApplyAllUnsavedChangesEvent());
        service.HasUnsavedChanges(Form).Returns(false);
        bus.Publish(new DocumentSavedEvent(Form, null));
        await carried.SaveAsync();
        (await TabsAsync()).EnumerateArray().Select(Modified).Should().Equal(false, false, false);
    }

    [AvaloniaFact]
    public async Task The_editor_listing_says_it_too()
    {
        service.HasUnsavedChanges(Form).Returns(true);
        Tabs(new Document { Title = "Object Browser" }, Designer());

        var editors = Json(await tools.GetOpenEditorsAsync(TestContext.Current.CancellationToken));

        editors.GetProperty("editors")[0].GetProperty("isModified").GetBoolean().Should().BeTrue();
        editors.GetProperty("activeEditor").GetProperty("isModified").GetBoolean().Should().BeTrue();
    }
}
