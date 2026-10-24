using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Every recognised line of a written <c>.vbp</c> must end with CRLF.
///
/// <para>
/// Measured against real vb6.exe (see <i>What a .vbp tolerates</i> in docs/vb6-fidelity-oracle.md): an
/// ITEM line — <c>Module=</c>, <c>RelatedDoc=</c> — as the file's last line with <b>no trailing CRLF</b>
/// kills the compiler with <c>0xC0000005</c> and writes <b>nothing</b> to the <c>/out</c> log. Adding the
/// terminator makes the byte-identical file legal. A final unterminated <c>Reference=</c> or <c>Name=</c>
/// is fine, so it is specific to item lines.
/// </para>
///
/// Every recognised line is CRLF-terminated, including an item retained as the last recognised line.
/// </summary>
public class ProjectFileTerminatorTests
{
    private static ProjectDefinition ProjectWithAModule()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Proj");
        project.AddModule(new ModuleDefinition(project, "Module1", ModuleKind.StandardModule)
        {
            AbsolutePath = Path.Combine(Path.GetTempPath(), "Module1.bas"),
        });
        return project;
    }

    [Fact]
    public void AWrittenProjectFileEndsWithACrLf()
    {
        var text = new ProjectSerializer().Serialize(ProjectWithAModule(), Path.Combine(Path.GetTempPath(), "Proj.vbp"));

        text.Should().EndWith("\r\n",
            "an item line as the last line WITHOUT a terminator crashes vb6.exe at 0xC0000005 with an "
          + "empty error log — so the terminator is a correctness requirement, not tidiness");
    }

    [Fact]
    public async Task An_item_in_the_last_read_slot_is_still_CrLf_terminated()
    {
        var dir = Path.Join(Path.GetTempPath(), "hexide-terminator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Join(dir, "Proj.vbp");
        const string original = "Type=Exe\r\nName=\"Proj\"\r\nModule=Module1; Module1.bas\r\n";
        try
        {
            await File.WriteAllTextAsync(path, original, TestContext.Current.CancellationToken);
            var loaded = await new ProjectLoader().LoadAsync(path, new NullSink());
            var text = new ProjectSerializer().Serialize(loaded.Project, path);
            text.Should().Be(original).And.EndWith("Module=Module1; Module1.bas\r\n");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string error) { }
    }

    [Fact]
    public void EveryLineOfAWrittenProjectFileIsCrLfTerminated()
    {
        // VB6 will not load an LF-terminated file at all — the same reason the oracle harness writes
        // every module CRLF+ASCII. A lone LF anywhere is the bug this catches before it reaches disk.
        var text = new ProjectSerializer().Serialize(ProjectWithAModule(), Path.Combine(Path.GetTempPath(), "Proj.vbp"));

        text.Replace("\r\n", "").Should().NotContain("\n", "no line may be terminated with a bare LF");
    }
}
