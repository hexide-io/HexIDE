using System.IO;
using HexIDE.IDE;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.IDE;

/// <summary>
/// Covers the classification paths reachable without constructing editor/designer view-models
/// (not-open files). The open-editor / designer paths are exercised by the integration tests.
/// </summary>
public class DirtyDetectorTests
{
    private static string P(string name) => Path.Combine(Path.GetTempPath(), "hexide-dirty-tests", name);

    private static ModuleDefinition Module(string code)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Proj");
        var module = new ModuleDefinition(project, "Mod1", ModuleKind.StandardModule);
        module.UpdateCode(code);
        return module;
    }

    [Fact]
    public void NotOpenModule_ModelMatchesBaseline_IsCleanReload()
    {
        var baseline = new FileBaselineStore();
        var path = P("a.bas");
        var module = Module("Public Sub Foo()\nEnd Sub");
        // The on-disk baseline is the full file (header + body); .bas/.cls keep the header out of Code,
        // so reconstruct the disk form to model "disk == model, no unsaved edits".
        baseline.Record(path, ModuleFileFormat.ToFileContent(module.Code, module.Name, module.Kind));

        var sut = new DirtyDetector(baseline);
        sut.Classify(new WatchedFileTarget(path, null, module, null, null))
            .Should().Be(ReloadDecision.CleanReload);
    }

    [Fact]
    public void NotOpenModule_ModelDiffersFromBaseline_IsConflict()
    {
        var baseline = new FileBaselineStore();
        var path = P("a.bas");
        baseline.Record(path, "Public Sub OnDisk()\nEnd Sub"); // last-known disk
        var module = Module("Public Sub EditedInIde()\nEnd Sub"); // unsaved in-memory edits

        var sut = new DirtyDetector(baseline);
        sut.Classify(new WatchedFileTarget(path, null, module, null, null))
            .Should().Be(ReloadDecision.Conflict);
    }

    [Fact]
    public void NotOpenModule_NoBaseline_IsIndeterminate()
    {
        var baseline = new FileBaselineStore();
        var module = Module("Public Sub Foo()\nEnd Sub");

        var sut = new DirtyDetector(baseline);
        sut.Classify(new WatchedFileTarget(P("a.bas"), null, module, null, null))
            .Should().Be(ReloadDecision.Indeterminate);
    }

    [Fact]
    public void NotOpenForm_IsIndeterminate()
    {
        var baseline = new FileBaselineStore();
        var project = new ProjectDefinition(VBProjectType.EXE, "Proj");
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");

        var sut = new DirtyDetector(baseline);
        sut.Classify(new WatchedFileTarget(P("Form1.frm"), form, null, null, null))
            .Should().Be(ReloadDecision.Indeterminate);
    }
}
