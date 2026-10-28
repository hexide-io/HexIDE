using System;
using System.IO;
using System.Collections.Generic;
using HexIDE.IDE;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

public class ImageComboLoadingTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string error) => throw new Exception(error);
    }

    private static string Corpus()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "corpus")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "corpus/inbox-controls/pending/imagecombo");
    }

    private sealed class Capture(List<string> printed) : IBasicStandardLibrary
    {
        public void DebugPrint(Vb6Value value) => printed.Add((string)value.Value!);
        public Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon)
            => throw new NotImplementedException();
        public Task<string?> InputBox(string prompt, string? title, string defaultText)
            => throw new NotImplementedException();
    }

    [AvaloniaFact]
    public async Task Loaded_control_dispatches_posted_Change_to_its_form()
    {
        var printed = new List<string>();
        var source = File.ReadAllText(Path.Combine(Corpus(), "Form1.frm")) + """

            Private Sub Form_Load()
                Dim a As Object
                ImageCombo2.Text = "typed"
                Debug.Print "before"
                DoEvents
                Debug.Print "after"
                Set a = ImageCombo2.ComboItems.Add(, , "Alpha")
                ImageCombo2.SelectedItem = a
                DoEvents
            End Sub
            Private Sub ImageCombo2_Change()
                Debug.Print ImageCombo2.Text
            End Sub
            """;
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, source, new Sink())!;
        project.AddForm(form);
        var context = new VBWindowContext(new Capture(printed));
        var program = VBLoader.PrepareForm(form, context, _ => { }, null);
        var failures = new List<Exception>();
        void RuntimeError(VBWindowContext sender, VBRunTimeException error) { if (sender == context) failures.Add(error); }
        void Fault(VBWindowContext sender, Exception error) { if (sender == context) failures.Add(error); }
        VBWindowContext.RunTimeError += RuntimeError;
        VBWindowContext.Faulted += Fault;
        try { (await context.StartAsync()).Should().BeTrue(); }
        finally
        {
            VBWindowContext.RunTimeError -= RuntimeError;
            VBWindowContext.Faulted -= Fault;
        }
        failures.Should().BeEmpty();
        printed.Should().Equal("before", "typed", "after", "Alpha");
    }

    [AvaloniaFact]
    public async Task Every_saved_form_loads_its_properties_and_connects_the_owner()
    {
        var forms = Directory.GetFiles(Corpus(), "*.frm");
        forms.Should().NotBeEmpty();
        foreach (var path in forms)
        {
            var project = new ProjectDefinition(VBProjectType.EXE, "P");
            var form = new FormDeserializer().Deserialize(project, File.ReadAllText(path), new Sink())!;
            form.UnknownChildSubtreeTexts.Count.Should().Be(8);
            var context = new ModuleExecutionContext();
            var env = new ExecutionEnvironment();
            var root = (DockPanel)VBLoader.SpawnComponents(form, context, env);
            var canvas = root.Children.OfType<Canvas>().Single();
            var combos = canvas.Children.OfType<VBImageCombo>().ToDictionary(c => VBProps.GetName(c)!);
            combos.Count.Should().Be(8);
            string Text(string name) { combos[name].TryGetProperty("Text", out var v); return (string)v.Value!; }
            Text("ImageCombo1").Should().BeEmpty();
            Text("ImageCombo2").Should().Be("hello");
            Text("ImageCombo8").Should().Be("ImageCombo8");
            combos["ImageCombo3"].Model.Indentation.Should().Be(2);
            combos["ImageCombo3"].TryGetProperty("Locked", out var locked);
            locked.Value.Should().Be(true);
            combos["ImageCombo4"].IsEnabled.Should().BeFalse();
            combos["ImageCombo1"].BackColor.Should().Be(unchecked((int)0x8000000F));
            combos["ImageCombo1"].ForeColor.Should().Be(unchecked((int)0x80000012));
            combos["ImageCombo5"].BackColor.Should().Be(255);
            combos["ImageCombo5"].ForeColor.Should().Be(65280);
            combos["ImageCombo8"].BackColor.Should().Be(-2147483643);
            combos["ImageCombo8"].ForeColor.Should().Be(-2147483640);
            combos["ImageCombo4"].MousePointer.Should().Be(11);
            combos["ImageCombo4"].OLEDragMode.Should().Be(1);
            combos["ImageCombo4"].OLEDropMode.Should().Be(1);
            KeyboardNavigation.GetTabIndex(combos["ImageCombo8"]).Should().Be(7);
            combos["ImageCombo6"].Height.Should().Be(25);
            AttachedProperties.GetFont(combos["ImageCombo6"])!.Value.FontFamilyName.Should().Be("Arial");
            AttachedProperties.GetFont(combos["ImageCombo6"])!.Value.Size.Should().Be(9.75);
            Canvas.GetLeft(combos["ImageCombo8"]).Should().Be(190);
            combos["ImageCombo8"].Width.Should().Be(100);
            var combo = combos["ImageCombo3"];
            var item = (await combo.Model.Items.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new Vb6Value("Alpha")]))!.Value;
            combo.TrySetProperty("SelectedItem", item);
            Text("ImageCombo3").Should().Be("Alpha");
            ((ImageComboItem)item.Value!).TryGetProperty("Indentation", out var indent);
            indent.Value.Should().Be(2);
            Action binding = () => combo.TryGetProperty("ImageList", out _);
            binding.Should().Throw<NotImplementedException>();
        }
    }
}
