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

    private sealed class View : Control, IFormView, IModuleExecutionRoot
    {
        public VBWindowContext Context { get; }
        public View(IBasicStandardLibrary library) => Context = new(library) { Window = this };
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null) => Context.ExecuteSub(name, args);
        public Task<bool> UnloadAsync(int mode) => Context.UnloadAsync(mode);
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
    // #1490, measured vb6.exe SP6, native and p-code, both module listing orders:
    // https://github.com/hexide-io/HexIDE/issues/1490 (full probe in the issue).
    [Theory]
    [InlineData(false, false, "direct")]
    [InlineData(true, false, "direct")]
    [InlineData(false, true, "direct")]
    [InlineData(false, false, "shared")]
    [InlineData(false, false, "field")]
    public async Task Main_return_releases_classes_newest_first_before_default_forms(bool reverseModules, bool end, string holder)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        void Module(string name, ModuleKind kind, string code)
        {
            var module = new ModuleDefinition(project, name, kind);
            module.UpdateCode(code);
            project.AddModule(module);
        }
        Module("C1", ModuleKind.ClassModule, """
            Public Label As String
            Private Sub Class_Terminate()
                Debug.Print "term " & Label
            End Sub
            """);
        Module("Holder", ModuleKind.ClassModule, "Public Child As C1\n");
        Module("Module1", ModuleKind.StandardModule, """
            Private a As C1
            Private b As C1
            Private x As Object
            Private alias As C1
            Public Sub Main()
                Dim first As New C1
                first.Label = "Module1.x"
                Set b = New C1
                b.Label = "Module1.b"
                Set a = New C1
                a.Label = "Module1.a"
                ModB.MakePrivate
                Set ModA.pa = New C1
                ModA.pa.Label = "ModA.pa"
                ModA.MakePrivate
                Set ModB.pb = New C1
                ModB.pb.Label = "ModB.pb"
                Hold
                Hold
            """ + (holder == "field" ? "\nSet x = New Holder\nSet x.Child = first\n"
                : "\nSet x = first\n" + (holder == "shared" ? "Set alias = first\n" : "")) + """
                Load Form1
                Unload Form1
            """ + (end ? "\nEnd\n" : "\n") + """
            End Sub
            Private Sub Hold()
                Static s As New C1
                s.Label = "Static s"
            End Sub
            """);
        foreach (var name in reverseModules ? new[] { "ModA", "ModB" } : new[] { "ModB", "ModA" })
        {
            var suffix = name[^1].ToString().ToLowerInvariant();
            Module(name, ModuleKind.StandardModule, $"""
                Public p{suffix} As C1
                Private q{suffix} As C1
                Public Sub MakePrivate()
                    Set q{suffix} = New C1
                    q{suffix}.Label = "{name}.q{suffix}"
                End Sub
                """);
        }
        project.AddForm(new FormDeserializer().Deserialize(project, """
            VERSION 5.00
            Begin VB.Form Form1
                Caption = "Form1"
            End
            Attribute VB_Name = "Form1"
            Private Sub Form_Terminate()
                Debug.Print "term Form1"
            End Sub
            """, new Sink())!);
        var library = new MockStdLib(debug);
        var program = new VBProgram(project, new VBWindowContext(library), null, _ => new View(library));

        await program.RunMainAsync();

        if (end)
            debug.Should().BeEmpty();
        else
            debug.Select(v => v.Value).Should().Equal("term Static s", "term ModB.pb", "term ModA.qa",
                "term ModA.pa", "term ModB.qb", "term Module1.a", "term Module1.b", "term Module1.x", "term Form1");
    }

    // #1490's creation-order list is kept for the end of the program, so it must not grow with every object a long-running
    // program makes and drops: the entries of terminated objects are compacted away as the list grows.
    [Fact]
    public async Task A_program_making_and_dropping_objects_keeps_no_entry_for_each_one()
    {
        const string main = "Dim o As C1, i As Long\nFor i = 1 To 5000\n    Set o = New C1\nNext\nDebug.Print \"made \" & (i - 1)\n";
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, main, "Module1", null,
            [("C1", "Public Label As String\n")]);

        await vb.Execute();

        debug.Select(v => v.Value).Should().Equal("made 5000");
        vb.TrackedInstanceCount.Should().BeLessThanOrEqualTo(1024);
    }
}
