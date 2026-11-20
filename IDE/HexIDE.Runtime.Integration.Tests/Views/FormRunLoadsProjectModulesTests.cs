using System.Linq;
using System.Threading;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Views;

/// <summary>
/// The IDE's real form-run entry point must hand the project's modules to the interpreter
/// (hexide-io/HexIDE#220).
///
/// <para>
/// <b>Deliberately at <see cref="VBLoader.RunForm"/> rather than one layer down.</b> The unit guard in
/// <c>FormRunModuleVisibilityTests</c> proves <c>SetCode</c> forwards what it is given, and proves the
/// gathering picks the right module kinds — but the defect lived in neither. It lived in the two lines
/// where <c>RunForm</c> simply never asked for the modules, and "the compiler checks that" is precisely the
/// reasoning under which this shipped. So this test starts where F5 starts.
/// </para>
/// </summary>
public class FormRunLoadsProjectModulesTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    private const string MinimalForm =
        "VERSION 5.00\r\n"
      + "Begin VB.Form Form1 \r\n"
      + "   Caption         =   \"Form1\"\r\n"
      + "   ClientHeight    =   3000\r\n"
      + "   ClientWidth     =   4000\r\n"
      + "End\r\n"
      + "Attribute VB_Name = \"Form1\"\r\n";

    /// <summary>A project holding one form plus the named modules, wired as the IDE would have it.</summary>
    private static FormDefinition ProjectWithForm(params (string Name, ModuleKind Kind)[] modules)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, MinimalForm, NullSink.Instance)!;
        project.AddForm(form);

        foreach (var (name, kind) in modules)
        {
            var module = new ModuleDefinition(project, name, kind);
            module.UpdateCode($"Public Sub {name}Thing()\r\nEnd Sub\r\n");
            project.AddModule(module);
        }
        return form;
    }

    [AvaloniaFact]
    public void RunningAFormLoadsTheProjectsStandardModules()
    {
        // The whole issue in one assertion: before this, pressing F5 on a form gave an interpreter that held
        // the form and nothing else, so every call into a .bas failed at run time on a project VB6 runs.
        var form = ProjectWithForm(("Helpers", ModuleKind.StandardModule), ("Utils", ModuleKind.StandardModule));

        VBLoader.RunForm(form, CancellationToken.None, out var window);

        window.Context.Interpreter!.Modules.All.Select(m => m.Name)
            .Should().BeEquivalentTo(["Form1", "Helpers", "Utils"]);
        window.Close();
    }

    [AvaloniaFact]
    public void RunningAFormRegistersTheProjectsClassModules()
    {
        var form = ProjectWithForm(("Widget", ModuleKind.ClassModule));

        VBLoader.RunForm(form, CancellationToken.None, out var window);

        window.Context.Interpreter!.Modules.TryGet("Widget", out var widget).Should().BeTrue(
            "New Widget from a form has to resolve the name");
        widget.Kind.Should().Be(InterpreterModuleKind.Class,
            "a class module is a template — registered so New resolves it, never run at startup");
        window.Close();
    }

    [AvaloniaFact]
    public void RunningAFormDoesNotLoadComponentBackedModules()
    {
        // A UserControl has code, but it is instantiated through the component model with its own execution
        // context. Loading it here as a free-standing module would run its declarations twice.
        var form = ProjectWithForm(("Gauge", ModuleKind.UserControl), ("Helpers", ModuleKind.StandardModule));

        VBLoader.RunForm(form, CancellationToken.None, out var window);

        window.Context.Interpreter!.Modules.Contains("Gauge").Should().BeFalse();
        window.Context.Interpreter!.Modules.Contains("Helpers").Should().BeTrue();
        window.Close();
    }

    // #5042, oracle PR #5053, rows U1, A2 and A1.
    [AvaloniaTheory]
    [InlineData(VBProjectType.EXE, false)]
    [InlineData(VBProjectType.Control, false)]
    [InlineData(VBProjectType.Control, true)]
    public void A_UserControls_public_type_depends_on_its_exposure(VBProjectType projectType, bool exposed)
    {
        var project = new ProjectDefinition(projectType, "P");
        var form = new FormDeserializer().Deserialize(project, MinimalForm, NullSink.Instance)!;
        project.AddForm(form);
        var control = new ModuleDefinition(project, "Gauge", ModuleKind.UserControl);
        control.UpdateFormPart(new FormDeserializer().Deserialize(project,
            "VERSION 5.00\r\nBegin VB.UserControl Gauge\r\nClientWidth = 1200\r\nEnd\r\nAttribute VB_Name = \"Gauge\"\r\n",
            NullSink.Instance)!);
        control.UpdateCode($"Attribute VB_Name = \"Gauge\"\r\nAttribute VB_Exposed = {exposed}\r\n"
            + "Public Type T\r\na(1 To 3) As Long\r\nEnd Type\r\nPrivate x As T\r\n");
        project.AddModule(control);

        var load = () => new VBProgram(form, new VBWindowContext(new UserControlKeyTests.Library()), null, null);
        if (exposed)
            load().Interpreter.Modules.Contains("Gauge").Should().BeTrue();
        else
            load.Should().Throw<VBCompileErrorException>().Which.Message.Should()
                .EndWith("Cannot define a Public user-defined type within a private object module");
    }

    [AvaloniaFact]
    public void AFormWithNoOtherModulesStillRuns()
    {
        // The control: the fix must not make a lone form depend on there being modules to load.
        var form = ProjectWithForm();

        VBLoader.RunForm(form, CancellationToken.None, out var window);

        window.Context.Interpreter!.Modules.All.Select(m => m.Name).Should().BeEquivalentTo(["Form1"]);
        window.Close();
    }
}
