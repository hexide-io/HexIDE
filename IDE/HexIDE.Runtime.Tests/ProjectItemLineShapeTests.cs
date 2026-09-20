using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A <c>.vbp</c> item line has two shapes, and VB6 is strict about which key takes which — in opposite
/// directions, which is why one shape applied to all four keys looked reasonable and made every ActiveX
/// control project HexIDE saved unopenable (hexide-io/HexIDE#483).
///
/// <para>
/// Measured with <c>vb6.exe /make</c>, one variable per run, on 2026-09-20; the log lines are quoted in
/// <c>docs/vb6-fidelity-oracle.md</c>:
/// </para>
///
/// <list type="table">
///   <item><term><c>UserControl=Gauge.ctl</c></term><description>builds</description></item>
///   <item><term><c>UserControl=Gauge; Gauge.ctl</c></term><description>"File not found: 'Gauge; Gauge.ctl'"</description></item>
///   <item><term><c>PropertyPage=GaugeGeneral; GaugeGeneral.pag</c></term><description>the same, for its own file</description></item>
///   <item><term><c>Form=Form1.frm</c></term><description>builds; <c>Form=Form1; Form1.frm</c> does not</description></item>
///   <item><term><c>Module=Mod1; Mod1.bas</c></term><description>builds</description></item>
///   <item><term><c>Module=Mod1.bas</c></term><description>"The project file … is corrupt, and can't be loaded"</description></item>
///   <item><term><c>Class=Cls1.cls</c></term><description>the same; <c>Class=Cls1; Cls1.cls</c> builds</description></item>
/// </list>
///
/// <para>
/// So the split is designer-file against code-file, across all five keys, and each family rejects the
/// other's shape in its own way — one as a missing file, one as a corrupt project.
/// </para>
///
/// <para>
/// <c>corpus/designer/</c> covers this end to end and is what found it. These fixtures are the narrow
/// version: they hold whether or not a corpus is present, and they name the rule rather than a byte count.
/// </para>
/// </summary>
public class ProjectItemLineShapeTests
{
    private static readonly string ProjectDir = Path.Combine(Path.GetTempPath(), "hexide-itemline-fixture");

    private static string Write(ModuleKind kind, string fileName)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P")
        {
            AbsolutePath = Path.Combine(ProjectDir, "P.vbp"),
        };
        project.AddModule(new ModuleDefinition(project, "Gauge", kind)
        {
            AbsolutePath = Path.Combine(ProjectDir, fileName),
        });
        return new ProjectSerializer().Serialize(project, project.AbsolutePath!);
    }

    [Theory]
    [InlineData(ModuleKind.UserControl, "Gauge.ctl", "UserControl=Gauge.ctl")]
    [InlineData(ModuleKind.PropertyPage, "Gauge.pag", "PropertyPage=Gauge.pag")]
    public void ADesignerModuleIsWrittenAsABarePath(ModuleKind kind, string fileName, string expected)
    {
        Write(kind, fileName).Should().Contain(expected + "\r\n")
            .And.NotContain("Gauge; ", "VB6 reads the whole value as a filename on these two keys");
    }

    [Theory]
    [InlineData(ModuleKind.StandardModule, "Gauge.bas", "Module=Gauge; Gauge.bas")]
    [InlineData(ModuleKind.ClassModule, "Gauge.cls", "Class=Gauge; Gauge.cls")]
    public void ACodeModuleIsWrittenAsNameThenPath(ModuleKind kind, string fileName, string expected)
    {
        Write(kind, fileName).Should().Contain(expected + "\r\n",
            "VB6 calls the project file corrupt when the name is missing from these two keys");
    }
}
