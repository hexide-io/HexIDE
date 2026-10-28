using Avalonia;
using Avalonia.Controls;
using HexIDE.Runtime;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

public class FormTerminationOrderTests : BaseVBTestFixture
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string message) => throw new InvalidOperationException(message);
    }

    private sealed class View(IBasicStandardLibrary library) : IFormView
    {
        public VBWindowContext Context { get; } = new(library);
        public bool IsShown => false;
        public (Size Size, WindowState State) Extent => (default, WindowState.Normal);
        public IFormView? HeldBy { get; set; }
        public void SetContent(Control content) { }
        public void ShowForm() { }
        public void ActivateForm() { }
        public void HideForm() { }
        public void CloseForm() { }
    }

    // #2496's two sequences, native and p-code agreeing:
    // docs/vb6-fidelity-oracle/the-forms-collection.md#the-terminates-at-the-runs-end.
    // MakeDefaultInstance supplies the second instance directly: New Form2 remains unsupported (#822).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Run_end_terminates_in_project_order_and_newest_instance_first(bool secondInstanceFirst)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        foreach (var name in new[] { "Form3", "Form1", "Form4", "Form2" })
        {
            var code = $"VERSION 5.00\r\nBegin VB.Form {name} \r\n   Caption = \"{name}\"\r\nEnd\r\nAttribute VB_Name = \"{name}\"\r\n"
                + "Public pub As Integer\r\nPrivate Sub Form_Initialize()\r\n"
                + (name == "Form2"
                    ? $"n = n + 1\r\nIf n = 1 Then pub = {(secondInstanceFirst ? 22 : 2)} Else pub = {(secondInstanceFirst ? 2 : 22)}\r\n"
                    : $"pub = {name[^1]}\r\n")
                + $"End Sub\r\nPrivate Sub Form_Terminate()\r\nDebug.Print \"{name} \" & pub\r\nEnd Sub\r\n";
            project.AddForm(new FormDeserializer().Deserialize(project, code, new Sink())!);
        }
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode("Public n As Integer\r\nPublic Sub Main()\r\nEnd Sub\r\n");
        project.AddModule(module);
        var library = new MockStdLib(debug);
        var views = new List<View>();
        var program = new VBProgram(project, new VBWindowContext(library), null, _ =>
        {
            var view = new View(library);
            views.Add(view);
            return view;
        });
        var sequence = secondInstanceFirst
            ? new[] { "Form2", "Form4", "Form2", "Form1", "Form3" }
            : new[] { "Form4", "Form2", "Form1", "Form3", "Form2" };
        foreach (var name in sequence)
        {
            program.Interpreter.Modules.TryGet(name, out var form).Should().BeTrue();
            await program.MakeDefaultInstance(form);
            // Keep each instance held until shutdown, as the probe's default names and module variable do.
            program.Interpreter.AddRef(Vb6Value.NewObject(views[^1].Context.FormInstance!));
        }

        await program.RunMainAsync();

        debug.Select(v => v.Value).Should().Equal("Form3 3", "Form1 1", "Form4 4",
            secondInstanceFirst ? "Form2 2" : "Form2 22", secondInstanceFirst ? "Form2 22" : "Form2 2");
        program.Ended.IsCompleted.Should().BeTrue();
    }
}
