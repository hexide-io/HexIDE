using System.Text;
using HexIDE.IDE;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// The invariant the rest of hexide-io/HexIDE#273 phase 3 rests on: <b>a document's composed text is the
/// file, byte for byte.</b>
/// </summary>
/// <remarks>
/// <para>
/// Phase 3 makes the code window show the whole file — designer block or module header included, folded and
/// read-only — so that one line number means the same thing to the editor, a language server, the
/// interpreter and the debugger. That only works if composing the two halves reproduces what was read. Every
/// later task (the buffer, the flush, dirty detection, the sidecar shift) assumes it, and none of them would
/// fail visibly if it were false: the file would simply come back subtly different.
/// </para>
///
/// <para>
/// <b>The LF fixtures are the point of this file, not a thoroughness flourish.</b> The code body used to be
/// rebuilt line by line with <c>StringBuilder.AppendLine</c>, which terminates with
/// <c>Environment.NewLine</c> — indistinguishable from the original on Windows and a silent rewrite of every
/// line to LF on Linux, where `build-ide` runs. The save path writes <c>Code</c> back verbatim while the
/// designer half is pinned to CRLF, so a form merely opened and saved on a Linux host came back with mixed
/// terminators. A test that only used CRLF fixtures would be green on this machine and green on CI for the
/// wrong reason; feeding LF input makes the defect reproducible on either.
/// </para>
/// </remarks>
public class WholeFileCompositionTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public List<string> Errors { get; } = [];
        public void LogError(string error) => Errors.Add(error);
    }

    private static FormDefinition Load(string source) =>
        new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink())!;

    /// <summary>A minimal but real UserControl file, CRLF as VB6 writes one.</summary>
    private const string Crlf =
        "VERSION 5.00\r\n" +
        "Begin VB.UserControl Gauge \r\n" +
        "   ClientHeight    =   3600\r\n" +
        "   ClientWidth     =   4800\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Gauge\"\r\n" +
        "Attribute VB_Creatable = True\r\n" +
        "Option Explicit\r\n" +
        "\r\n" +
        "Private Sub UserControl_Resize()\r\n" +
        "End Sub\r\n";

    // ── The invariant ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ComposingTheTwoHalvesReproducesTheFile()
    {
        var form = Load(Crlf);

        FormCodeText.WholeFile(form).Should().Be(Crlf);
    }

    [Fact]
    public void ItReproducesAFileTerminatedWithLf()
    {
        // THE regression. Every line of the body used to come back with Environment.NewLine, so on Windows
        // this returned a body whose LF terminators had become CRLF -- the composed text was four bytes
        // longer than the file it came from. On Linux the same code silently did the reverse to a CRLF file.
        var lf = Crlf.Replace("\r\n", "\n");

        var form = Load(lf);

        FormCodeText.WholeFile(form).Should().Be(lf);
        form.Code.Should().NotContain("\r", "the file had no carriage returns, so neither should its body");
    }

    [Fact]
    public void ItReproducesAFileWithNoTrailingNewline()
    {
        // The other half of rebuilding line by line: AppendLine gave every line a terminator, so a file
        // whose last line had none grew one. Small, silent, and a diff on every save.
        var noTrailer = Crlf.TrimEnd('\r', '\n');

        var form = Load(noTrailer);

        FormCodeText.WholeFile(form).Should().Be(noTrailer);
        form.Code.Should().NotEndWith("\n");
    }

    [Fact]
    public void ItReproducesAFileWithMixedTerminators()
    {
        // Not hypothetical: the design record's own open question notes a saved form can already mix them,
        // because the designer half is pinned to CRLF while typed lines arrive as LF. Whatever produced such
        // a file, reading it must not tidy it -- that would be a change the developer never made.
        var mixed = "VERSION 5.00\r\n"
                  + "Begin VB.UserControl Gauge \n"
                  + "   ClientHeight    =   3600\r\n"
                  + "End\n"
                  + "Attribute VB_Name = \"Gauge\"\r\n"
                  + "Option Explicit\n";

        FormCodeText.WholeFile(Load(mixed)).Should().Be(mixed);
    }

    // ── Where the two halves are cut ──────────────────────────────────────────────────────────────────

    [Fact]
    public void TheDesignerHalfEndsAtTheRootEndAndTheBodyBeginsAtTheAttributeRun()
    {
        // The boundary the whole phase turns on, asserted rather than assumed. Ground truth is
        // corpus/designer/Gauge.ctl, whose root End is line 9 and whose Attribute VB_Name is line 10.
        var form = Load(Crlf);

        form.DesignerText.Should().EndWith("End\r\n");
        form.DesignerText.Should().StartWith("VERSION 5.00");
        form.Code.Should().StartWith("Attribute VB_Name = \"Gauge\"",
            "a VB6-authored form keeps its leading Attribute run inside Code -- prepending it to the "
          + "designer half instead would show it twice, and splitting after it would write a form with no "
          + "VB_Name");
    }

    [Fact]
    public void TheVersionLineSurvivesAlthoughTheParserDiscardsIt()
    {
        // It is dropped at parse and regenerated from a literal on save, so before this change there was no
        // way to know what the file actually said. That is why the text is kept as well as the model.
        Load("VERSION 4.00\r\nBegin VB.Form F \r\nEnd\r\nAttribute VB_Name = \"F\"\r\n")
            .DesignerText.Should().StartWith("VERSION 4.00");
    }

    [Fact]
    public void AFormHexideCreatedComposesToItsCodeAloneUntilSomethingCommits()
    {
        // No file was read, so there is nothing recorded to put in front of the code, and composing
        // "" + Code is right rather than a gap while that lasts: nothing has decided what the file will say.
        // Task 3.4 gives it one on the first committed designer change -- see the sibling below, and the
        // open question in that task about the window in between.
        var form = new FormDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), [], "Form1");

        form.DesignerText.Should().BeNull();
        FormCodeText.WholeFile(form).Should().Be(form.Code);
    }

    [Fact]
    public void AndComposesBothHalvesOnceItHasOne()
    {
        // The other side of the same state, held here rather than only in HeaderRefresherTests because the
        // composition is this file's subject: a form that acquires a header composes exactly like one read
        // from disk, with no special case for where the header came from.
        var form = new FormDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), FormComponentClass.Instance, "Form1");
        var rendered = new FormSerializer().SerializeDesignerText(form, "Form1.frm");
        form.RecordDesignerText(rendered);

        FormCodeText.Prefix(form).Should().Be(rendered);
        FormCodeText.WholeFile(form).Should().Be(rendered + form.Code);
    }

    [Fact]
    public void TheNameAFormWithNoFileRendersAgainstIsTheOneItsFirstSaveWillUse()
    {
        // <Name>.frx in the task's wording is right only for a .frm. The spelling comes from the identity,
        // which resolves a UserControl's designer half to its module and answers the extension that kind is
        // saved with -- the same two pieces DocumentWireName puts after untitled:.
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");

        FormCodeText.RenderFileNameFor(form).Should().Be("Form1.frm");

        var module = new ModuleDefinition(project, "MyControl", ModuleKind.UserControl);
        var part = new FormDefinition(project, [], "MyControl");
        module.UpdateFormPart(part);

        // And a form that HAS a file renders against the FILE name, not its VB6 name. The two diverge as
        // soon as a form is renamed or saved elsewhere, and a companion is cited by the name beside it on
        // disk -- citing the VB6 name would name a .frx that is not there.
        form.AbsolutePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Renamed.frm");
        FormCodeText.RenderFileNameFor(form).Should().Be("Renamed.frm");

        FormCodeText.RenderFileNameFor(part).Should().Be("MyControl.ctl",
            "the companion is then .ctx, which the serializer derives from this");
    }

    // ── And the same cut, made from the other side ────────────────────────────────

    [Fact]
    public void DesignerHalfIsTheRenderMinusTheCode()
    {
        // Three things depend on this and none of them could see it break: the header a save records
        // (FormCodeText.DesignerHalfOf slices by the code's length), the header a committed designer change
        // renders (FormSerializer.SerializeDesignerText passes no code at all), and the split the code
        // window makes. They agree only because WriteCode is a bare Write with no separator in front of it.
        //
        // A separator added there later -- a blank line between the designer block and the code would look
        // like tidying -- moves all three at once and silently: the save would record a header one line
        // short, the next flush would take that line out of the body, and the code window would show the
        // form's Attribute run indented under a stray blank.
        var form = Load(Crlf);
        var serializer = new FormSerializer();

        var (whole, _) = serializer.Serialize(form, "Gauge.ctl");
        var designerHalf = serializer.SerializeDesignerText(form, "Gauge.ctl");

        whole.Should().Be(designerHalf + form.Code);
        FormCodeText.DesignerHalfOf(whole, form.Code).Should().Be(designerHalf);
    }

    [Fact]
    public void ARenderOfAFormWithNoCodeIsAllDesignerHalf()
    {
        // The degenerate case DesignerHalfOf has to get right, because a form HexIDE has just created has
        // an empty Code and is written exactly once -- at creation, where the header is recorded. Slicing
        // [..^0] on an empty code is the whole string, which is correct and easy to write as [..^code.Length]
        // without noticing that it is.
        var form = Load(Crlf);
        form.UpdateCode("");
        var serializer = new FormSerializer();

        var (whole, _) = serializer.Serialize(form, "Gauge.ctl");

        FormCodeText.DesignerHalfOf(whole, form.Code).Should().Be(whole);
        serializer.SerializeDesignerText(form, "Gauge.ctl").Should().Be(whole);
    }

    // ── A reload has to take it too ───────────────────────────────────────────────────────────────────

    [Fact]
    public void AReloadAdoptsTheDesignerTextOfTheFileItJustRead()
    {
        // Without this the code window keeps showing the previous file's header after an external change,
        // and the next save writes it back -- the same class of staleness AdoptFidelityState already exists
        // to prevent for the reproduction verdict.
        var open = Load(Crlf);
        var changed = Crlf.Replace("ClientHeight    =   3600", "ClientHeight    =   9999");

        open.AdoptFidelityState(Load(changed));

        open.DesignerText.Should().Contain("9999").And.NotContain("3600");
    }

    [Fact]
    public void AReloadAdoptsWhetherTheFormCitesCompanionContent()
    {
        // hexide-io/HexIDE#506, found while adding the line above, and it is a data-loss path rather than a
        // staleness one. CitedCompanionBlobCount is the guard on deleting the .frx/.ctx/.pgx: the save path
        // leaves the companion alone when the form cites nothing, because a companion this form never
        // referenced holds bytes that exist nowhere else. A reload that kept a stale non-zero count defeated
        // that guard, so a form whose citations were removed externally deleted its companion on the next
        // ordinary save.
        var citing = Load(Crlf.Replace(
            "   ClientWidth     =   4800\r\n",
            "   Picture         =   \"Gauge.ctx\":0000\r\n"));
        citing.CitedCompanionBlobCount.Should().BeGreaterThan(0, "the fixture must actually cite one");

        citing.AdoptFidelityState(Load(Crlf));

        citing.CitedCompanionBlobCount.Should().Be(0,
            "the file on disk now cites nothing, and the save path reads this to decide whether the "
          + "companion is HexIDE's to delete");
    }

    [Fact]
    public void AReloadAdoptsLockControls()
    {
        // The milder sibling of the above: read from the designer root at load, written back on save, and
        // otherwise frozen at whatever the file said when it was first opened.
        var locked = Load(Crlf.Replace(
            "   ClientWidth     =   4800\r\n",
            "   LockControls    =   -1  'True\r\n"));
        locked.LockControls.Should().BeTrue("the fixture must actually set it");

        locked.AdoptFidelityState(Load(Crlf));

        locked.LockControls.Should().BeFalse();
    }

    // ── The corpus, which is the only ground truth this repository owns ───────────────────────────────

    [Theory]
    [InlineData("Gauge.ctl")]
    [InlineData("GaugeGeneral.pag")]
    public void EveryDesignerCorpusFileComposesBackToItself(string fileName)
    {
        // Real VB6-authored files rather than a fixture written to pass. These two are the only .ctl and
        // .pag this repository owns; everything else with those extensions lives in a VB6 install that CI
        // does not have.
        var dir = FindUpwards(Path.Join("corpus", "designer"));
        dir.Should().NotBeNull("the corpus is the proof -- without it this passes vacuously");

        var path = Path.Join(dir, fileName);
        File.Exists(path).Should().BeTrue();

        var original = Vb6TextFile.Decode(File.ReadAllBytes(path));

        FormCodeText.WholeFile(Load(original)).Should().Be(original);
    }

    [Theory]
    [InlineData("MemberAttributes", "Main.frm")]
    [InlineData("MemberAttributes", "Gauge.ctl")]
    [InlineData("Extras", "Panel.ctl")]
    public void EveryDesignerFileVb6WroteMembersAttributesIntoComposesBackToItself(string project, string fileName)
    {
        // #273 task 4.6: the only designer files anywhere whose code carries members' attribute runs, as VB6's own
        // IDE wrote them. The designer block is split off and the code kept as text, so the runs ride in the code
        // half, the header's own attribute run ahead of them; composing the two halves gives back the file.
        var dir = FindUpwards(Path.Join("corpus", "member-attributes", "round-trip", project));
        dir.Should().NotBeNull("the corpus is the proof -- without it this passes vacuously");

        var path = Path.Join(dir, fileName);
        File.Exists(path).Should().BeTrue();

        var original = Vb6TextFile.Decode(File.ReadAllBytes(path));
        var form = Load(original);

        FormCodeText.WholeFile(form).Should().Be(original);
        form.Code.Should().StartWith("Attribute VB_Name", "the premise: the code half opens with the header's own run")
            .And.Contain(".VB_Description = ", "and carries the members' runs below it");
    }

    private static string? FindUpwards(string folderName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Join(dir.FullName, folderName);
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    // ── A module composes through the same accessor ───────────────────────────────────────────────────

    [Fact]
    public void AClassModuleComposesHeaderPlusBody()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = new ModuleDefinition(project, "Thing", ModuleKind.ClassModule);
        module.RecordOriginalHeader("VERSION 1.0 CLASS\r\nBEGIN\r\nEND\r\nAttribute VB_Name = \"Thing\"\r\n");
        module.UpdateCode("Option Explicit\r\n");

        FormCodeText.WholeFile(module).Should().Be(
            "VERSION 1.0 CLASS\r\nBEGIN\r\nEND\r\nAttribute VB_Name = \"Thing\"\r\nOption Explicit\r\n");
    }

    [Fact]
    public void AUserControlComposesFromItsFormPartNotItsModuleHeader()
    {
        // ModuleFileFormat.HandlesHeader is false for UserControl and PropertyPage, so ToFileContent hands
        // the body straight back for them. Routing those through the FormPart is what stops the accessor
        // quietly returning half a file for the two kinds that have both halves.
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = new ModuleDefinition(project, "Gauge", ModuleKind.UserControl);
        module.UpdateFormPart(Load(Crlf));
        module.UpdateCode("Attribute VB_Name = \"Gauge\"\r\nOption Explicit\r\n");

        FormCodeText.WholeFile(module).Should().StartWith("VERSION 5.00")
            .And.EndWith("Option Explicit\r\n");
    }
}
