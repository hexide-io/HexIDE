using System.IO;
using HexIDE.IDE;
using HexIDE.Projects;
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

        var sut = new DirtyDetector(baseline, Substitute.For<IProjectService>());
        sut.Classify(new WatchedFileTarget(path, null, module, null, null))
            .Should().Be(ReloadDecision.CleanReload);
    }

    [Theory]
    [InlineData(ModuleKind.StandardModule, 1252, false, "café")]
    [InlineData(ModuleKind.ClassModule, 1252, false, "café")]
    [InlineData(ModuleKind.StandardModule, 1252, false, "€")]
    [InlineData(ModuleKind.ClassModule, 1252, false, "€")]
    [InlineData(ModuleKind.StandardModule, 65001, true, "café")]
    [InlineData(ModuleKind.ClassModule, 65001, true, "café")]
    public void NotOpenModule_EncodedModelMatchesByteBaseline_IsCleanReload(
        ModuleKind kind, int codePage, bool bom, string comment)
    {
        var module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "Proj"), "Mod1", kind)
        {
            TextEncoding = new Vb6TextEncoding(codePage, bom)
        };
        module.UpdateCode($"' {comment}\r\nPublic Sub Foo()\r\nEnd Sub");
        var path = P("encoded.bas");
        var baseline = new FileBaselineStore();
        baseline.Record(path, Vb6TextFile.Encode(
            ModuleFileFormat.ToFileContent(module.Code, module.Name, module.Kind), module.TextEncoding));

        var sut = new DirtyDetector(baseline, Substitute.For<IProjectService>());
        sut.Classify(new WatchedFileTarget(path, null, module, null, null))
            .Should().Be(ReloadDecision.CleanReload);
    }

    [Theory]
    [InlineData(ModuleKind.UserControl, false)]
    [InlineData(ModuleKind.PropertyPage, false)]
    [InlineData(ModuleKind.UserControl, true)]
    [InlineData(ModuleKind.PropertyPage, true)]
    public void NotOpenDesignerModule_UsesRenderComparison(ModuleKind kind, bool dirty)
    {
        var module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "Proj"), "Control1", kind);
        var path = P("Control1.ctl");
        var baseline = new FileBaselineStore();
        // Deliberately disagree with the render verdict: raw bytes cannot answer this question.
        baseline.Record(path, dirty ? module.Code : "designer block and code");
        var projectService = Substitute.For<IProjectService>();
        projectService.HasUnsavedChanges(module).Returns(dirty);

        var sut = new DirtyDetector(baseline, projectService);
        sut.Classify(new WatchedFileTarget(path, null, module, null, null))
            .Should().Be(dirty ? ReloadDecision.Conflict : ReloadDecision.CleanReload);
    }

    [Fact]
    public void NotOpenModule_ModelDiffersFromBaseline_IsConflict()
    {
        var baseline = new FileBaselineStore();
        var path = P("a.bas");
        baseline.Record(path, "Public Sub OnDisk()\nEnd Sub"); // last-known disk
        var module = Module("Public Sub EditedInIde()\nEnd Sub"); // unsaved in-memory edits

        var sut = new DirtyDetector(baseline, Substitute.For<IProjectService>());
        sut.Classify(new WatchedFileTarget(path, null, module, null, null))
            .Should().Be(ReloadDecision.Conflict);
    }

    [Fact]
    public void NotOpenModule_NoBaseline_IsIndeterminate()
    {
        var baseline = new FileBaselineStore();
        var module = Module("Public Sub Foo()\nEnd Sub");

        var sut = new DirtyDetector(baseline, Substitute.For<IProjectService>());
        sut.Classify(new WatchedFileTarget(P("a.bas"), null, module, null, null))
            .Should().Be(ReloadDecision.Indeterminate);
    }

    // ── not-open forms (issue #23) ────────────────────────────────────────────
    // A .frm can't be hashed against an editor buffer, so a not-open form is classified by asking the
    // project service whether the model has unsaved edits. Skipping instead left the cached model stale,
    // and SaveProject — which writes every form unconditionally — then wrote it back over the external
    // change, silently discarding pulled work.

    private static FormDefinition Form()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Proj");
        return new FormDefinition(project, FormComponentClass.Instance, "Form1");
    }

    [Fact]
    public void NotOpenForm_ModelHasNoUnsavedEdits_IsCleanReload()
    {
        var form = Form();
        var projectService = Substitute.For<IProjectService>();
        projectService.HasUnsavedChanges(form).Returns(false);

        var sut = new DirtyDetector(new FileBaselineStore(), projectService);
        sut.Classify(new WatchedFileTarget(P("Form1.frm"), form, null, null, null))
            .Should().Be(ReloadDecision.CleanReload);
    }

    [Fact]
    public void NotOpenForm_ModelHasUnsavedEdits_IsConflict()
    {
        var form = Form();
        var projectService = Substitute.For<IProjectService>();
        projectService.HasUnsavedChanges(form).Returns(true);

        var sut = new DirtyDetector(new FileBaselineStore(), projectService);
        sut.Classify(new WatchedFileTarget(P("Form1.frm"), form, null, null, null))
            .Should().Be(ReloadDecision.Conflict);
    }
}
