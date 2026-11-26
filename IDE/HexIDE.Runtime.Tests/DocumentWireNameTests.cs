using HexIDE.Runtime.Components;
using HexIDE.Lsp;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Tests;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// What a document is called on the wire (#273 task 2.1). The scheme HexIDE invented for itself,
/// <c>vb6://</c>, is gone: a server that had never heard of it could not open the document, resolve
/// anything relative to it, or say anything useful about it.
///
/// <para>
/// The <c>untitled:</c> spellings asserted here are the ones actually measured against five
/// externally-authored language servers in <see cref="UntitledDocumentNamesTests"/>, including the
/// percent-encoded form. These tests pin the converter to that measurement; those tests establish that a
/// real server accepts it.
/// </para>
/// </summary>
public class DocumentWireNameTests
{
    private static ProjectDefinition Project(string name = "Project1") => TestHelpers.CreateProject(name);

    private static ModuleDefinition Module(
        ProjectDefinition project, string name, ModuleKind kind = ModuleKind.StandardModule,
        string? path = null)
    {
        var module = new ModuleDefinition(project, name, kind) { AbsolutePath = path };
        project.AddModule(module);
        return module;
    }

    [Fact]
    public void AUserControlIsNamedByItsModulesFile_NotItsDesignerHalfs()
    {
        // One file, two halves, one identity — the same rule DocumentIdentity.For(FormDefinition) follows.
        // The two paths genuinely diverge today (#474), so this is not a distinction without a difference.
        var project = Project();
        var modulePath = OperatingSystem.IsWindows() ? @"C:\src\Gauge.ctl" : "/src/Gauge.ctl";
        var formPath = OperatingSystem.IsWindows() ? @"C:\elsewhere\Gauge.frm" : "/elsewhere/Gauge.frm";

        var module = Module(project, "Gauge", ModuleKind.UserControl, modulePath);
        var designer = new FormDefinition(project, FormComponentClass.Instance, "Gauge")
        {
            AbsolutePath = formPath,
        };
        module.UpdateFormPart(designer);

        DocumentWireName.For(designer).Should().Be(LspDocumentUri.ForFile(modulePath));
        DocumentWireName.For(module).Should().Be(LspDocumentUri.ForFile(modulePath));
    }

    [Fact]
    public void RenamingAFormThatHasAFile()
    {
        // lsp-client's scenario of that name: the wire name is the file's, and a rename moves no file, so a server
        // is told nothing and keeps the document it has.
        var path = OperatingSystem.IsWindows() ? @"C:\src\proj\Form1.frm" : "/src/proj/Form1.frm";
        var form = new FormDefinition(Project(), FormComponentClass.Instance, "Form1") { AbsolutePath = path };
        var before = DocumentWireName.For(form);

        form.Components[0].SetProperty(VBProperties.NameProperty, "Renamed");

        form.Name.Should().Be("Renamed", "the premise of this test");
        DocumentWireName.For(form).Should().Be(before).And.Be(LspDocumentUri.ForFile(path));
    }
}
