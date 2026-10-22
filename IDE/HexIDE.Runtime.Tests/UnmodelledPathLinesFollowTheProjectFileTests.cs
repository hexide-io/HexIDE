using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.IDE;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// The paths in project-file lines HexIDE does not model are written from wherever the project file goes, where VB6's
/// own Save Project As writes them so (#1406). They were kept as read, so after a Save As to another folder a designer,
/// a resource file and a build folder were named beside the new project file.
/// </summary>
/// <remarks>
/// <para>
/// Measured on the oracle: VB6's Save Project As, from a project in <c>proj</c> into <c>proj\deeper</c>, wrote the
/// <c>Saved.vbp</c> lines these tests expect. <c>Designer=..\AddIn.Dsr</c>, <c>ResFile32="..\res\probe.res"</c> and
/// <c>Path32="..\out"</c> are rebased; <c>HelpFile="help\probe.hlp"</c> is left as typed. Saved into a sibling folder
/// instead, it wrote <c>..\proj\…</c> for each of the three.
/// </para>
/// <para>
/// A reference's path VB6 re-derives from where the type library is registered; HexIDE, which cannot read that on every
/// host, keeps the file's own path and names it from the new folder, which is the same path wherever the library has not
/// moved. Expectations are project-file text, so they are written with backslashes; the folders are host paths.
/// </para>
/// </remarks>
public class UnmodelledPathLinesFollowTheProjectFileTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string error) { }
    }

    private const string ProjectFile =
        "Type=OleDll\r\n" +
        "Reference=*\\G{00020430-0000-0000-C000-000000000046}#2.0#0#..\\Windows\\SysWOW64\\stdole2.tlb#OLE Automation\r\n" +
        "Reference=*\\G{EF404E00-EDA6-101A-8DAF-00DD010F7EBB}#5.3#0#C:\\Program Files (x86)\\VB98\\VB6EXT.OLB#Microsoft Visual Basic 6.0 Extensibility\r\n" +
        "Designer=AddIn.Dsr\r\n" +
        "Startup=\"(None)\"\r\n" +
        "ResFile32=\"res\\probe.res\"\r\n" +
        "HelpFile=\"help\\probe.hlp\"\r\n" +
        "Command32=\"\"\r\n" +
        "ExeName32=\"Probe.dll\"\r\n" +
        "Name=\"MyAddIn\"\r\n" +
        "Path32=\"out\"\r\n" +
        "HelpContextID=\"0\"\r\n";

    private static async Task<string[]> SavedTo(params string[] where)
    {
        var dir = Path.Combine(Path.GetTempPath(), "hexide-pathlines-" + Guid.NewGuid().ToString("N"));
        var proj = Path.Combine(dir, "proj");
        Directory.CreateDirectory(proj);
        try
        {
            var projectPath = Path.Combine(proj, "Probe.vbp");
            await File.WriteAllTextAsync(projectPath, ProjectFile, TestContext.Current.CancellationToken);
            var project = (await new ProjectLoader().LoadAsync(projectPath, new NullSink())).Project;
            return new ProjectSerializer().Serialize(project, Path.Combine([dir, .. where])).Split("\r\n");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Saved_a_folder_deeper_each_rebased_path_is_named_from_there()
    {
        var lines = await SavedTo("proj", "deeper", "Saved.vbp");

        lines.Should().Contain([
            @"Designer=..\AddIn.Dsr",
            @"ResFile32=""..\res\probe.res""",
            @"Path32=""..\out""",
            @"Reference=*\G{00020430-0000-0000-C000-000000000046}#2.0#0#..\..\Windows\SysWOW64\stdole2.tlb#OLE Automation",
        ]);
    }

    [Fact]
    public async Task Saved_to_a_sibling_folder_each_rebased_path_is_named_from_there()
    {
        var lines = await SavedTo("other", "Saved2.vbp");

        lines.Should().Contain([@"Designer=..\proj\AddIn.Dsr", @"ResFile32=""..\proj\res\probe.res""", @"Path32=""..\proj\out"""]);
    }

    [Fact]
    public async Task HelpFile_and_a_rooted_reference_are_written_as_they_were_read()
    {
        var lines = await SavedTo("proj", "deeper", "Saved.vbp");

        lines.Should().Contain(@"HelpFile=""help\probe.hlp""", "VB6's Save As left it as typed")
            .And.Contain(@"Reference=*\G{EF404E00-EDA6-101A-8DAF-00DD010F7EBB}#5.3#0#C:\Program Files (x86)\VB98\VB6EXT.OLB#Microsoft Visual Basic 6.0 Extensibility");
    }

    [Fact]
    public async Task Saved_where_it_was_read_every_line_is_as_it_was()
    {
        var lines = await SavedTo("proj", "Probe.vbp");

        lines.Should().Contain([
            "Designer=AddIn.Dsr", @"ResFile32=""res\probe.res""", @"Path32=""out""", @"HelpFile=""help\probe.hlp""",
            @"Reference=*\G{00020430-0000-0000-C000-000000000046}#2.0#0#..\Windows\SysWOW64\stdole2.tlb#OLE Automation",
        ]);
    }
}
