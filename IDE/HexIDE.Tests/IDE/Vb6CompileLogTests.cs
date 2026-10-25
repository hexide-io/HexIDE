using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.IDE;

/// <summary>
/// Reading what <c>vb6.exe</c> actually writes to <c>/out</c>, and turning its line number into one the
/// editor can put a marker on (#273 task 2.9, #477).
///
/// <para>
/// The format and the line semantics are both MEASURED, not assumed -- nine probes against real
/// <c>vb6.exe</c> 6.00.8176 (the original release), recorded in <c>docs/vb6-fidelity-oracle.md</c> under *What <c>/make</c>
/// writes to <c>/out</c>, and what "Line N" counts*. Both were guessed in the code before that, and both
/// guesses were wrong: the regex matched a C compiler's format, and the line was read as a 1-based file
/// line when it is a 0-based index into the code view.
/// </para>
/// </summary>
public class Vb6CompileLogTests
{
    private static readonly string Dir = Path.Combine(Path.GetTempPath(), "hexide-compile-log-test");

    private static (ProjectDefinition Project, ModuleDefinition Module) AModuleWith(
        string code, ModuleKind kind = ModuleKind.StandardModule, string extension = ".bas")
    {
        var project = TestHelpers.CreateProject("P");
        var module = new ModuleDefinition(project, "Thing", kind)
        {
            AbsolutePath = Path.Combine(Dir, "Thing" + extension),
        };
        module.UpdateCode(code);
        project.AddModule(module);
        return (project, module);
    }

    // ── What the log looks like ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheMeasuredLogBytesParse()
    {
        // Quoted from the oracle, including the leading blank line the log always opens with and the
        // trailer, which must contribute nothing.
        var (project, module) = AModuleWith("Sub Main()\r\n  x = 1\r\nEnd Sub\r\n");
        var log = "\r\nCompile Error in File '" + module.AbsolutePath
                + "', Line 1 : Variable not defined\r\nBuild of 'P.exe' failed.\r\n";

        var found = Vb6ToolchainService.ParseForTests(log, project);

        found.Should().ContainSingle();
        found[0].diagnostic.Message.Should().Be("Variable not defined");
        found[0].uri.Should().Be(LspDocumentUri.ForFile(module.AbsolutePath!));
    }

    [Fact]
    public void ASuccessfulBuildYieldsNothing()
    {
        var (project, _) = AModuleWith("Sub Main()\r\nEnd Sub\r\n");

        Vb6ToolchainService.ParseForTests("\r\nBuild of 'P.exe' succeeded.\r\n", project)
            .Should().BeEmpty();
    }

    [Fact]
    public void AMessageContainingAColonSurvivesWhole()
    {
        // Why the expression anchors on the fixed ` : ` terminator rather than splitting on a colon.
        // "Expected: expression" is a real VB6 message, measured in probes a4/a6/a7.
        var (project, module) = AModuleWith("Dim\r\n");
        var log = $"\r\nCompile Error in File '{module.AbsolutePath}', Line 0 : Expected: expression\r\n";

        Vb6ToolchainService.ParseForTests(log, project)[0].diagnostic.Message
            .Should().Be("Expected: expression");
    }

    [Fact]
    public void APathWithSpacesParenthesesAndAnApostropheParses()
    {
        // The path is delimited by an apostrophe, and a path may contain one. Anchoring on `', Line `
        // rather than on the first apostrophe is what makes this work.
        var project = TestHelpers.CreateProject("P");
        var awkward = Path.Combine(Dir, "Bob's Files (old)", "Thing.bas");
        var module = new ModuleDefinition(project, "Thing", ModuleKind.StandardModule)
        {
            AbsolutePath = awkward,
        };
        module.UpdateCode("x\r\n");
        project.AddModule(module);

        var log = $"\r\nCompile Error in File '{awkward}', Line 0 : Syntax error\r\n";

        Vb6ToolchainService.ParseForTests(log, project).Should().ContainSingle()
            .Which.uri.Should().Be(LspDocumentUri.ForFile(awkward));
    }

    [Fact]
    public void AnErrorInAFileNoLoadedProjectHoldsIsDropped()
    {
        var (project, _) = AModuleWith("Sub Main()\r\nEnd Sub\r\n");
        var log = $"\r\nCompile Error in File '{Path.Combine(Dir, "Somewhere", "Else.bas")}', "
                + "Line 0 : Syntax error\r\n";

        Vb6ToolchainService.ParseForTests(log, project).Should().BeEmpty();
    }

    // ── Which document it belongs to ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ModuleKind.StandardModule, ".bas")]
    [InlineData(ModuleKind.ClassModule, ".cls")]
    [InlineData(ModuleKind.UserControl, ".ctl")]
    [InlineData(ModuleKind.PropertyPage, ".pag")]
    public void EveryKindIsResolved_NotOnlyForms(ModuleKind kind, string extension)
    {
        // It used to match project.Forms only, by file stem -- so a .bas, a .cls, a .ctl and a .pag were
        // all dropped in silence. Invisible, because the regex above them matched nothing either.
        var (project, module) = AModuleWith("x\r\n", kind, extension);
        var log = $"\r\nCompile Error in File '{module.AbsolutePath}', Line 0 : Syntax error\r\n";

        Vb6ToolchainService.ParseForTests(log, project).Should().ContainSingle()
            .Which.uri.Should().Be(LspDocumentUri.ForFile(module.AbsolutePath!));
    }

    [Fact]
    public void AFormWhoseFileIsNotNamedAfterItIsStillResolved()
    {
        // The other half of matching by stem: VB6 does not require a form's file to be named after the
        // form, and the oracle has a section on filenames being free. Resolution is by PATH now, so this
        // is no longer a special case -- which is the point of asserting it.
        var project = TestHelpers.CreateProject("P");
        var form = new FormDefinition(project, FormComponentClass.Instance, "frmMain")
        {
            AbsolutePath = Path.Combine(Dir, "MainWindow.frm"),
        };
        project.AddForm(form);

        var log = $"\r\nCompile Error in File '{form.AbsolutePath}', Line 0 : Syntax error\r\n";

        Vb6ToolchainService.ParseForTests(log, project).Should().ContainSingle()
            .Which.uri.Should().Be(LspDocumentUri.ForFile(form.AbsolutePath!));
    }

    // ── Which line it lands on ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LineZeroIsTheFirstCodeLine()
    {
        // Probe a4: VB6 prints `Line 0` for an error on the first code line, and a6 shows that is a real
        // index rather than a "no line" sentinel. Read as 1-based it would land above the file. The buffer
        // holds the whole file, so the first code line is buffer line 1, below `Attribute VB_Name`.
        var (project, module) = AModuleWith("Dim\r\nSub Main()\r\nEnd Sub\r\n");
        var log = $"\r\nCompile Error in File '{module.AbsolutePath}', Line 0 : Expected: expression\r\n";

        Vb6ToolchainService.ParseForTests(log, project)[0].diagnostic.Range.Start.Line.Should().Be(1);
    }

    [Fact]
    public void ACompileErrorFromVb6()
    {
        // The code-editor delta's scenario, on the case it exists for: a form's designer block, which VB6
        // does not count and the buffer holds.
        //
        // The designer block is six lines and the Attribute line a seventh, none of them counted by VB6. Its
        // `Line 1` is the statement under Form_Load, buffer line 8. Mapped against the code alone it landed
        // on line 2, which is `Caption` in the designer block.
        var project = TestHelpers.CreateProject("P");
        var path = Path.Combine(Dir, "Form1.frm");
        var form = new FormDeserializer().Deserialize(project,
            "VERSION 5.00\r\n"
          + "Begin VB.Form Form1 \r\n"
          + "   Caption         =   \"Form1\"\r\n"
          + "   ClientHeight    =   3000\r\n"
          + "   ClientWidth     =   4000\r\n"
          + "End\r\n"
          + "Attribute VB_Name = \"Form1\"\r\n"
          + "Private Sub Form_Load()\r\n"
          + "    x = \r\n"
          + "End Sub\r\n", Substitute.For<IDeserializeErrorSink>())!;
        form.AbsolutePath = path;
        project.AddForm(form);
        var log = $"\r\nCompile Error in File '{path}', Line 1 : Expected: expression\r\n";

        Vb6ToolchainService.ParseForTests(log, project)[0].diagnostic.Range.Start.Line.Should().Be(8);
    }

    [Fact]
    public void ProcedureAttributeLinesAreSkippedWhenCounting()
    {
        // Probe d1, and the reason this counts rather than adding a per-kind offset. VB6 excludes EVERY
        // Attribute line from its numbering, module-level and procedure-level alike -- but the editor's
        // buffer keeps the procedure-level ones. So a fixed offset is right until a file has one in it,
        // and then every marker below it is a line too high.
        var code = "Sub Main()\r\n"
                 + "Attribute Main.VB_Description = \"go\"\r\n"
                 + "  x = 1\r\n"
                 + "End Sub\r\n";
        var (project, module) = AModuleWith(code);

        // VB6 counts: 0 = "Sub Main()", 1 = "  x = 1" (the Attribute line is not counted).
        var log = $"\r\nCompile Error in File '{module.AbsolutePath}', Line 1 : Variable not defined\r\n";

        Vb6ToolchainService.ParseForTests(log, project)[0].diagnostic.Range.Start.Line
            .Should().Be(3, "buffer line 3 is `  x = 1`, below the module's Attribute VB_Name and the "
                            + "procedure's attribute, neither of which VB6 counted");
    }

    // Measured p11a, p11b, p07c and p07 (#968): attributes belong to logical lines.
    [Theory]
    [InlineData("Attribute Foo.VB_Description = _\r\n\"p11a\"", 6)]
    [InlineData("Attribute _\r\nFoo.VB_Description = \"p11b\"", 6)]
    [InlineData("' a comment _\r\nAttribute Foo.VB_Description = \"p07c\"", 8)]
    public void ContinuedAttributeShapesMapTheMeasuredError(string shape, int printedLine)
    {
        var code = "Option Explicit\r\nPublic Sub Foo()\r\n" + shape
                 + "\r\n    Debug.Print 1\r\nEnd Sub\r\n\r\nPublic Sub Main()\r\n    zz = 1\r\nEnd Sub\r\n";
        var (project, module) = AModuleWith(code);
        var log = $"Compile Error in File '{module.AbsolutePath}', Line {printedLine} : Variable not defined\r\n";

        Vb6ToolchainService.ParseForTests(log, project)[0].diagnostic.Range.Start.Line
            .Should().Be(9);
    }

    [Fact]
    public void AttributeInsideContinuedDeclarationIsCounted()
    {
        var (project, module) = AModuleWith("Option Explicit\r\nPublic Sub Foo(ByVal a As Long, _\r\n"
            + "Attribute Foo.VB_Description = \"p07\"\r\nByVal b As Long)\r\nEnd Sub\r\n");
        var log = $"Compile Error in File '{module.AbsolutePath}', Line 2 : Expected: identifier\r\n";

        Vb6ToolchainService.ParseForTests(log, project)[0].diagnostic.Range.Start.Line
            .Should().Be(3);
    }

    [Fact]
    public void ALineBeyondWhatWeHoldIsClampedRatherThanDropped()
    {
        // The developer is better served by a marker on the last line carrying the compiler's message than
        // by silence about a build that failed.
        var (project, module) = AModuleWith("Sub Main()\r\nEnd Sub\r\n");
        var log = $"\r\nCompile Error in File '{module.AbsolutePath}', Line 99 : Syntax error\r\n";

        var found = Vb6ToolchainService.ParseForTests(log, project);

        found.Should().ContainSingle();
        found[0].diagnostic.Range.Start.Line.Should().BeLessThan(99);
    }
}
