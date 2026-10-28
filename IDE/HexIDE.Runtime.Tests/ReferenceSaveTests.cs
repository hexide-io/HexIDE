using System.Linq;
using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

public class ReferenceSaveTests
{
    [Theory]
    [InlineData(@"C:\Windows\SysWOW64\msi.dll", @"C:\work\probe", @"..\..\Windows\SysWOW64\msi.dll")]
    [InlineData(@"X:\HexIde2274D.tlb", @"C:\work\probe", @"X:\HexIde2274D.tlb")]
    [InlineData(@"\\localhost\hexoracle2274\HexIde2274U.tlb", @"C:\work\probe", @"\\localhost\hexoracle2274\HexIde2274U.tlb")]
    [InlineData("", @"C:\work\probe", "")]
    public void Reference_paths_follow_the_measured_save_rules(string path, string folder, string expected)
    {
        SerializedProject.ReferencePath(path, folder, folder).Should().Be(expected);
    }

    [Fact]
    public void Writer_saves_same_drive_relative_and_other_drive_absolute_on_every_host()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Probe");
        project.AddReference(new VbReference("{00020430-0000-0000-C000-000000000046}", "2.0", 0,
            @"C:\Windows\SysWOW64\stdole2.tlb", "OLE Automation"));
        project.AddReference(new VbReference("{11111111-0000-0000-0000-000000000001}", "1.0", 0,
            @"X:\HexIde2274D.tlb", "Other drive"));

        new ProjectSerializer().Serialize(project, @"C:\a\b\Probe.vbp").Split("\r\n")
            .Where(l => l.StartsWith("Reference=")).Should().Equal(
                @"Reference=*\G{00020430-0000-0000-C000-000000000046}#2.0#0#..\..\Windows\SysWOW64\stdole2.tlb#OLE Automation",
                @"Reference=*\G{11111111-0000-0000-0000-000000000001}#1.0#0#X:\HexIde2274D.tlb#Other drive");
    }

    [Fact]
    public void An_absolute_reference_is_relative_in_the_serialized_project()
    {
        var folder = Path.Combine(Path.GetTempPath(), "hexide-reference-save");
        var project = new ProjectDefinition(VBProjectType.EXE, "Probe") { ItemLinesDirectory = folder };
        project.AddReference(new VbReference("{00020430-0000-0000-C000-000000000046}", "2.0", 0,
            Path.Combine(folder, "lib", "probe.tlb"), "Probe library"));

        new ProjectSerializer().Serialize(project, Path.Combine(folder, "Probe.vbp"))
            .Should().Contain(@"#2.0#0#lib\probe.tlb#Probe library");
    }

    [Fact]
    public void Save_after_References_OK_moves_references_and_read_objects_before_members()
    {
        var folder = Path.Combine(Path.GetTempPath(), "hexide-reference-save");
        var project = new ProjectDefinition(VBProjectType.EXE, "Probe") { ItemLinesDirectory = folder };
        project.ReferencesDialogAccepted = true;
        project.KnownLineKeys.AddRange(["Type", "Form", "Reference", "Name"]);
        project.PreserveItemLine("Form=Missing.frm", 1);
        project.UnknownPreSectionLines.Add((0, "Object=read"));
        project.UnknownPreSectionLines.Add((ProjectDefinition.AfterFormsHint, "Object=added"));
        project.AddReference(new VbReference("{00020430-0000-0000-C000-000000000046}", "2.0", 0, "", "OLE Automation"));

        new ProjectSerializer().Serialize(project, Path.Combine(folder, "Probe.vbp")).Split("\r\n")
            .Where(l => l.Length > 0).Should().Equal("Type=Exe",
                @"Reference=*\G{00020430-0000-0000-C000-000000000046}#2.0#0##OLE Automation",
                "Object=read", "Form=Missing.frm", "Object=added", "Name=\"Probe\"");
    }
}
