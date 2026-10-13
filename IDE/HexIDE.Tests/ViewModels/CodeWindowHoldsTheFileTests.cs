using System.IO;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;

namespace HexIDE.Tests.ViewModels;

/// <summary>
/// The code window holds the whole file: as it stands when it is opened, and after a save as the text that save
/// wrote (hexide-io/HexIDE#273 task 3.21, the code-editor delta's "The code window SHALL hold the whole file").
/// </summary>
/// <remarks>
/// <para>
/// Real files, read from disk as the project service reads them, opened in real code windows, and saved through
/// the project service with those windows open: one event bus carries the flush that runs before a save and the
/// header refresh that follows it, and the dock hands the refresher the open windows, as the IDE's does.
/// </para>
/// <para>
/// <b>The save is <see cref="ProjectService.SaveForm"/>, not the code window's Ctrl+S.</b> The code window runs
/// Format on Save first, which needs a language server; what formatting does to the header is
/// <c>GuardedFormattingTests</c>' and the bundled server's own suite's. What is under test here is what a save
/// writes and what the window holds afterwards, which every save path shares.
/// </para>
/// </remarks>
public sealed class CodeWindowHoldsTheFileTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-window-file-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> loaded = [];
    private readonly List<CodeEditorViewModel> open = [];
    private readonly List<CodeEditorViewModel> made = [];
    private readonly EventBus bus = new();
    private readonly IDocumentDockService dock = Substitute.For<IDocumentDockService>();
    private readonly HeaderRefresher refresher;

    public CodeWindowHoldsTheFileTests()
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

    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
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

    private static string? FindUpwards(string relative)
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            var candidate = Path.Join(at.FullName, relative);
            if (Directory.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static readonly string[] SourceExtensions =
        [".vbp", ".frm", ".frx", ".bas", ".cls", ".ctl", ".ctx", ".pag", ".pgx"];

    /// <summary>A copy of a repository project in a directory of its own, so a save never writes into the repository.</summary>
    private string Stage(string projectDirectory)
    {
        var target = Path.Join(dir, Path.GetFileName(projectDirectory));
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(projectDirectory))
        {
            if (SourceExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                File.Copy(file, Path.Join(target, Path.GetFileName(file)));
        }
        return Directory.EnumerateFiles(target, "*.vbp").Single();
    }

    private static string Demo(string name) => Path.Join(FindUpwards("demo")!, name);

    /// <summary>Every project the repository carries VB6 source for: the demos and the designer corpus.</summary>
    private static IEnumerable<string> RepositoryProjects()
    {
        var roots = new[] { FindUpwards("demo"), FindUpwards(Path.Join("corpus", "designer")) }.OfType<string>().ToList();
        roots.Should().HaveCount(2, "the repository's own VB6 projects are the corpus here, and must be found");
        return roots.SelectMany(root => Directory.EnumerateFiles(root, "*.vbp", SearchOption.AllDirectories))
            .Select(Path.GetDirectoryName).OfType<string>().Distinct().Order(StringComparer.Ordinal);
    }

    // ── Opening ─────────────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task OpeningAClassModule()
    {
        // The code-editor delta's scenario, on a class read from its file as a project opens it. The class's header
        // carries the two lines VB6's Class Builder adds, so it is not the header HexIDE writes for a new class:
        // a window that showed a header of its own making rather than the file's would differ here, where on a
        // class whose header happens to be the canonical one it could not.
        var vbp = Stage(Demo("battleship"));
        var path = Path.Join(Path.GetDirectoryName(vbp)!, "Game.cls");
        const string exposed = "Attribute VB_Exposed = False\r\n";
        var original = Decoded(path);
        original.Should().Contain(exposed, "the premise: the class opens with VB6's attribute run");
        File.WriteAllText(path, original.Replace(exposed, exposed
            + "Attribute VB_Ext_KEY = \"SavedWithClassBuilder6\" ,\"Yes\"\r\n"
            + "Attribute VB_Ext_KEY = \"Top_Level\" ,\"Yes\"\r\n"));
        await MakeService().OpenProject(vbp);
        var game = DocumentLookup.Find(loaded, "Game", null).Single();

        var editor = Open(game);

        editor.Document.Text.Should().Be(Decoded(path), "the code window's text is the text of its file");
        editor.Document.Text.Should().StartWith("VERSION 1.0 CLASS\r\nBEGIN\r\n", "header included");
        editor.BufferBody.Should().Be(game.Module!.Code).And.NotContain("VB_Ext_KEY",
            "the Class Builder's lines are header, not code");
    }

    [AvaloniaFact]
    public async Task TheBufferIsTheFileOnOpenForEveryKind()
    {
        // Every document the repository carries, of every kind, opened from disk: the window holds the file, and
        // the split the next flush makes gives back exactly the code the model holds. The model-level round trip
        // is DocumentContentTests'; this is the window's, which composes the buffer by its own path.
        var opened = new List<string>();
        foreach (var project in RepositoryProjects())
        {
            loaded.Clear();
            await MakeService().OpenProject(Stage(project));
            foreach (var document in DocumentLookup.DocumentsOf(loaded.Single()))
            {
                var editor = Open(document);

                editor.Document.Text.Should().Be(Decoded(document.AbsolutePath!), $"{document.Name}'s window holds its file");
                editor.BufferBody.Should().Be(document.Module?.Code ?? document.Form!.Code,
                    $"{document.Name}'s window splits where its model's code starts");
                opened.Add(Path.GetExtension(document.AbsolutePath!).ToLowerInvariant());
            }
        }

        opened.Should().Contain([".frm", ".bas", ".cls", ".ctl", ".pag"],
            "every kind is opened, or the claim above is about fewer kinds than it says");
    }

    // ── Saving ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A form whose designer block the serializer does not reproduce: four-space indents. So its first save
    /// rewrites the header, and the window has something to follow.
    /// </summary>
    private const string OddlyFormatted =
        "VERSION 5.00\r\n"
      + "Begin VB.Form Form1 \r\n"
      + "    Caption         =   \"Orders\"\r\n"
      + "    Begin VB.Label Label1 \r\n"
      + "        Caption         =   \"Hi\"\r\n"
      + "    End\r\n"
      + "End\r\n"
      + "Attribute VB_Name = \"Form1\"\r\n"
      + "Option Explicit\r\n"
      + "\r\n"
      + "Private Sub Form_Load()\r\n"
      + "    Label1.Caption = \"Ready\"\r\n"
      + "End Sub\r\n";

    /// <summary>A form read from <paramref name="text"/>, written to disk first, in a project of its own.</summary>
    private FormDefinition FormOnDisk(string text)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Test") { AbsolutePath = Path.Join(dir, "Test.vbp") };
        loaded.Add(project);
        var path = Path.Join(dir, "Form1.frm");
        File.WriteAllText(path, text);
        var form = new FormDeserializer().Deserialize(project, Decoded(path), NullSink.Instance)!;
        form.AbsolutePath = path;
        project.AddForm(form);
        return form;
    }

    [AvaloniaFact]
    public async Task OpeningAFormAndSavingItUnchanged()
    {
        var service = MakeService();
        var form = FormOnDisk(OddlyFormatted);
        var editor = Open(DocumentIdentity.For(form));
        editor.Document.Text.Should().Be(OddlyFormatted, "the premise: the window opens on the file as read");

        (await service.SaveForm(form, saveAs: false, interactive: false)).Should().BeTrue();

        var written = Decoded(form.AbsolutePath!);
        written.Should().NotBe(OddlyFormatted, "the premise: the serializer reproduced the header rather than copying it");
        editor.Document.Text.Should().Be(written, "the code window's text afterwards is the text of the file that was written");

        editor.UndoRequested(() => editor.Document.UndoStack.Undo());

        editor.Document.Text.Should().Be(written, "the update is not something the developer can undo");
        editor.BufferBody.Should().Be(form.Code, "and it does not mark the document as edited");
        service.HasUnsavedChanges(form).Should().BeFalse();
        new DirtyDetector(new FileBaselineStore(), service)
            .Classify(new WatchedFileTarget(form.AbsolutePath!, form, null, editor, null))
            .Should().Be(ReloadDecision.CleanReload, "an external change now would be a silent reload, not a conflict");
    }

    /// <summary>
    /// The fixtures for an unchanged save. "The save writes the buffer" holds byte for byte only for a form the
    /// serializer reproduces exactly, because a form is written by rendering its model, not by copying what was
    /// read: <see cref="OddlyFormatted"/> is the counterexample, and is the scenario above. So each fixture here
    /// is render-stable, and the test checks that before it relies on it: one the serializer wrote itself, which
    /// is its own fixed point, and one written in VB6's format by hand and compiled by VB6, which the serializer
    /// happens to reproduce today.
    /// </summary>
    public static TheoryData<string> RenderStable => new() { "rendered", "bill-of-fare" };

    [AvaloniaTheory]
    [MemberData(nameof(RenderStable))]
    public async Task AnUnchangedSaveWritesTheBuffer(string fixture)
    {
        var service = MakeService();
        FormDefinition form;
        if (fixture == "rendered")
        {
            var rendered = new FormSerializer().Serialize(FormOnDisk(OddlyFormatted), "Form1.frm").Item1;
            loaded.Clear();
            form = FormOnDisk(rendered);
        }
        else
        {
            await service.OpenProject(Stage(Demo(fixture)));
            form = loaded.Single().Forms.Single();
        }
        var bytes = await File.ReadAllBytesAsync(form.AbsolutePath!, TestContext.Current.CancellationToken);
        new FormSerializer().Serialize(form, Path.GetFileName(form.AbsolutePath!)).Item1
            .Should().Be(Decoded(form.AbsolutePath!), "the premise: the serializer reproduces this file exactly");
        var editor = Open(DocumentIdentity.For(form));
        var buffer = editor.Document.Text;

        (await service.SaveForm(form, saveAs: false, interactive: false)).Should().BeTrue();

        Decoded(form.AbsolutePath!).Should().Be(buffer, "an unchanged save writes the buffer");
        (await File.ReadAllBytesAsync(form.AbsolutePath!, TestContext.Current.CancellationToken))
            .Should().Equal(bytes, "byte for byte, encoding and line endings included");
        editor.Document.Text.Should().Be(buffer, "and the window, having nothing to follow, is left as it was");
    }
}
