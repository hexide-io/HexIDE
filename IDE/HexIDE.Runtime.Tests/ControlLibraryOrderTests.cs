using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.IDE;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Tests;

// Measured native and p-code: oracle/in-box-classes-a-bare-name-binds-to-the-first-library.md (#2265).
public class ControlLibraryOrderTests : BaseVBTestFixture
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Object_and_Reference_lines_bind_in_read_order(bool referenceFirst, bool componentIsSix)
    {
        var component = componentIsSix ? ControlLibraries.MSComctlLib : ControlLibraries.ComctlLib;
        var reference = componentIsSix ? ControlLibraries.ComctlLib : ControlLibraries.MSComctlLib;
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.KnownLineKeys.AddRange(["Type", "Reference", "Name"]);
        project.SetReferences([new(reference.Guid, reference.Version, 0, "", reference.Name)]);
        project.UnknownPreSectionLines.Add((referenceFirst ? 2 : 1, component.ProjectLine));
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");
        form.HeaderLines.Add(reference.FormLine);
        form.HeaderLines.Add(component.FormLine);
        project.AddForm(form);

        var libraries = RuntimeLibraries.ReferencedBy(project);
        libraries.Should().Equal(referenceFirst ? [reference, component] : [component, reference]);
        context.AllocVariable(rootEnv, "componentBar", new Vb6Value((Avalonia.Controls.Control)new VBProgressBar { Library = component }));
        context.AllocVariable(rootEnv, "referenceBar", new Vb6Value((Avalonia.Controls.Control)new VBProgressBar { Library = reference }));
        var interpreter = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, """
            Dim pb As ProgressBar
            Debug.Print TypeName(pb)
            Debug.Print TypeOf componentBar Is ProgressBar, TypeOf referenceBar Is ProgressBar
            On Error Resume Next
            Set pb = componentBar
            Debug.Print Err.Number
            Err.Clear
            Set pb = referenceBar
            Debug.Print Err.Number
            """);
        interpreter.SetAppInfo(new AppInfo(ControlLibraries: libraries));
        interpreter.ControlClassFor("ProgressBar").Should().Be((referenceFirst ? reference : component).TypeName("ProgressBar"));
        interpreter.ControlClassFor("MSComctlLib.ProgressBar").Should().Be("MSComctlLib.ProgressBar");
        interpreter.ControlClassFor("ComctlLib.ProgressBar").Should().Be("ComctlLib.ProgressBar");
        await interpreter.Execute();
        debug.Select(value => value.Value?.ToString()).Should().Equal("Nothing",
            (!referenceFirst).ToString(), referenceFirst.ToString(),
            referenceFirst ? "13" : "0", referenceFirst ? "0" : "13");
    }

    [Fact]
    public void Object_lines_are_sorted_by_their_read_slots_before_form_fallbacks()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.KnownLineKeys.AddRange(["Type", "Name"]);
        project.UnknownPreSectionLines.Add((2, ControlLibraries.MSComctlLib.ProjectLine));
        project.UnknownPreSectionLines.Add((1, ControlLibraries.ComctlLib.ProjectLine));
        RuntimeLibraries.ReferencedBy(project).Select(l => l.Name).Should().Equal("ComctlLib", "MSComctlLib");
    }
}
