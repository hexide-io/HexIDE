using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A written <c>.vbp</c> must end with a line terminator.
///
/// <para>
/// Measured against real vb6.exe (see <i>What a .vbp tolerates</i> in docs/vb6-fidelity-oracle.md): an
/// ITEM line — <c>Module=</c>, <c>RelatedDoc=</c> — as the file's last line with <b>no trailing CRLF</b>
/// kills the compiler with <c>0xC0000005</c> and writes <b>nothing</b> to the <c>/out</c> log. Adding the
/// terminator makes the byte-identical file legal. A final unterminated <c>Reference=</c> or <c>Name=</c>
/// is fine, so it is specific to item lines.
/// </para>
///
/// <para>
/// <b>Why this is a test and not a comment.</b> <c>ProjectSerializer</c> is not exposed today, but only
/// by ordering luck: every known line goes through <c>WriteLine</c> and the last one happens to be
/// <c>Name=</c>. Move an item line to the end, or append anything after the extension tail, and the
/// failure mode is a silent process kill with an empty error log — which is the least diagnosable bug a
/// serializer can have. Cheap to pin now, very expensive to find later.
/// </para>
/// </summary>
public class ProjectFileTerminatorTests
{
    private static ProjectDefinition ProjectWithAModule()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Proj");
        project.AddModule(new ModuleDefinition(project, "Module1", ModuleKind.StandardModule));
        return project;
    }

    [Fact]
    public void AWrittenProjectFileEndsWithACrLf()
    {
        var text = new ProjectSerializer().Serialize(ProjectWithAModule(), @"C:\proj\Proj.vbp");

        text.Should().EndWith("\r\n",
            "an item line as the last line WITHOUT a terminator crashes vb6.exe at 0xC0000005 with an "
          + "empty error log — so the terminator is a correctness requirement, not tidiness");
    }

    [Fact]
    public void AnItemLineIsNeverTheLastLineOfTheFile()
    {
        // THE invariant, and it is narrower than "the file ends with CRLF". An extension tail is
        // preserved VERBATIM — that is the round-trip contract, and a source .vbp that ended without a
        // terminator must come back without one. Demanding a terminator there would trade a byte-for-byte
        // guarantee for a safety property VB6 does not need in that position: the tail lives in the region
        // ignored from the first '[', so it can never BE an item line.
        //
        // What actually has to hold is that no Module=/RelatedDoc= line is last. It holds today because
        // Name= is always emitted after them; this is what would catch a reordering that changed it.
        var project = ProjectWithAModule();
        project.ExtensionTail = "[CustomTool]\r\nSetting=1";   // deliberately unterminated

        var text = new ProjectSerializer().Serialize(project, @"C:\proj\Proj.vbp");

        var lastLine = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[^1];
        lastLine.Should().NotStartWith("Module=").And.NotStartWith("RelatedDoc=").And.NotStartWith("Form=",
            "an item line last with no terminator crashes vb6.exe at 0xC0000005 with an empty error log");
    }

    [Fact]
    public void EveryLineOfAWrittenProjectFileIsCrLfTerminated()
    {
        // VB6 will not load an LF-terminated file at all — the same reason the oracle harness writes
        // every module CRLF+ASCII. A lone LF anywhere is the bug this catches before it reaches disk.
        var text = new ProjectSerializer().Serialize(ProjectWithAModule(), @"C:\proj\Proj.vbp");

        text.Replace("\r\n", "").Should().NotContain("\n", "no line may be terminated with a bare LF");
    }
}
