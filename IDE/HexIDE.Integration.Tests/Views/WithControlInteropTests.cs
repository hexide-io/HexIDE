using Avalonia.Controls;
using HexIDE.IDE;
using HexIDE.Runtime;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// Phase 4.4 — With over a real (headless) control. The runtime unit tests prove the leading-dot → With-stack
/// mechanism against the Debug proxy; this proves member property read AND write dispatch through a With block
/// on an actual Avalonia control, which the runtime unit project can't instantiate.
/// </summary>
public class WithControlInteropTests
{
    private static readonly ProjectDefinition Project = new(VBProjectType.EXE, "MyProject");

    private class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    private class CaptureLib(List<Vb6Value> debug) : IBasicStandardLibrary
    {
        public Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon)
            => Task.FromResult<MessageBoxResult>(default);
        public Task<string?> InputBox(string prompt, string? title, string defaultText)
            => Task.FromResult<string?>(null);
        public void DebugPrint(Vb6Value value) => debug.Add(value);
    }

    private static TextBox SpawnTextBox()
    {
        var form = new FormDeserializer().Deserialize(Project, """
            VERSION 5.00
            Begin VB.UserControl MyCtrl
               Begin VB.TextBox Text1
                  Text = "initial"
               End
            End
            Attribute VB_Name = "MyCtrl"
            """, NullSink.Instance)!;
        var canvas = VBLoader.SpawnComponentsForDesigner(form);
        return (TextBox)canvas.Children[0];
    }

    [AvaloniaFact]
    public async Task With_WritesThenReadsAControlProperty()
    {
        var textbox = SpawnTextBox();

        var context = new ModuleExecutionContext();
        var rootEnv = new ExecutionEnvironment();
        context.AllocVariable(rootEnv, "Text1", new Vb6Value((Control)textbox));

        var debug = new List<Vb6Value>();
        var code =
            "With Text1\r\n" +
            ".Text = \"Hi\"\r\n" +      // member write through the With
            "End With\r\n" +
            "Dim s\r\n" +
            "With Text1\r\n" +
            "s = .Text\r\n" +          // member read through the With
            "End With\r\n" +
            "Debug.Print s\r\n";
        var vb = new BasicInterpreter(new CaptureLib(debug), context, rootEnv, code);
        await vb.Execute();

        textbox.Text.Should().Be("Hi");
        debug.Should().ContainSingle();
        debug[0].Value.Should().Be("Hi");
    }
}
