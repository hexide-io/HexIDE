using System.IO;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;
using HexIDE.Tests.LspClient;
using Microsoft.Extensions.Logging;

namespace HexIDE.Tests.ViewModels;

/// <summary>
/// A member's rename rewrites its own attribute qualifiers, whatever made the rename (hexide-io/HexIDE#273 task 4.4;
/// design.md, "Renames (4.4)").
/// </summary>
/// <remarks>
/// <para>
/// <b>VB6 does this at every save</b>, so HexIDE does it there too: the save writes each procedure's run, and the run
/// a name retyped on a one-name variable line leaves, qualified with the member's name as declared, and the open code
/// window follows as one of the IDE's own writes. A rename by a server that knows attribute lines, by one that does
/// not, or by retyping the name all end with the file VB6 would have written.
/// </para>
/// <para>
/// Real files, opened through the project service so the dirty check has its baselines, real code windows on the
/// same bus, and saves through the project service, as <see cref="CodeWindowHoldsTheFileTests"/> drives them. The
/// classes are VB6's own, from the oracle guest's IDE sessions: each input is a file VB6 wrote, and each expected
/// file is the one VB6 saved after the same edit (oracle, "Member attributes": IDE probes C2, C3, V1, V2 and B).
/// </para>
/// </remarks>
public sealed class MemberAttributesFollowARenameTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-requalify-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> loaded = [];
    private readonly List<CodeEditorViewModel> open = [];
    private readonly List<CodeEditorViewModel> made = [];
    private readonly EventBus bus = new();
    private readonly IDocumentDockService dock = Substitute.For<IDocumentDockService>();
    private readonly HeaderRefresher refresher;

    public MemberAttributesFollowARenameTests()
    {
        Directory.CreateDirectory(dir);
        dock.OpenDocuments.Returns(_ => open.Cast<BaseEditorWindowViewModel>().ToList());
        refresher = new HeaderRefresher(bus, dock, new BreakpointService(), new BookmarkService());
    }

    public void Dispose()
    {
        foreach (var vm in made) vm.Dispose();
        refresher.Dispose();
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }

    private ProjectService MakeService()
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(_ => loaded);
        projectManager.When(m => m.AddProject(Arg.Any<ProjectDefinition>()))
                      .Do(ci => loaded.Add(ci.Arg<ProjectDefinition>()));

        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);

        return new ProjectService(
            () => throw new InvalidOperationException("new-project dialog must not be reached"),
            Substitute.For<IWindowManager>(),
            bus,
            projectManager,
            Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(),
            sidecar,
            new FileBaselineStore(),
            Substitute.For<ILocalizationService>(),
            refresher);
    }

    /// <summary>A code window on the document, on the same bus as the save, and open as far as the dock knows.</summary>
    private CodeEditorViewModel Open(DocumentIdentity document)
    {
        var lsp = Substitute.For<ILspClient>();
        lsp.IsRunning.Returns(false);
        var vm = new CodeEditorViewModel(
            Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(), bus, lsp,
            Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(),
            Substitute.For<IBookmarkService>(), Substitute.For<IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            Substitute.For<IRunScope>(), Substitute.For<ILocalizationService>());
        made.Add(vm);
        open.Add(vm);
        return document.Module is { } module ? vm.Initialize(module) : vm.Initialize(document.Form!);
    }

    private static string Decoded(string path) => Vb6TextFile.ReadDocument(File.ReadAllBytes(path)).Text;

    /// <summary>A class written to disk and opened in a project of its own, as a project opens it.</summary>
    private async Task<ModuleDefinition> ClassOnDisk(ProjectService service, string name, string text)
    {
        File.WriteAllText(Path.Join(dir, name + ".cls"), text);
        var vbp = Path.Join(dir, "Test.vbp");
        File.WriteAllText(vbp, $"Type=Exe\r\nClass={name}; {name}.cls\r\nName=\"Test\"\r\n");
        await service.OpenProject(vbp);
        return loaded.Single().Modules.Single();
    }

    /// <summary>A form written to disk and opened in a project of its own, as a project opens it.</summary>
    private async Task<FormDefinition> FormOnDisk(ProjectService service, string text)
    {
        File.WriteAllText(Path.Join(dir, "Form1.frm"), text);
        var vbp = Path.Join(dir, "Test.vbp");
        File.WriteAllText(vbp, "Type=Exe\r\nForm=Form1.frm\r\nName=\"Test\"\r\n");
        await service.OpenProject(vbp);
        return loaded.Single().Forms.Single();
    }

    /// <summary>The flush every save starts with, then the dirty check the close prompt makes.</summary>
    private bool HasUnsavedChanges(ProjectService service, ModuleDefinition module)
    {
        bus.Publish(new ApplyAllUnsavedChangesEvent());
        return service.HasUnsavedChanges(module);
    }

    /// <summary>Replaces <paramref name="old"/> in <paramref name="within"/>, the first line holding it, as typing does.</summary>
    private static void Retype(CodeEditorViewModel editor, string within, string old, string @new)
    {
        var at = editor.Document.Text.IndexOf(within, StringComparison.Ordinal) + within.IndexOf(old, StringComparison.Ordinal);
        editor.Document.Replace(at, old.Length, @new);
    }

    /// <summary>An edit replacing <paramref name="length"/> characters at <paramref name="offset"/>, as a server sends it.</summary>
    private static TextEdit EditAt(CodeEditorViewModel editor, int offset, int length, string text)
    {
        var start = editor.Document.GetLocation(offset);
        var end = editor.Document.GetLocation(offset + length);
        return new TextEdit(new HexIDE.Lsp.Messages.Range(
            new Position(start.Line - 1, start.Column - 1), new Position(end.Line - 1, end.Column - 1)), text);
    }

    // ── VB6's files ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The header VB6 writes for every class of these probes.</summary>
    private static string Vb6Class(string name, string code) =>
        "VERSION 1.0 CLASS\r\n" +
        "BEGIN\r\n" +
        "  MultiUse = -1  'True\r\n" +
        "  Persistable = 0  'NotPersistable\r\n" +
        "  DataBindingBehavior = 0  'vbNone\r\n" +
        "  DataSourceBehavior  = 0  'vbNone\r\n" +
        "  MTSTransactionMode  = 0  'NotAnMTSObject\r\n" +
        "END\r\n" +
        $"Attribute VB_Name = \"{name}\"\r\n" +
        "Attribute VB_GlobalNameSpace = False\r\n" +
        "Attribute VB_Creatable = True\r\n" +
        "Attribute VB_PredeclaredId = False\r\n" +
        "Attribute VB_Exposed = False\r\n" +
        code;

    /// <summary>The first session's <c>CalcC&lt;n&gt;</c>, described through Tools > Procedure Attributes.</summary>
    private static string Calc(string name, string declared, string body, string qualifier = "Total") => Vb6Class(name,
        "Option Explicit\r\n" +
        "\r\n" +
        "Private mSum As Long\r\n" +
        "\r\n" +
        $"Public Function {declared}() As Long\r\n" +
        $"Attribute {qualifier}.VB_Description = \"Adds things up.\"\r\n" +
        $"    {body} = mSum\r\n" +
        "End Function\r\n" +
        "\r\n" +
        "Public Sub Other()\r\n" +
        "    Debug.Print 2\r\n" +
        "End Sub\r\n");

    private const string BundledServerRoute = "the bundled server";
    private const string ServerLeavingAttributeLinesAlone = "a server that leaves attribute lines alone";
    private const string Retyping = "retyping the name on the declaration";

    public static IEnumerable<TheoryDataRow<string>> RenameRoutes()
    {
        yield return new TheoryDataRow<string>(BundledServerRoute)
        {
            Skip = BundledServer.StaleReason() ?? (BundledServer.Find() is null
                ? "The bundled VB6 language server has not been built. Run `cd LspServer && dotnet build HexIDE.VbLspServer/`."
                : null),
        };
        yield return new TheoryDataRow<string>(ServerLeavingAttributeLinesAlone);
        yield return new TheoryDataRow<string>(Retyping);
    }

    // ── The scenario ────────────────────────────────────────────────────────────────────────────────────

    [AvaloniaTheory]
    [MemberData(nameof(RenameRoutes))]
    public async Task RenamingAProcedureThatHasADescription(string route)
    {
        // The code-editor delta's scenario, by each kind of rename. The file each route ends with is the one VB6
        // saved after the same rename: C2's for a name retyped on the declaration, which leaves the body's
        // `Total = mSum` as it was, and C3's for a rename of every occurrence, which VB6 made by Replace All.
        var service = MakeService();
        var name = route == Retyping ? "CalcC2" : "CalcC3";
        var input = Calc(name, "Total", "Total");
        var module = await ClassOnDisk(service, name, input);
        var editor = Open(DocumentIdentity.For(module));
        var declaration = editor.Document.Text.IndexOf("Function Total", StringComparison.Ordinal) + "Function ".Length;
        var body = editor.Document.Text.IndexOf("    Total = mSum", StringComparison.Ordinal) + 4;

        switch (route)
        {
            case Retyping:
                Retype(editor, "Public Function Total", "Total", "GrandTotal");
                break;
            case ServerLeavingAttributeLinesAlone:
                editor.ApplyRename([EditAt(editor, declaration, 5, "GrandTotal"), EditAt(editor, body, 5, "GrandTotal")], "Total")
                    .Should().BeNull();
                break;
            default:
                editor.ApplyRename(await BundledServerRenameAsync(editor.Document.Text, editor.Document.GetLocation(declaration),
                    "GrandTotal"), "Total").Should().BeNull("the bundled server's qualifier edit is the procedure's own");
                editor.Document.Text.Should().Contain("Attribute GrandTotal.VB_Description",
                    "a server that knows attribute lines sends the qualifier with the rename, and the window applies it");
                break;
        }

        (await service.SaveModule(module, saveAs: false, interactive: false)).Should().BeTrue();

        var vb6Saved = route == Retyping ? Calc(name, "GrandTotal", "Total", "GrandTotal") : Calc(name, "GrandTotal", "GrandTotal", "GrandTotal");
        Decoded(module.AbsolutePath!).Should().Be(vb6Saved, "the attribute line names the procedure's new name, as VB6 writes it");
        editor.Document.Text.Should().Be(vb6Saved, "and so does the window, which holds the text the save wrote");
        HasUnsavedChanges(service, module).Should().BeFalse("the rewrite is the save's, not an edit");

        editor.UndoRequested(() => editor.Document.UndoStack.Undo());

        editor.Document.Text.Should().Be(input, "one Undo takes back the rename, and the IDE's rewrite of the run with it");
    }

    /// <summary>The bundled server's answer to renaming the name at <paramref name="at"/> in <paramref name="text"/>.</summary>
    private static async Task<IReadOnlyList<TextEdit>> BundledServerRenameAsync(string text, AvaloniaEdit.Document.TextLocation at, string newName)
    {
        var loggerFactory = LoggerFactory.Create(_ => { });
        var transport = new StdioProcessLspTransport(
            new LspServerInfo(BundledServer.Find()!, "", Path.GetTempPath()),
            loggerFactory.CreateLogger<StdioProcessLspTransport>());
        var client = new VBLspClient(transport, loggerFactory.CreateLogger<VBLspClient>(), "vb6");
        try
        {
            await client.StartAsync(TestContext.Current.CancellationToken);
            const string uri = "vb6://module/Requalify";
            await client.OpenDocumentAsync(uri, text, TestContext.Current.CancellationToken);
            var edit = await client.RequestRenameAsync(uri, new Position(at.Line - 1, at.Column - 1), newName,
                TestContext.Current.CancellationToken);
            return edit!.Changes![uri];
        }
        finally
        {
            try { await client.StopAsync(); } catch { /* teardown is best effort */ }
            try { await client.DisposeAsync(); } catch { /* ditto */ }
        }
    }

    // ── Which saves rewrite ─────────────────────────────────────────────────────────────────────────────

    /// <summary>B's hand-written shapes, VB6's input and the file it saved after the marker comment was edited.</summary>
    public static TheoryData<string, string, string> LoadedShapes => new()
    {
        {
            "NamesNothing",
            "Public Function Total() As Long\r\nAttribute Gone.VB_Description = \"Names nothing.\"\r\n    Total = 1\r\nEnd Function\r\n",
            "Public Function Total() As Long\r\nAttribute Total.VB_Description = \"Names nothing.\"\r\n    Total = 1\r\nEnd Function\r\n"
        },
        {
            "NamesOther",
            "Public Function Total() As Long\r\nAttribute Other.VB_Description = \"Names Other.\"\r\n    Total = 1\r\nEnd Function\r\n\r\n" +
            "Public Function Other() As Long\r\n    Other = 2\r\nEnd Function\r\n",
            "Public Function Total() As Long\r\nAttribute Total.VB_Description = \"Names Other.\"\r\n    Total = 1\r\nEnd Function\r\n\r\n" +
            "Public Function Other() As Long\r\n    Other = 2\r\nEnd Function\r\n"
        },
        {
            "LowerCase",
            "Public Function Total() As Long\r\nAttribute total.VB_Description = \"Lower-case qualifier.\"\r\n    Total = 1\r\nEnd Function\r\n",
            "Public Function Total() As Long\r\nAttribute Total.VB_Description = \"Lower-case qualifier.\"\r\n    Total = 1\r\nEnd Function\r\n"
        },
    };

    private static string Shape(string name, string marker, string procedures) =>
        Vb6Class(name, $"Option Explicit\r\n' {marker}\r\n\r\n{procedures}");

    [AvaloniaTheory]
    [MemberData(nameof(LoadedShapes))]
    public async Task ASaveOfAChangedDocumentWritesTheQualifiersVb6Wrote(string name, string loadedRun, string savedRun)
    {
        // B: each module dirtied by editing a marker comment and saved, VB6 rewrote the stale qualifier. The whole
        // expected file is the one VB6 saved.
        var service = MakeService();
        var module = await ClassOnDisk(service, name, Shape(name, "p4 marker", loadedRun));
        var editor = Open(DocumentIdentity.For(module));

        Retype(editor, "' p4 marker", "p4 marker", "p4 marker2");
        (await service.SaveModule(module, saveAs: false, interactive: false)).Should().BeTrue();

        var vb6Saved = Shape(name, "p4 marker2", savedRun);
        Decoded(module.AbsolutePath!).Should().Be(vb6Saved);
        editor.Document.Text.Should().Be(vb6Saved);
        HasUnsavedChanges(service, module).Should().BeFalse();
    }

    [AvaloniaTheory]
    [MemberData(nameof(LoadedShapes))]
    public async Task AnUnchangedSaveLeavesAStaleQualifierAsItWasRead(string name, string loadedRun, string savedRun)
    {
        // Byte for byte. VB6 rewrites the qualifiers of a module it writes, and is taken to write only a module it
        // holds as changed, the premise the oracle's sessions dirtied every module on and did not measure. HexIDE
        // writes every member at Save Project, and only the gate on unsaved changes keeps this one's bytes.
        _ = savedRun;
        var service = MakeService();
        var text = Shape(name, "p4 marker", loadedRun);
        var module = await ClassOnDisk(service, name, text);
        var editor = Open(DocumentIdentity.For(module));
        var bytes = await File.ReadAllBytesAsync(module.AbsolutePath!, TestContext.Current.CancellationToken);

        await service.SaveProject(loaded.Single(), saveAs: false);

        (await File.ReadAllBytesAsync(module.AbsolutePath!, TestContext.Current.CancellationToken)).Should().Equal(bytes);
        editor.Document.Text.Should().Be(text, "and the window, with nothing to follow, is left as it was");
    }

    [AvaloniaFact]
    public async Task ANameRetypedOnALineDeclaringOneVariableCarriesItsDescription()
    {
        // V1: Count retyped Tally. VB6 saved the description re-qualified, and so does HexIDE.
        static string Var1(string declared, string qualifier) => Vb6Class("Var1",
            "Option Explicit\r\n\r\n" +
            $"Public {declared} As Long\r\n" +
            $"Attribute {qualifier}.VB_VarDescription = \"Var1 Count, described.\"\r\n\r\n" +
            "Public Sub Other1()\r\n    Debug.Print 1\r\nEnd Sub\r\n");
        var service = MakeService();
        var module = await ClassOnDisk(service, "Var1", Var1("Count", "Count"));
        var editor = Open(DocumentIdentity.For(module));

        Retype(editor, "Public Count", "Count", "Tally");
        (await service.SaveModule(module, saveAs: false, interactive: false)).Should().BeTrue();

        Decoded(module.AbsolutePath!).Should().Be(Var1("Tally", "Tally"), "VB6's own saved file");
        editor.Document.Text.Should().Be(Var1("Tally", "Tally"));
    }

    [AvaloniaFact]
    public async Task ARunNamingADeclaratorTheLineNoLongerDeclaresIsKeptAsText()
    {
        // V2's Multi2: B retyped C on a line declaring two. VB6 dropped B's description at the save; HexIDE keeps the
        // line, an orphan the developer can see and repair, because it does not rewrite text nobody can tell the
        // meaning of. A deliberate divergence (design.md, "Renames (4.4)").
        static string Multi2(string second, bool described) => Vb6Class("Multi2",
            "Option Explicit\r\n\r\n" +
            $"Public A As Long, {second} As Long\r\n" +
            (described ? "Attribute B.VB_VarDescription = \"Multi2 B, described.\"\r\n" : "") +
            "\r\nPublic Sub Other2()\r\n    Debug.Print 2\r\nEnd Sub\r\n");
        var service = MakeService();
        var module = await ClassOnDisk(service, "Multi2", Multi2("B", described: true));
        var editor = Open(DocumentIdentity.For(module));

        Retype(editor, "Public A As Long, B", "B", "C");
        (await service.SaveModule(module, saveAs: false, interactive: false)).Should().BeTrue();

        Decoded(module.AbsolutePath!).Should().Be(Multi2("C", described: true))
            .And.NotBe(Multi2("C", described: false), "which is what VB6 saved");
    }

    [AvaloniaFact]
    public async Task AClosedDocumentsQualifiersAreRewrittenOnTheModel()
    {
        // A rename can reach a document with no window open: an add-in, or a project-wide replace. The model is what
        // the save writes, so the rewrite is made there, and with no buffer there is nothing else to follow.
        var service = MakeService();
        var module = await ClassOnDisk(service, "CalcC2", Calc("CalcC2", "Total", "Total"));

        module.UpdateCode(module.Code.Replace("Function Total", "Function GrandTotal", StringComparison.Ordinal));
        (await service.SaveModule(module, saveAs: false, interactive: false)).Should().BeTrue();

        Decoded(module.AbsolutePath!).Should().Be(Calc("CalcC2", "GrandTotal", "Total", "GrandTotal"));
        module.Code.Should().Contain("Attribute GrandTotal.VB_Description");
    }

    // ── The window's side: one undo entry, never undone on its own ─────────────────────────────────────

    /// <summary>
    /// A form whose designer block the serializer does not reproduce (four-space indents), so a save re-renders its
    /// header, with a procedure whose run names nothing, as B's <c>NamesNothing</c> does.
    /// </summary>
    private const string StaleForm =
        "VERSION 5.00\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "    Caption         =   \"Orders\"\r\n" +
        "    Begin VB.Label Label1 \r\n" +
        "        Caption         =   \"Hi\"\r\n" +
        "    End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n" +
        "Option Explicit\r\n" +
        "\r\n" +
        "Public Function Total() As Long\r\n" +
        "Attribute Gone.VB_Description = \"Names nothing.\"\r\n" +
        "    Total = 1\r\n" +
        "End Function\r\n";

    [AvaloniaFact]
    public async Task TheSavesHeaderAndItsQualifiersAreOneUndoEntry()
    {
        // The save's two writes into the window, made as one. So even an undo that does not come through the code
        // window's own Undo (#513 lists those) cannot separate the header a save rendered from the qualifiers it
        // rewrote.
        var service = MakeService();
        var form = await FormOnDisk(service, StaleForm);
        var editor = Open(DocumentIdentity.For(form));
        Retype(editor, "Public Function Total", "Total", "GrandTotal");
        var beforeSave = editor.Document.Text;

        (await service.SaveForm(form, saveAs: false, interactive: false)).Should().BeTrue();
        var written = Decoded(form.AbsolutePath!);
        written.Should().Contain("Attribute GrandTotal.VB_Description").And.NotContain("\r\n    Caption         =   \"Orders\"",
            "the premise: the save rewrote both the run and the header, whose four-space indent the serializer does not keep");
        editor.Document.Text.Should().Be(written);

        editor.Document.UndoStack.Undo();

        editor.Document.Text.Should().Be(beforeSave, "one entry took back the header and the qualifier together");
    }

    [AvaloniaFact]
    public async Task UndoInTheCodeWindowAfterASaveThatRewroteQualifiersDoesNotTakeThemBack()
    {
        // A form changed only in its designer is a changed document, so its save rewrites the stale qualifier. Ctrl+Z
        // in its code window then has no code edit to undo: it goes past the IDE's own writes, the designer's header
        // and the save's, and puts back what the IDE owns. Without the qualifiers among those, the window would be
        // left holding `Attribute Gone.` under a file that says `Total.`.
        var service = MakeService();
        var form = await FormOnDisk(service, StaleForm);
        var editor = Open(DocumentIdentity.For(form));
        form.Components[0].SetProperty(VBProperties.CaptionProperty, "Sales");
        refresher.LayoutChanged(form);

        (await service.SaveForm(form, saveAs: false, interactive: false)).Should().BeTrue();
        var written = Decoded(form.AbsolutePath!);
        written.Should().Contain("Attribute Total.VB_Description", "the premise: the save rewrote the run");
        editor.Document.Text.Should().Be(written);

        editor.UndoRequested(() => editor.Document.UndoStack.Undo());

        editor.Document.Text.Should().Be(written, "nothing the developer did here is there to undo");
    }

    [AvaloniaFact]
    public async Task UndoAcrossADesignerChangeLeavesALoadedStaleQualifierAsItWasRead()
    {
        // The qualifiers are put back only after an undo that undid a save's rewrite of them. An undo past a designer
        // change alone, with no save, leaves the window's runs as the file has them: rewriting them there would
        // change the document by itself, with nothing saved to follow.
        var service = MakeService();
        var form = await FormOnDisk(service, StaleForm);
        var editor = Open(DocumentIdentity.For(form));
        form.Components[0].SetProperty(VBProperties.CaptionProperty, "Sales");
        refresher.LayoutChanged(form);

        editor.UndoRequested(() => editor.Document.UndoStack.Undo());

        editor.Document.Text.Should().Contain("\"Sales\"", "the premise: the designer's header is put back, not undone")
            .And.Contain("Attribute Gone.VB_Description");
    }

    [AvaloniaFact]
    public async Task ACaretBelowARewrittenRunStaysOnTheTextItWasOn()
    {
        // The rewrite replaces each qualifier in place, so everything below a run that grew moves down with it, and the
        // view model's own copy of the caret, which a window with no view attached has alone, moves too.
        var service = MakeService();
        var module = await ClassOnDisk(service, "CalcC2", Calc("CalcC2", "Total", "Total"));
        var editor = Open(DocumentIdentity.For(module));
        Retype(editor, "Public Function Total", "Total", "GrandTotal");
        editor.CaretOffset = editor.Document.Text.IndexOf("Public Sub Other", StringComparison.Ordinal);

        (await service.SaveModule(module, saveAs: false, interactive: false)).Should().BeTrue();

        editor.CaretOffset.Should().Be(editor.Document.Text.IndexOf("Public Sub Other", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task UndoAfterASaveThatRewroteQualifiersTakesBackOnlyTheEditBeforeIt()
    {
        // The other side: with a code edit before the save, Undo takes that edit back and keeps the qualifiers the
        // save wrote, which describe the declaration the edit did not touch.
        var service = MakeService();
        var form = await FormOnDisk(service, StaleForm);
        var editor = Open(DocumentIdentity.For(form));
        Retype(editor, "    Total = 1", "1", "2");

        (await service.SaveForm(form, saveAs: false, interactive: false)).Should().BeTrue();
        editor.UndoRequested(() => editor.Document.UndoStack.Undo());

        editor.Document.Text.Should().Contain("Attribute Total.VB_Description").And.Contain("    Total = 1\r\n");
    }

    [AvaloniaFact]
    public async Task ACopyMakeWritesLeavesTheModelsQualifiersAsTheyAre()
    {
        // Make writes every form into a temporary directory and puts the project back afterwards, so nothing it
        // writes is the developer's save. The model keeps its text, as it keeps its header.
        var service = MakeService();
        var form = await FormOnDisk(service, StaleForm);
        var editor = Open(DocumentIdentity.For(form));
        Retype(editor, "    Total = 1", "1", "2");
        bus.Publish(new ApplyAllUnsavedChangesEvent());
        var copy = Path.Join(dir, "packaged", "Form1.frm");
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);

        service.SerializeFormToFile(form, copy, adoptHeader: false).Should().BeTrue();

        form.Code.Should().Contain("Attribute Gone.VB_Description");
        Decoded(copy).Should().Contain("Attribute Gone.VB_Description");
        editor.Document.Text.Should().Contain("Attribute Gone.VB_Description");
    }

    [AvaloniaFact]
    public async Task ACopyMakeWritesOfAModuleLeavesTheModelsQualifiersAsTheyAre()
    {
        // The module half of the same rule, which is a separate write: Make repoints each module into its temporary
        // directory and writes it through the save core without announcing it, then puts every path back. At that
        // path the module has no baseline, so the dirty check alone would call it changed and rewrite the model.
        var service = MakeService();
        const string name = "NamesNothing";
        var module = await ClassOnDisk(service, name, Shape(name, "p4 marker",
            "Public Function Total() As Long\r\nAttribute Gone.VB_Description = \"Names nothing.\"\r\n    Total = 1\r\nEnd Function\r\n"));
        var editor = Open(DocumentIdentity.For(module));
        Retype(editor, "' p4 marker", "p4 marker", "p4 marker2");
        var own = module.AbsolutePath;
        var copy = Path.Join(dir, "packaged", name + ".cls");
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);

        module.AbsolutePath = copy;
        try
        {
            (await service.SaveModuleCore(module, saveAs: false, announceSave: false)).Should().BeTrue();
        }
        finally
        {
            module.AbsolutePath = own;
        }

        Decoded(copy).Should().Contain("Attribute Gone.VB_Description", "the copy is written from the model as it stands");
        module.Code.Should().Contain("Attribute Gone.VB_Description");
        editor.Document.Text.Should().Contain("Attribute Gone.VB_Description");
    }

    /// <summary>A class with two procedures whose runs name nothing, the second's name the longer.</summary>
    private static string TwoStaleRuns(string marker, string first, string second) => Vb6Class("TwoRuns",
        "Option Explicit\r\n" +
        $"' {marker}\r\n" +
        "\r\n" +
        "Public Function Total() As Long\r\n" +
        $"Attribute {first}.VB_Description = \"Adds things up.\"\r\n" +
        "    Total = 1\r\n" +
        "End Function\r\n" +
        "\r\n" +
        "Public Sub LongerName()\r\n" +
        $"Attribute {second}.VB_Description = \"Does more.\"\r\n" +
        "End Sub\r\n");

    [AvaloniaFact]
    public async Task SeveralRewritesInOneWindowLandWhereTheSaveWroteThem()
    {
        // Each rewrite changes the length of the text after it, so the window makes them from the last to the first:
        // made the other way, every offset after the first rewrite would be off by what that one added. The window's
        // own check compares the save's text with a rewrite composed separately, so only the window's text after the
        // write can tell the two orders apart.
        var service = MakeService();
        var module = await ClassOnDisk(service, "TwoRuns", TwoStaleRuns("p4 marker", "G", "X"));
        var editor = Open(DocumentIdentity.For(module));
        Retype(editor, "' p4 marker", "p4 marker", "p4 marker2");

        (await service.SaveModule(module, saveAs: false, interactive: false)).Should().BeTrue();

        Decoded(module.AbsolutePath!).Should().Be(TwoStaleRuns("p4 marker2", "Total", "LongerName"));
        editor.Document.Text.Should().Be(Decoded(module.AbsolutePath!));
    }

    [AvaloniaFact]
    public async Task ACaretAboveARewrittenRunDoesNotMove()
    {
        // The other side of ACaretBelowARewrittenRunStaysOnTheTextItWasOn: a rewrite after the caret moves nothing
        // before it.
        var service = MakeService();
        var module = await ClassOnDisk(service, "TwoRuns", TwoStaleRuns("p4 marker", "G", "X"));
        var editor = Open(DocumentIdentity.For(module));
        Retype(editor, "' p4 marker", "p4 marker", "p4 marker2");
        var above = editor.Document.Text.IndexOf("Option Explicit", StringComparison.Ordinal);
        editor.CaretOffset = above;

        (await service.SaveModule(module, saveAs: false, interactive: false)).Should().BeTrue();

        editor.Document.Text.Should().Contain("Attribute LongerName.VB_Description", "the premise: both runs were rewritten");
        editor.CaretOffset.Should().Be(above);
    }

    [AvaloniaFact]
    public async Task AnUndoAfterOneThatTookBackASavesRewriteLeavesAStaleRunAsItStands()
    {
        // Whether an undo puts the qualifiers back is asked afresh for each Undo. The first Undo here takes back the
        // save's rewrite with the edit before it, and the window rewrites the run again from the text as it then
        // stands. The second takes back a designer change and the edit before that, and no save's rewrite: the
        // stale run a retype made since stays as it stands until a save writes it, as it does with no undo at all.
        var service = MakeService();
        var form = await FormOnDisk(service, StaleForm);
        var editor = Open(DocumentIdentity.For(form));
        Retype(editor, "    Total = 1", "1", "2");
        (await service.SaveForm(form, saveAs: false, interactive: false)).Should().BeTrue();
        editor.UndoRequested(() => editor.Document.UndoStack.Undo());
        editor.Document.Text.Should().Contain("Public Function Total() As Long\r\nAttribute Total.VB_Description",
            "the premise: the first Undo undid the save's rewrite, and the window made it again");

        Retype(editor, "Public Function Total", "Total", "Halt");
        Retype(editor, "    Total = 1", "1", "3");
        form.Components[0].SetProperty(VBProperties.CaptionProperty, "Sales");
        refresher.LayoutChanged(form);
        editor.UndoRequested(() => editor.Document.UndoStack.Undo());

        editor.Document.Text.Should().Contain("    Total = 1\r\n", "the premise: the second Undo took back the body edit")
            .And.Contain("Public Function Halt() As Long\r\nAttribute Total.VB_Description");
    }
}
