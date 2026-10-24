using System.IO;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;

namespace HexIDE.Tests.IDE;

/// <summary>
/// Reading a form's, module's or class's content and writing it back: the rule behind <c>get_file_content</c>
/// and <c>set_file_content</c> (hexide-io/HexIDE#273 task 3.18).
/// </summary>
/// <remarks>
/// The tools themselves are compiled into an executable no test references, so these prove the rule they
/// relay. Which text a tool reads, and the save that follows a write, are the live check's (task 3.22).
/// </remarks>
public class DocumentContentTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-content-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> loaded = [];
    private readonly List<CodeEditorViewModel> made = [];

    public DocumentContentTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        foreach (var vm in made) vm.Dispose();
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    // Lines 1-7 the designer block, 8-9 the attribute run: the header is 9 lines, and the code starts at 10.
    private const string Frm =
        "VERSION 5.00\r\n" +
        "Begin VB.Form frmOrders \r\n" +
        "   Caption         =   \"Orders\"\r\n" +
        "   Begin VB.CommandButton Command1 \r\n" +
        "      Caption         =   \"OK\"\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"frmOrders\"\r\n" +
        "Attribute VB_PredeclaredId = True\r\n" +
        "Option Explicit\r\n" +
        "\r\n" +
        "Private Sub Command1_Click()\r\n" +
        "    Command1.Enabled = False\r\n" +
        "End Sub\r\n";

    private const string ClassCode =
        "Option Explicit\r\n" +
        "\r\n" +
        "Public Function Total() As Currency\r\n" +
        "    Total = 0\r\n" +
        "End Function\r\n";

    private static FormDefinition NewForm() =>
        new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), Frm, NullSink.Instance)!;

    private static ModuleDefinition NewModule(ModuleKind kind)
    {
        var module = TestHelpers.CreateModule(name: kind == ModuleKind.ClassModule ? "Order" : "Helpers", kind: kind);
        module.UpdateCode(ClassCode);
        return module;
    }

    private CodeEditorViewModel NewEditor()
    {
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
        var vm = new CodeEditorViewModel(
            Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(), eventBus, Substitute.For<ILspClient>(),
            Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(),
            Substitute.For<IBookmarkService>(), Substitute.For<IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            Substitute.For<IRunScope>(), Substitute.For<ILocalizationService>());
        made.Add(vm);
        return vm;
    }

    /// <summary>
    /// A project service with the real header refresher, so a save leaves the model holding the header it
    /// wrote, as the IDE's does.
    /// </summary>
    private ProjectService MakeService()
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(_ => loaded);
        projectManager.When(m => m.AddProject(Arg.Any<ProjectDefinition>()))
                      .Do(ci => loaded.Add(ci.Arg<ProjectDefinition>()));

        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);

        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns(new List<BaseEditorWindowViewModel>());

        return new ProjectService(
            () => throw new InvalidOperationException("new-project dialog must not be reached"),
            Substitute.For<IWindowManager>(),
            Substitute.For<IEventBus>(),
            projectManager,
            Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(),
            sidecar,
            new FileBaselineStore(),
            Substitute.For<ILocalizationService>(),
            new HeaderRefresher(new EventBus(), dock, new BreakpointService(), new BookmarkService()));
    }

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

    /// <summary>
    /// A copy of a repository project's source files in a directory of its own, so that a save writes there
    /// and never into the repository. Returns the copied <c>.vbp</c>.
    /// </summary>
    private string Stage(string projectDirectory)
    {
        // Named after the project's folder, and numbered where two share a name: each of the member-attribute
        // evidence's four projects keeps VB6's output in a folder called saved.
        var target = Path.Join(dir, Path.GetFileName(projectDirectory));
        for (var n = 2; Directory.Exists(target); n++)
            target = Path.Join(dir, Path.GetFileName(projectDirectory) + n);
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(projectDirectory))
        {
            if (SourceExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                File.Copy(file, Path.Join(target, Path.GetFileName(file)));
        }
        return Directory.EnumerateFiles(target, "*.vbp").Single();
    }

    /// <summary>
    /// Every project the repository carries VB6 source for: the demos, the designer corpus, and the projects VB6's own
    /// IDE wrote members' attribute lines into (#273 task 4.6), which are the round-trip half of
    /// <c>corpus/member-attributes</c> and the project VB6 saved for each hand-written one of its evidence half. Not
    /// those hand-written inputs, which are not VB6's bytes.
    /// </summary>
    private static IEnumerable<string> RepositoryProjects()
    {
        var memberAttributes = FindUpwards(Path.Join("corpus", "member-attributes"));
        var roots = new[]
        {
            FindUpwards("demo"), FindUpwards(Path.Join("corpus", "designer")),
            memberAttributes is null ? null : Path.Join(memberAttributes, "round-trip"),
        }.OfType<string>().ToList();
        roots.Should().HaveCount(3, "the repository's own VB6 projects are the corpus here, and must be found");
        roots.AddRange(Directory.EnumerateDirectories(Path.Join(memberAttributes!, "evidence"), "saved*", SearchOption.AllDirectories));
        return roots.SelectMany(root => Directory.EnumerateFiles(root, "*.vbp", SearchOption.AllDirectories))
            .Select(Path.GetDirectoryName).OfType<string>().Distinct().Order(StringComparer.Ordinal);
    }

    // ── The hexide-mcp-server delta's scenario ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadingAndWritingBackAForm()
    {
        // A form VB6 wrote, read the way get_file_content reads it, written back the way set_file_content
        // writes it, and saved as that tool always saves. "Unchanged" is asserted on the bytes on disk, which is
        // only meaningful for a form the serializer reproduces exactly. BillOfFare is one, and this assertion is
        // what holds it there: SerializationCorpusTests counts everything under demo/ as HexIDE's own and does
        // not hold that bucket to zero differences.
        var vbp = Stage(Path.Join(FindUpwards("demo")!, "bill-of-fare"));
        var service = MakeService();
        await service.OpenProject(vbp);
        var form = loaded.Single().Forms.Single();
        var document = DocumentIdentity.For(form);
        var bytes = await File.ReadAllBytesAsync(form.AbsolutePath!, TestContext.Current.CancellationToken);
        var code = form.Code;

        var read = DocumentContent.Read(document, editor: null);
        read.Should().Be(Vb6TextFile.ReadDocument(bytes).Text, "reading a form returns its whole file, designer block included");
        read.Should().StartWith("VERSION ");

        var written = DocumentContent.Write(document, editor: null, read);
        written.Refusal.Should().BeNull("the write takes back exactly what the read returned");
        written.NewText.Should().Be(read);
        DocumentContent.LeftOutAttributeLines(document, read).Should().BeFalse("nothing was left out, so nothing is reported kept");
        (await service.SaveForm(form, saveAs: false, interactive: false)).Should().BeTrue();

        var after = await File.ReadAllBytesAsync(form.AbsolutePath!, TestContext.Current.CancellationToken);
        after.Should().Equal(bytes, "the form is unchanged");
        form.Code.Should().Be(code);
        DocumentContent.Read(document, editor: null).Should().Be(read);
    }

    [Fact]
    public async Task ReadingAndWritingBackAFormWhoseAttributeLineNamesAnotherName()
    {
        // The scenario's "but for": set_file_content saves as the form's own Save does, and VB6's own Save of a
        // document nobody changed writes its runs afresh (the oracle's third IDE session), so a stale qualifier read
        // and written back comes out naming its member, and nothing else of the file moves (#273 task 4.4).
        var vbp = Stage(Path.Join(FindUpwards("demo")!, "bill-of-fare"));
        var frm = Directory.EnumerateFiles(Path.GetDirectoryName(vbp)!, "*.frm").Single();
        const string stale = "Public Function Total() As Long\r\nAttribute Gone.VB_Description = \"Names nothing.\"\r\n"
                             + "    Total = 1\r\nEnd Function\r\n";
        await File.AppendAllTextAsync(frm, "\r\n" + stale, TestContext.Current.CancellationToken);
        var bytes = await File.ReadAllBytesAsync(frm, TestContext.Current.CancellationToken);
        var service = MakeService();
        await service.OpenProject(vbp);
        var form = loaded.Single().Forms.Single();
        var document = DocumentIdentity.For(form);

        var read = DocumentContent.Read(document, editor: null);
        DocumentContent.Write(document, editor: null, read).Refusal.Should().BeNull();
        (await service.SaveForm(form, saveAs: false, interactive: false)).Should().BeTrue();

        var after = Vb6TextFile.ReadDocument(await File.ReadAllBytesAsync(frm, TestContext.Current.CancellationToken)).Text;
        after.Should().Be(Vb6TextFile.ReadDocument(bytes).Text.Replace("Attribute Gone.", "Attribute Total."),
            "the form is unchanged but for the qualifier, which names its member as the form's own Save writes it");
    }

    [AvaloniaFact]
    public void ReadingAndWritingBackAFormWithItsCodeWindowOpen()
    {
        var form = NewForm();
        var document = DocumentIdentity.For(form);
        var vm = NewEditor().Initialize(form);

        var read = DocumentContent.Read(document, vm);
        read.Should().Be(Frm, "an open window's buffer is the whole file");

        DocumentContent.Write(document, vm, read).Refusal.Should().BeNull();

        vm.Document.Text.Should().Be(Frm);
        DocumentContent.CodeAheadOfModel(document, vm).Should().BeFalse();
    }

    [Fact]
    public async Task EveryDocumentTheRepositoryCarriesReadsAsItsFileAndTakesItBack()
    {
        // The header a write compares against is found two ways: the document's by the prefix it was composed
        // with, the incoming text's by walking it. Nothing else compares the two over files VB6 wrote, and a file
        // where they disagreed would refuse its own round trip. Forms, modules, classes, a UserControl and a
        // PropertyPage are all here, and the last two go through the module's half.
        var checkedDocuments = new List<string>();
        foreach (var project in RepositoryProjects())
        {
            loaded.Clear();
            await MakeService().OpenProject(Stage(project));
            foreach (var document in DocumentLookup.DocumentsOf(loaded.Single()))
            {
                var file = Vb6TextFile.ReadDocument(File.ReadAllBytes(document.AbsolutePath!)).Text;
                var code = document.Module?.Code ?? document.Form!.Code;

                var read = DocumentContent.Read(document, editor: null);
                read.Should().Be(file, $"{document.Name} reads as its whole file");

                var written = DocumentContent.Write(document, editor: null, read);
                written.Refusal.Should().BeNull($"{document.Name} takes back what reading it returned");
                written.NewText.Should().Be(read);
                (document.Module?.Code ?? document.Form!.Code).Should().Be(code);
                checkedDocuments.Add(Path.GetFileName(document.AbsolutePath!));
            }
        }

        checkedDocuments.Should().Contain(["BillOfFare.frm", "Gauge.ctl", "GaugeGeneral.pag", "TideTable.bas", "Game.cls"],
            "every kind is covered, or the claim above is about fewer kinds than it says");
        checkedDocuments.Should().Contain(["Main.frm", "Panel.ctl", "Helpers.bas", "Ledger.cls"],
            "and every kind again with members' attribute lines in it, as VB6's IDE writes them");
        checkedDocuments.Should().Contain(["BothRuns.cls", "ConstMulti.bas"],
            "and what VB6 saved of the evidence's hand-written projects, a property's two runs and a constant's among it");
    }

    // ── What the read returns ──────────────────────────────────────────────────────────────────────────

    public static TheoryData<string> Kinds => new() { "form", "class", "module" };

    private static DocumentIdentity DocumentOf(string kind) => kind switch
    {
        "form" => DocumentIdentity.For(NewForm()),
        "class" => DocumentIdentity.For(NewModule(ModuleKind.ClassModule)),
        _ => DocumentIdentity.For(NewModule(ModuleKind.StandardModule)),
    };

    [Theory]
    [MemberData(nameof(Kinds))]
    public void AClosedDocumentReadsAsTheTextItsLineNumbersCountIn(string kind)
    {
        // Line N of what the read returns is line N to set_breakpoints and the other mark tools, which count in
        // the same composition.
        var document = DocumentOf(kind);
        var read = DocumentContent.Read(document, editor: null);

        read.Should().Be(CodeWindowText.Of(document).Text);
        read.Should().EndWith(document.Module?.Code ?? document.Form!.Code);
        read.Length.Should().BeGreaterThan((document.Module?.Code ?? document.Form!.Code).Length, "the header comes first");
    }

    [AvaloniaTheory]
    [MemberData(nameof(Kinds))]
    public void AnOpenDocumentReadsAsItsBuffer(string kind)
    {
        var document = DocumentOf(kind);
        var vm = document.Module is { } module ? NewEditor().Initialize(module) : NewEditor().Initialize(document.Form!);

        DocumentContent.Read(document, vm).Should().Be(vm.CodeWindowText.Text)
            .And.Be(CodeWindowText.Of(document).Text, "open or not, the read is the same file");
    }

    [Fact]
    public void AClassReadsWithItsHeaderSoItsFirstLineOfCodeIsNotLineOne()
    {
        var document = DocumentOf("class");
        var lines = DocumentContent.Read(document, editor: null).Replace("\r\n", "\n").Split('\n');

        lines[0].Should().StartWith("VERSION 1.0 CLASS");
        Array.IndexOf(lines, "Option Explicit").Should().Be(CodeWindowText.Of(document).HeaderLineCount,
            "the code starts on the line after the header, counted from the top of the file");
    }

    // ── hasUnsavedChanges stays a question about the code ──────────────────────────────────────────────

    [AvaloniaFact]
    public void AnUntouchedOpenDocumentHasNoCodeAheadOfItsModel()
    {
        // The trap the read sets: what it returns is never equal to the model's Code, so comparing them would
        // call every open document edited.
        var form = NewForm();
        var document = DocumentIdentity.For(form);
        var vm = NewEditor().Initialize(form);

        DocumentContent.Read(document, vm).Should().NotBe(form.Code);
        DocumentContent.CodeAheadOfModel(document, vm).Should().BeFalse();
    }

    [AvaloniaFact]
    public void CodeTypedInAnOpenDocumentIsAheadOfItsModel()
    {
        var form = NewForm();
        var document = DocumentIdentity.For(form);
        var vm = NewEditor().Initialize(form);

        vm.Document.Insert(vm.Document.TextLength, "' typed\r\n");

        DocumentContent.CodeAheadOfModel(document, vm).Should().BeTrue();
    }

    [Fact]
    public void AClosedDocumentHasNoCodeAheadOfItsModel() =>
        DocumentContent.CodeAheadOfModel(DocumentOf("form"), editor: null).Should().BeFalse();

    // ── What the write accepts and refuses ─────────────────────────────────────────────────────────────

    [Fact]
    public void AWholeFormWithItsDesignerBlockUnchangedIsAccepted()
    {
        // New with this task: the tool used to refuse a whole .frm before the guard saw it (#338).
        var form = NewForm();
        var document = DocumentIdentity.For(form);
        var designer = form.DesignerText;

        var result = DocumentContent.Write(document, editor: null, Frm.Replace("Enabled = False", "Enabled = True"));

        result.Refusal.Should().BeNull();
        form.Code.Should().Contain("Command1.Enabled = True").And.StartWith("Attribute VB_Name");
        form.DesignerText.Should().Be(designer);
    }

    [Theory]
    [InlineData("\"Orders\"", "\"Sales\"")]
    [InlineData("Begin VB.Form frmOrders", "Begin VB.Form frmInvoices")]
    [InlineData("Attribute VB_PredeclaredId = True", "Attribute VB_PredeclaredId = False")]
    public void AWholeFormWithADifferentHeaderIsRefusedAndNothingChanges(string from, string to)
    {
        var form = NewForm();
        var document = DocumentIdentity.For(form);
        var (code, designer) = (form.Code, form.DesignerText);

        var result = DocumentContent.Write(document, editor: null, Frm.Replace(from, to));

        result.Refusal.Should().Contain("header");
        form.Code.Should().Be(code);
        form.DesignerText.Should().Be(designer);
    }

    [Fact]
    public void AClosedModulesMemberAttributeLinesAreTheGuardsWithNoWindowOpen()
    {
        // The model's composition is held to the same rule as an open buffer (#273 task 4.5b): a changed run is
        // refused naming its member, and nothing reaches the model; a rename that carries the run is written.
        var module = TestHelpers.CreateModule(name: "Order", kind: ModuleKind.ClassModule);
        module.UpdateCode(ClassCode.Replace("Currency\r\n", "Currency\r\nAttribute Total.VB_Description = \"The order total\"\r\n"));
        var document = DocumentIdentity.For(module);
        var code = module.Code;

        var changed = DocumentContent.Write(document, editor: null, code.Replace("The order total", "The total"));

        changed.Refusal.Should().Contain("the Attribute lines describing Total are changed");
        module.Code.Should().Be(code);

        DocumentContent.Write(document, editor: null, code.Replace("Total", "Amount")).Refusal.Should().BeNull();
        module.Code.Should().Be(code.Replace("Total", "Amount"));
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("added")]
    [InlineData("removed")]
    public void AWholeReplacementReportsTheHeaderAndMemberFindingsTogether(string change)
    {
        var module = NewModule(ModuleKind.ClassModule);
        const string attribute = "Attribute Total.VB_Description = \"The order total\"\r\n";
        if (change != "added")
            module.UpdateCode(ClassCode.Replace("Currency\r\n", "Currency\r\n" + attribute));
        var document = DocumentIdentity.For(module);
        var before = DocumentContent.Read(document, editor: null);
        var incoming = before.Replace("Attribute VB_Name", "Attribute VB_Description");
        incoming = change switch
        {
            "changed" => incoming.Replace("The order total", "The total"),
            "added" => incoming.Replace("Currency\r\n", "Currency\r\n" + attribute),
            _ => incoming.Replace(attribute, ""),
        };

        var result = DocumentContent.Write(document, editor: null, incoming);

        result.Refusal.Should().Contain("header").And.Contain("Attribute lines describing Total")
            .And.Contain($"are {change}");
        result.Refusal!.IndexOf("header", StringComparison.Ordinal).Should().BeLessThan(
            result.Refusal.IndexOf("Attribute lines describing Total", StringComparison.Ordinal));
        result.NewText.Should().BeNull();
        result.HeaderKept.Should().BeFalse();
        DocumentContent.Read(document, editor: null).Should().Be(before);
    }

    [AvaloniaFact]
    public void AWholeFormWithADifferentHeaderIsRefusedInAnOpenWindowToo()
    {
        var form = NewForm();
        var document = DocumentIdentity.For(form);
        var vm = NewEditor().Initialize(form);

        DocumentContent.Write(document, vm, Frm.Replace("\"OK\"", "\"Go\"")).Refusal.Should().Contain("header");

        vm.Document.Text.Should().Be(Frm);
    }

    [Fact]
    public void TheCodeAloneIsAcceptedAndTheHeaderKept()
    {
        var form = NewForm();
        var document = DocumentIdentity.For(form);
        const string body = "Option Explicit\r\nPrivate Sub Form_Load()\r\nEnd Sub\r\n";

        DocumentContent.Write(document, editor: null, body).Refusal.Should().BeNull();

        DocumentContent.Read(document, editor: null).Should().EndWith(
            "Attribute VB_PredeclaredId = True\r\n" + body, "the form's Attribute lines are kept in front of the code");
    }

    [Fact]
    public void AModulesCodeAloneIsAcceptedAndItsWholeFileToo()
    {
        var document = DocumentOf("class");
        var whole = DocumentContent.Read(document, editor: null);

        DocumentContent.Write(document, editor: null, "Option Explicit\r\n").Refusal.Should().BeNull();
        document.Module!.Code.Should().Be("Option Explicit\r\n");

        DocumentContent.Write(document, editor: null, whole).Refusal.Should().BeNull();
        DocumentContent.Read(document, editor: null).Should().Be(whole);
    }

    // ── What the reply reports ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CodeThatLeavesOutAFormsAttributeLinesIsReported()
    {
        var document = DocumentOf("form");

        DocumentContent.LeftOutAttributeLines(document, "Option Explicit\r\n").Should().BeTrue();
        DocumentContent.LeftOutAttributeLines(document, Frm).Should().BeFalse();
        DocumentContent.LeftOutAttributeLines(document, "Attribute VB_Name = \"frmOrders\"\r\nOption Explicit\r\n").Should().BeFalse();
        DocumentContent.LeftOutAttributeLines(DocumentOf("class"), "Option Explicit\r\n").Should().BeFalse(
            "a module's header is kept without remark; the report exists for a form's identity");
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void TheLinesAWriteReportsAreTheRangeTheMarkToolsAccept(string kind)
    {
        // The reply's range is the code window's: a refusal from set_breakpoints names the same last line.
        var document = DocumentOf(kind);
        var text = DocumentContent.TextOf(document, editor: null);

        MarkLineRules.Refusal(text, document.Name, [text.LineCount], [], first: 1, "breakpoint").Should().BeNull();
        MarkLineRules.Refusal(text, document.Name, [text.LineCount + 1], [], first: 1, "breakpoint")
            .Should().Contain($"1..{text.LineCount}");
    }
}
