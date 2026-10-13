using System.IO;
using HexIDE.Lsp;
using HexIDE.Runtime.Components;

namespace HexIDE.Tests.IDE;

/// <summary>
/// How an automation tool finds the document a caller named, and what it replies when it cannot
/// (hexide-io/HexIDE#273, the hexide-mcp-server delta).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="HexIDE.Tests.ProjectElements.DocumentLookupTests"/> covers the search. These cover what used to live only in the tool bodies, which
/// nothing could test: the refusals a caller reads, the carried-file fallback, the designer refusals, and the reverse
/// lookup that names a server's document for <c>get_diagnostics</c>. Every tool that takes a document's name goes
/// through this, including the three that did not until this change: <c>set_control_property</c> searched the
/// startup project only, and <c>move_control</c> and <c>add_control</c> took the first open designer of that name.
/// </para>
/// </remarks>
public sealed class NamedDocumentsTests
{
    private static ProjectDefinition AProject(string name, string[]? forms = null, string[]? modules = null)
    {
        var project = TestHelpers.CreateProject(name);
        foreach (var form in forms ?? [])
            project.AddForm(new FormDefinition(project, FormComponentClass.Instance, form));
        foreach (var module in modules ?? [])
            project.AddModule(new ModuleDefinition(project, module, ModuleKind.StandardModule));
        return project;
    }

    private static RelatedDocumentDefinition Carry(ProjectDefinition project, string name, string? fileName = null)
    {
        var file = new RelatedDocumentDefinition(project, name,
            fileName is null ? null : Path.Combine(Path.GetTempPath(), "HexIDE.Tests.NamedDocuments", fileName));
        project.AddRelatedDocument(file);
        return file;
    }

    [Theory]
    [InlineData("Form1")]
    [InlineData("form1")]
    [InlineData("FORM1")]
    public void ANameInAnyCaseFindsTheDocumentAndTheReplyUsesItsOwnSpelling(string spelling)
    {
        var project = AProject("P", forms: ["Form1"]);

        var named = NamedDocuments.Resolve([project], spelling, null);

        named.Error.Should().BeNull();
        named.Document.Should().Be(DocumentIdentity.For(project.Forms[0]));
        named.Name.Should().Be("Form1", "a reply names the document as the IDE spells it, which is the only way a "
            + "caller can see that a name matched without regard to case was understood (#467)");
        named.Project.Should().Be("P");
    }

    [Fact]
    public void ADocumentInAnotherLoadedProjectIsFound()
    {
        var first = AProject("First", modules: ["Alpha"]);
        var second = AProject("Second", modules: ["Beta"]);

        NamedDocuments.Resolve([first, second], "beta", null).Document!.Project.Should().BeSameAs(second);
    }

    [Fact]
    public void ANameInTwoProjectsIsRefusedAndTheRefusalListsBoth()
    {
        var first = AProject("First", modules: ["Module1"]);
        var second = AProject("Second", modules: ["Module1"]);

        var named = NamedDocuments.Resolve([first, second], "module1", null);

        named.Document.Should().BeNull("answering with whichever was found first is a coin toss the caller cannot see");
        named.Missing.Should().BeFalse("two documents answered, so it is not a name to look for among carried files");
        named.Error.Should().Be("'module1' names more than one document: First/Module1, Second/Module1. "
                                + "Pass `project` to say which.");
    }

    [Fact]
    public void TheProjectArgumentSaysWhichAndIsMatchedInAnyCase()
    {
        var first = AProject("First", modules: ["Module1"]);
        var second = AProject("Second", modules: ["Module1"]);

        var named = NamedDocuments.Resolve([first, second], "Module1", "SECOND");

        named.Document!.Project.Should().BeSameAs(second);
        named.Project.Should().Be("Second", "the reply spells the project as the IDE does, too");
    }

    [Fact]
    public void AProjectThatIsNotLoadedIsRefusedWithTheLoadedOnes()
    {
        var named = NamedDocuments.Resolve(
            [AProject("First", modules: ["Module1"]), AProject("Second")], "Module1", "Third");

        named.Document.Should().BeNull();
        named.Error.Should().Be("No loaded project named 'Third'. Loaded: First, Second");
    }

    [Fact]
    public void NothingLoadedIsSaidSo() =>
        NamedDocuments.Resolve([], "Form1", null).Error.Should().Be("No project loaded");

    [Fact]
    public void AMissSaysWhatEachProjectHolds()
    {
        var first = AProject("First", forms: ["Form1"], modules: ["Module1"]);
        Carry(first, "Notes");
        var second = AProject("Second");

        var named = NamedDocuments.Resolve([first, second], "Nope", null);

        named.Missing.Should().BeTrue();
        named.Error.Should().Be("No form or module named 'Nope' in any loaded project. "
                                + "First holds forms and UserControls Form1; modules Module1; carried files Notes. "
                                + "Second holds no forms and UserControls; no modules; no carried files.");
    }

    [Fact]
    public void AMissInANamedProjectSaysWhatThatProjectHolds()
    {
        var first = AProject("First", modules: ["Module1"]);
        var second = AProject("Second", forms: ["Form1"]);

        NamedDocuments.Resolve([first, second], "Module1", "second").Error.Should().Be(
            "No form or module named 'Module1' in project 'second'. "
            + "Second holds forms and UserControls Form1; no modules; no carried files.");
    }

    [Fact]
    public void ACarriedFileIsFoundOnlyByAToolThatTakesOne()
    {
        var project = AProject("P", modules: ["Module1"]);
        var notes = Carry(project, "Notes");

        var taken = NamedDocuments.Find([project], "notes", null, carried: true);
        taken.Carried.Should().BeSameAs(notes);
        taken.Project.Should().Be("P");
        taken.Name.Should().Be("Notes");

        var refused = NamedDocuments.Find([project], "notes", null, carried: false);
        refused.Carried.Should().BeNull();
        refused.Error.Should().StartWith("No form or module named 'notes' in any loaded project: 'notes' is a carried file. ",
            "a refusal says what the name is when it is something else (#573)");
    }

    [Fact]
    public void ACarriedFileIsFoundByItsFilenameWhenNothingHasThatName()
    {
        // VB6 carries a file on a code line as `Module=Notes; Notes.md`, which names it Notes (#548).
        var project = AProject("P");
        var notes = Carry(project, "Notes", "Notes.md");

        NamedDocuments.Find([project], "notes.md", null, carried: true).Carried.Should().BeSameAs(notes);
    }

    [Fact]
    public void ACarriedFileOfOneNameInTwoProjectsIsRefused()
    {
        var first = AProject("First");
        Carry(first, "Notes");
        var second = AProject("Second");
        Carry(second, "Notes");

        NamedDocuments.Find([first, second], "Notes", null, carried: true).Error.Should().Be(
            "'Notes' names more than one carried file: First/Notes, Second/Notes. Pass `project` to say which.");
    }

    [Fact]
    public void AFormOrModuleIsFoundAheadOfACarriedFileOfTheSameName()
    {
        var project = AProject("P", modules: ["Notes"]);
        Carry(project, "Notes");

        var named = NamedDocuments.Find([project], "Notes", null, carried: true);

        named.Document.Should().NotBeNull();
        named.Carried.Should().BeNull();
    }

    [Fact]
    public void AFormsDesignerIsTheFormItself()
    {
        var project = AProject("P", forms: ["Form1"]);

        var named = NamedDocuments.FindDesigner([project], "form1", null);

        named.Form.Should().BeSameAs(project.Forms[0]);
        named.Document!.Name.Should().Be("Form1");
    }

    [Fact]
    public void AUserControlsDesignerIsItsFormPartAndTheReplyNamesItsModule()
    {
        var project = TestHelpers.CreateProject("P");
        var gauge = new ModuleDefinition(project, "Gauge", ModuleKind.UserControl);
        gauge.UpdateFormPart(new FormDefinition(project, FormComponentClass.Instance, "Gauge"));
        project.AddModule(gauge);

        var named = NamedDocuments.FindDesigner([project], "GAUGE", null);

        named.Form.Should().BeSameAs(gauge.FormPart);
        named.Document!.Module.Should().BeSameAs(gauge,
            "a UserControl is one file and one identity, and the file the save writes is its module's");
    }

    [Fact]
    public void AModuleHasNoDesignerAndTheRefusalSaysWhereItsCodeOpens()
    {
        var named = NamedDocuments.FindDesigner([AProject("P", modules: ["Module1"])], "module1", null);

        named.Form.Should().BeNull();
        named.Error.Should().Be("'Module1' is a module, which has no designer. open_file opens its code.",
            "\"No form named 'Module1'\" read as a wrong name, when the name was right and the tool was not (#573)");
    }

    [Fact]
    public void ACarriedFileHasNoDesigner()
    {
        var project = AProject("P");
        Carry(project, "Notes");

        NamedDocuments.FindDesigner([project], "Notes", null).Error
            .Should().Be("'Notes' is a carried file, which has no designer. open_file opens it.");
    }

    [Fact]
    public void AnAmbiguousNameIsRefusedForTheDesignerToo()
    {
        var first = AProject("First", forms: ["Form1"]);
        var second = AProject("Second", forms: ["Form1"]);

        var named = NamedDocuments.FindDesigner([first, second], "Form1", null);

        named.Form.Should().BeNull("move_control and add_control used to take the first open designer of that name");
        named.Error.Should().Be("'Form1' names more than one document: First/Form1, Second/Form1. Pass `project` to say which.");
        NamedDocuments.FindDesigner([first, second], "Form1", "Second").Form.Should().BeSameAs(second.Forms[0]);
    }

    [Fact]
    public void ADocumentWithNoFileIsNamedByItsUntitledNameInAnyCase()
    {
        var project = AProject("P", modules: ["Module1"]);

        // A VB6 name is compared without regard to case, and an untitled: name is made of two of them (task 2.2).
        var named = NamedDocuments.ForUri([project], "untitled:p/MODULE1.bas");

        named.Document.Should().Be(DocumentIdentity.For(project.Modules[0]));
        named.Project.Should().Be("P");
        named.Name.Should().Be("Module1");
    }

    [Fact]
    public void ADocumentWithAFileIsNamedByItsFileUri()
    {
        var project = AProject("P", modules: ["Module1"]);
        var module = project.Modules[0];
        module.AbsolutePath = Path.Combine(Path.GetTempPath(), "HexIDE.Tests.NamedDocuments", "Module1.bas");

        var named = NamedDocuments.ForUri([project], DocumentWireName.For(module));

        named.Document.Should().Be(DocumentIdentity.For(module));
        NamedDocuments.ForUri([project], LspDocumentUri.ForUntitled("P", "Module1", ".bas")).Document.Should()
            .BeNull("a document with a file is known to a server by the file, and by nothing else");
    }

    [Fact]
    public void ACarriedFileIsNamedByItsFileUri()
    {
        var project = AProject("P");
        var notes = Carry(project, "Notes", "Notes.md");

        var named = NamedDocuments.ForUri([project], LspDocumentUri.ForFile(notes.AbsolutePath!));

        named.Carried.Should().BeSameAs(notes);
        named.Project.Should().Be("P");
        named.Name.Should().Be("Notes");
    }

    [Fact]
    public void ANameNothingLoadedAnswersToNamesNothing()
    {
        var project = AProject("P", modules: ["Module1"]);

        var named = NamedDocuments.ForUri([project],
            LspDocumentUri.ForFile(Path.Combine(Path.GetTempPath(), "HexIDE.Tests.NamedDocuments", "Elsewhere.md")));

        named.Project.Should().BeNull("a server may analyse a file no project holds, and saying nothing is the answer");
        named.Name.Should().BeNull();
        named.Error.Should().BeNull();
    }
}
