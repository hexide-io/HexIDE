using Avalonia.Controls;
using Avalonia.Threading;
using HexIDE.IDE;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Views;

// VB6 SP6 IDE measurements: hexide-io/HexIDE#4205, oracle PR #4358.
public class ElementHandlerTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string message) { }
    }

    private static string Source(bool element, string parameter, string defaults = "") =>
        "VERSION 5.00\nBegin VB.Form Form1\nBegin VB.CommandButton Command1\nCaption = \"Command1\"\n"
        + (element ? "Index = 0\n" : "")
        + "End\nEnd\nAttribute VB_Name = \"Form1\"\n" + defaults
        + "Private Sub Form_Initialize()\nDebug.Print \"initialize\"\nEnd Sub\n"
        + $"Private Sub Command1_Click({parameter})\nDebug.Print TypeName({(parameter.StartsWith('i') ? "i" : "Index")})\n"
        + $"Debug.Print {(parameter.StartsWith('i') ? "i" : "Index")}\nEnd Sub\n";

    private static (List<string> Printed, List<Exception> Errors) Run(string source, bool click)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, source.ReplaceLineEndings("\r\n"), new NullSink())!;
        project.AddForm(form);
        var printed = new List<string>();
        var errors = new List<Exception>();
        void Compile(VBWindowContext _, VBCompileErrorException error) => errors.Add(error);
        void Fault(VBWindowContext _, Exception error) => errors.Add(error);
        void Runtime(VBWindowContext _, VBRunTimeException error) => errors.Add(error);
        VBDebugConsole.Output += printed.Add;
        VBWindowContext.CompileError += Compile;
        VBWindowContext.Faulted += Fault;
        VBWindowContext.RunTimeError += Runtime;
        try
        {
            VBLoader.RunForm(form, CancellationToken.None, out var window);
            Dispatcher.UIThread.RunJobs();
            if (click && errors.Count == 0)
            {
                var button = window.Context.FormInstance!.Controls!.All.OfType<VBCommandButton>().Single();
                button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
            }
            window.Close();
        }
        finally
        {
            VBDebugConsole.Output -= printed.Add;
            VBWindowContext.CompileError -= Compile;
            VBWindowContext.Faulted -= Fault;
            VBWindowContext.RunTimeError -= Runtime;
        }
        return (printed, errors);
    }

    [AvaloniaTheory]
    [InlineData(true, "")]
    [InlineData(false, "Index As Integer")]
    [InlineData(true, "Index As Long")]
    [InlineData(true, "ByVal Index As Integer")]
    [InlineData(true, "Index")]
    public void A_mismatch_stops_before_Initialize_at_the_handler_declaration(bool element, string parameter)
    {
        var source = Source(element, parameter);
        var run = Run(source, false);
        run.Printed.Should().BeEmpty();
        var error = run.Errors.Should().ContainSingle().Subject.Should().BeOfType<VBCompileErrorException>().Subject;
        error.Message.Should().Be("Compile error:\n\nProcedure declaration does not match description of event or procedure having the same name");
        var line = source[..source.IndexOf("Private Sub Command1_Click", StringComparison.Ordinal)].Count(c => c == '\n') + 1;
        error.Line.Should().Be(line);
        ErrorLocation.Of(error).Should().Be(new ErrorLocation("Form1", line));
    }

    [AvaloniaTheory]
    [InlineData("i As Integer", "")]
    [InlineData("Index", "DefInt I\n")]
    [InlineData("Index%", "")]
    [InlineData("Index As Integer", "")]
    public void An_elements_first_argument_is_an_Integer_zero_regardless_of_its_name(string parameter, string defaults)
    {
        var run = Run(Source(true, parameter, defaults), true);
        run.Errors.Should().BeEmpty();
        run.Printed.Select(s => s.Trim()).Should().Equal("initialize", "Integer", "0");
    }
}
