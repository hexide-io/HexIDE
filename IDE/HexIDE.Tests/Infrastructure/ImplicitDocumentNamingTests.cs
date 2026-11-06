using System.Reflection;
using System.Text.Json;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using HexIDE.Desktop.Server;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.Infrastructure;

public class ImplicitDocumentNamingTests
{
    private static FormEditViewModel Designer(ProjectDefinition project) => new FormEditViewModel(
        null!, Substitute.For<IEventBus>(), null!, null!, null!, Substitute.For<ISettingsService>(),
        Substitute.For<ILocalizationService>()).Initialize(project.Forms[0]);

    private static HexIDE.Forms.ViewModels.CodeEditorViewModel Code(FormDefinition form)
    {
        var events = Substitute.For<IEventBus>();
        events.Subscribe<HexIDE.Events.CreateOrNavigateToSubEvent>(Arg.Any<Action<HexIDE.Events.CreateOrNavigateToSubEvent>>())
            .Returns(Substitute.For<IDisposable>());
        var vm = new HexIDE.Forms.ViewModels.CodeEditorViewModel(
            Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(), Substitute.For<IProjectService>(),
            events, Substitute.For<ILspClient>(), Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(),
            Substitute.For<HexIDE.Bookmarks.IBookmarkService>(), Substitute.For<HexIDE.Debugging.IBreakpointService>(),
            Substitute.For<IDebugController>(), Substitute.For<HexIDE.Debugging.IRunScope>(),
            Substitute.For<ILocalizationService>());
        return vm.Initialize(form);
    }

    private sealed class Fixture : IDisposable
    {
        public readonly ProjectDefinition First = TestHelpers.CreateProjectWithForm("First");
        public readonly ProjectDefinition Second = TestHelpers.CreateProjectWithForm("Second");
        public readonly IDocumentDockService Dock = Substitute.For<IDocumentDockService>();
        public readonly IdeContext Context;
        public readonly HexIdeTools Tools;
        public Fixture()
        {
            var projects = Substitute.For<IProjectManager>();
            projects.LoadedProjects.Returns([First, Second]);
            var runner = Substitute.For<IProjectRunnerService>();
            runner.RunningProject.Returns(Second);
            Context = new IdeContext(projects, Dock, Substitute.For<ILspClient>(), null!, runner,
                null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
            Tools = new HexIdeTools(Context);
        }
        public void Tabs(params IDockable[] tabs)
        {
            Dock.AllTabs.Returns(tabs);
            Dock.OpenDocuments.Returns(tabs.OfType<HexIDE.Forms.ViewModels.BaseEditorWindowViewModel>().ToArray());
            Dock.ActiveTab.Returns(tabs.LastOrDefault());
            Dock.ActiveDocument.Returns(tabs.LastOrDefault() as HexIDE.Forms.ViewModels.BaseEditorWindowViewModel);
        }
        public void Dispose() => Context.Dispose();
    }

    // Bind by parameter name as the tool caller does. This also runs against the old signature for the
    // regression proof: it can then only receive title, and must fail the identity/target assertions.
    private static async Task<MutateResult> TabCall(HexIdeTools tools, bool close, string? title = null,
        string? name = null, string? project = null, string? type = null)
    {
        var method = typeof(HexIdeTools).GetMethod(close ? "CloseDocumentTabAsync" : "ActivateDocumentTabAsync")!;
        var values = new Dictionary<string, object?> { ["title"] = title, ["name"] = name, ["project"] = project,
            ["type"] = type, ["ct"] = TestContext.Current.CancellationToken };
        return await (Task<MutateResult>)method.Invoke(tools, method.GetParameters().Select(p => values[p.Name!]).ToArray())!;
    }

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, ReplyJson.Options);

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_tab_is_targeted_by_its_document_in_the_requested_project(bool close)
    {
        using var f = new Fixture();
        using var first = Designer(f.First);
        using var second = Designer(f.Second);
        f.Tabs(first, second);
        IDockable? target = null;
        if (close)
            f.Dock.TryCloseAny(Arg.Any<Func<IDockable, bool>>()).Returns(call =>
            { target = f.Dock.AllTabs.Single(call.Arg<Func<IDockable, bool>>()); return true; });
        else
            f.Dock.TryActivateAny(Arg.Any<Func<IDockable, bool>>()).Returns(call =>
            { target = f.Dock.AllTabs.Single(call.Arg<Func<IDockable, bool>>()); return true; });

        var result = await TabCall(f.Tools, close, name: "form1", project: "second");

        result.Success.Should().BeTrue();
        target.Should().BeSameAs(second);
        result.Project.Should().Be("Second");
        result.Document.Should().Be("Form1");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_document_with_code_and_designer_tabs_requires_a_type(bool close)
    {
        using var f = new Fixture();
        using var designer = Designer(f.Second);
        using var code = Code(f.Second.Forms[0]);
        f.Tabs(designer, code);
        var refused = await TabCall(f.Tools, close, name: "Form1", project: "Second");
        refused.Error.Should().Contain("More than one tab");
        IDockable? target = null;
        if (close)
            f.Dock.TryCloseAny(Arg.Any<Func<IDockable, bool>>()).Returns(call =>
            { target = f.Dock.AllTabs.Single(call.Arg<Func<IDockable, bool>>()); return true; });
        else
            f.Dock.TryActivateAny(Arg.Any<Func<IDockable, bool>>()).Returns(call =>
            { target = f.Dock.AllTabs.Single(call.Arg<Func<IDockable, bool>>()); return true; });
        var result = await TabCall(f.Tools, close, name: "Form1", project: "Second", type: "code");
        result.Success.Should().BeTrue();
        target.Should().BeSameAs(code);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_ambiguous_document_name_reports_candidates(bool close)
    {
        using var f = new Fixture();
        using var first = Designer(f.First);
        using var second = Designer(f.Second);
        f.Tabs(first, second);
        var result = await TabCall(f.Tools, close, name: "Form1");
        result.Error.Should().Contain("First/Form1").And.Contain("Second/Form1").And.Contain("project");
        f.Dock.DidNotReceive().TryActivateAny(Arg.Any<Func<IDockable, bool>>());
        f.Dock.DidNotReceive().TryCloseAny(Arg.Any<Func<IDockable, bool>>());
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Duplicate_titles_are_refused_without_acting(bool close)
    {
        using var f = new Fixture();
        f.Tabs(new Document { Title = "Same" }, new Document { Title = "Same" });
        var result = await TabCall(f.Tools, close, title: "same");
        result.Error.Should().Contain("More than one tab");
        f.Dock.DidNotReceive().TryActivateAny(Arg.Any<Func<IDockable, bool>>());
        f.Dock.DidNotReceive().TryCloseAny(Arg.Any<Func<IDockable, bool>>());
    }

    [AvaloniaFact]
    public async Task Listings_identify_each_designer_and_the_active_editor()
    {
        using var f = new Fixture();
        using var first = Designer(f.First);
        using var second = Designer(f.Second);
        f.Tabs(first, second);
        var tabs = Json(await f.Tools.GetDocumentTabsAsync(default)).GetProperty("tabs");
        tabs[0].GetProperty("project").GetString().Should().Be("First");
        tabs[1].GetProperty("project").GetString().Should().Be("Second");
        tabs[1].GetProperty("document").GetString().Should().Be("Form1");
        var editors = Json(await f.Tools.GetOpenEditorsAsync(default));
        editors.GetProperty("editors")[0].GetProperty("project").GetString().Should().Be("First");
        editors.GetProperty("activeEditor").GetProperty("project").GetString().Should().Be("Second");
    }

    [AvaloniaTheory]
    [InlineData("undo")]
    [InlineData("redo")]
    [InlineData("format")]
    [InlineData("unknown-format")]
    [InlineData("state")]
    public async Task Active_designer_replies_identify_the_form_even_when_nothing_changes(string action)
    {
        using var f = new Fixture();
        using var designer = Designer(f.Second);
        f.Tabs(designer);
        object result = action switch
        {
            "undo" => await f.Tools.InvokeDesignerUndoAsync(default),
            "redo" => await f.Tools.InvokeDesignerRedoAsync(default),
            "format" => await f.Tools.InvokeFormatCommandAsync("AlignLefts", default),
            "unknown-format" => await f.Tools.InvokeFormatCommandAsync("unknown", default),
            _ => await f.Tools.GetUndoStateAsync(default),
        };
        var json = Json(result);
        json.GetProperty("project").GetString().Should().Be("Second");
        json.GetProperty("document").GetString().Should().Be("Form1");
    }

    [Theory]
    [InlineData("ActivateDocumentTabAsync")]
    [InlineData("CloseDocumentTabAsync")]
    public void The_generated_schema_allows_document_addressing_without_a_title(string method)
    {
        using var f = new Fixture();
        var tool = ModelContextProtocol.Server.McpServerTool.Create(typeof(HexIdeTools).GetMethod(method)!, f.Tools);
        var schema = tool.ProtocolTool.InputSchema;
        if (schema.TryGetProperty("required", out var required))
            required.EnumerateArray().Select(p => p.GetString()).Should().NotContain("title");
        var properties = schema.GetProperty("properties");
        properties.TryGetProperty("name", out _).Should().BeTrue();
        properties.TryGetProperty("project", out _).Should().BeTrue();
    }

    [Fact]
    public void A_paused_location_names_the_running_project()
    {
        using var f = new Fixture();
        var method = typeof(HexIdeTools).GetMethod("PausedAt", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)!;
        method.Invoke(f.Tools, [new StoppedInfo(StopReason.Step, "Form1", 9)]).Should()
            .Be("Paused at Second/Form1 line 9 (Step).");
    }
}
