using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using HexIDE.Runtime.BuiltinControls;
using Avalonia.LogicalTree;
using HexIDE.Runtime;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

// #1631 part B, vb6.exe SP6 native and p-code, real input: issuecomment-6425849231.
public partial class UserControlKeyTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string error) => throw new InvalidOperationException(error);
    }

    private sealed class Library : IBasicStandardLibrary
    {
        internal readonly List<Vb6Value> Printed = [];
        internal Action? BeforePrint;
        public void DebugPrint(Vb6Value value)
        {
            BeforePrint?.Invoke();
            Printed.Add(value);
        }
        public Task<HexIDE.IDE.MessageBoxResult> MsgBox(string text, string? caption, HexIDE.IDE.MessageBoxButtons buttons, HexIDE.IDE.MessageBoxIcon icon) => throw new NotImplementedException();
        public Task<string?> InputBox(string prompt, string? title, string defaultText) => throw new NotImplementedException();
    }

    private sealed class RunWindow : Window, IModuleExecutionRoot
    {
        internal VBWindowContext? Context;
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null) => Context?.ExecuteSub(name, args);
    }

    private sealed class Run : IDisposable
    {
        internal readonly RunWindow Window = new() { Width = 500, Height = 400 };
        internal readonly Library Lib = new();
        internal readonly VBWindowContext Context;
        internal Control Named(string name) => Window.GetLogicalDescendants().OfType<Control>().Single(c => VBProps.GetName(c) == name);
        internal Run(string settings, string formCode = "", string controlCode = "", string child = "", bool plain = false, string? blocks = null)
        {
            var project = new ProjectDefinition(VBProjectType.EXE, "P");
            var ctl = new FormDeserializer().Deserialize(project,
                $"VERSION 5.00\nBegin VB.UserControl Dial\nClientWidth = 1200\n{settings}\nEnd\nAttribute VB_Name = \"Dial\"\n{controlCode}", new Sink())!;
            var module = new ModuleDefinition(project, "Dial", ModuleKind.UserControl);
            module.UpdateCode($"Attribute VB_Name = \"Dial\"\n{controlCode}");
            module.UpdateFormPart(ctl);
            project.AddModule(module);
            var registry = new Dictionary<string, ComponentBaseClass>();
            registry["P.Dial"] = UserControlComponentClass.ForModule("P.Dial", ctl, registry);
            if (plain)
            {
                var plainDefinition = new FormDeserializer().Deserialize(project,
                    $"VERSION 5.00\nBegin VB.UserControl Plain\nClientWidth = 1200\nEnd\nAttribute VB_Name = \"Plain\"\n{controlCode}", new Sink())!;
                var plainModule = new ModuleDefinition(project, "Plain", ModuleKind.UserControl);
                plainModule.UpdateCode($"Attribute VB_Name = \"Plain\"\n{controlCode}");
                plainModule.UpdateFormPart(plainDefinition);
                project.AddModule(plainModule);
                registry["P.Plain"] = UserControlComponentClass.ForModule("P.Plain", plainDefinition, registry);
            }
            var form = new FormDeserializer().Deserialize(project,
                "VERSION 5.00\nBegin VB.Form Form1\nClientWidth = 6000\nClientHeight = 4000\n"
                + (blocks ?? (Block("VB.TextBox", "Text1", 0) + Block("P.Dial", "uc1", 1, child)
                + (plain ? Block("P.Plain", "ucPlain", 4) : "")
                + Block("VB.CommandButton", "Command1", 2) + Block("VB.Label", "Label1", 3, extra: "Caption = \"&T\"")))
                + "End\nAttribute VB_Name = \"Form1\"\n" + formCode, new Sink(), extraComponents: registry)!;
            project.AddForm(form);
            Context = new VBWindowContext(Lib);
            Window.Context = Context;
            VBLoader.PrepareForm(form, Context, content => Window.Content = content, null);
            ContainerRuntimeHarness.MergeAppResources(Window);
            Context.StartAsync().GetAwaiter().GetResult().Should().BeTrue();
            Window.Show();
            Context.ShowControls();
        }
        internal void Key(Key key, KeyModifiers modifiers = KeyModifiers.None)
        {
            Window.KeyPress(key, (RawInputModifiers)modifiers, PhysicalKey.None, null);
            Window.KeyRelease(key, (RawInputModifiers)modifiers, PhysicalKey.None, null);
        }
        internal async Task Code(string code)
        {
            foreach (var line in code.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                await new StatementExecutor(Context.Interpreter!, Context.InstanceEnv, Context.FormInstance!.ClassDef)
                    .Visit(Context.Interpreter!.TryParseStatement(line)!);
        }
        public void Dispose() { Context.Program?.Abandon(); Window.Close(); }
    }

    private static string Block(string type, string name, int tab, string child = "", string extra = "") =>
        $"Begin {type} {name}\nHeight = 375\nWidth = 1200\nTop = {tab * 600}\nLeft = 120\nTabIndex = {tab}\n{extra}\n{child}\nEnd\n";

    // VB6 SP6 native/p-code: #1631, issuecomment-6425849231.
    [AvaloniaTheory]
    [InlineData("", "uc1")]
    [InlineData("Windowless = -1", "uc1")]
    [InlineData("CanGetFocus = 0", "Command1")]
    [InlineData("ForwardFocus = -1", "Command1")]
    [InlineData("InvisibleAtRuntime = -1", "Command1")]
    public void Tab_stops_only_on_controls_that_can_receive_it(string settings, string next)
    {
        using var run = new Run(settings);
        run.Named("Text1").Focus();
        run.Key(Avalonia.Input.Key.Tab);
        run.Named(next).IsFocused.Should().BeTrue();
    }

    // VB6 SP6 native/p-code: #1631, issuecomment-6425849231.
    [AvaloniaFact]
    public void A_child_inside_a_ControlContainer_is_a_separate_tab_stop()
    {
        using var run = new Run("ControlContainer = -1", child: Block("VB.TextBox", "Inner", 2));
        run.Named("uc1").Focus().Should().BeTrue();
        run.Key(Avalonia.Input.Key.Tab);
        run.Named("Inner").IsFocused.Should().BeTrue();
        run.Key(Avalonia.Input.Key.Tab);
        run.Named("Command1").IsFocused.Should().BeTrue();
    }

    // VB6 SP6 native/p-code: #1631, issuecomment-6425849231.
    [AvaloniaFact]
    public void A_control_without_TabIndex_leaves_a_hole_that_an_out_of_range_index_takes()
    {
        using var run = new Run("CanGetFocus = 0", plain: true, blocks:
            Block("VB.TextBox", "Text1", 0) + Block("P.Plain", "ucPlain", 1)
            + Block("VB.CommandButton", "Command1", 2) + Block("P.Dial", "uc1", 3)
            + Block("VB.CommandButton", "cmdOut", 17) + Block("VB.Label", "Label1", 4));
        KeyboardNavigation.GetTabIndex(run.Named("cmdOut")).Should().Be(3);
        KeyboardNavigation.GetTabIndex(run.Named("Label1")).Should().Be(4);
    }

    // VB6 SP6 native/p-code: #1631, issuecomment-6425849231.
    [AvaloniaTheory]
    [InlineData("AccessKeys = \"q\"", Avalonia.Input.Key.Q, "uc1", "113")]
    [InlineData("ForwardFocus = -1\nAccessKeys = \"w\"", Avalonia.Input.Key.W, "Command1", "")]
    [InlineData("CanGetFocus = 0\nAccessKeys = \"z\"", Avalonia.Input.Key.Z, "Text1", "")]
    public void Access_keys_require_Alt_and_forward_as_measured(string settings, Key key, string target, string printed)
    {
        using var run = new Run(settings, controlCode:
            "Private Sub UserControl_AccessKeyPress(KeyAscii As Integer)\nDebug.Print CStr(KeyAscii)\nEnd Sub\n");
        var start = target == "Command1" ? "Text1" : "Command1";
        run.Named(start).Focus();
        if (target == "uc1")
            run.Lib.BeforePrint = () => run.Named("uc1").IsFocused.Should().BeFalse("AccessKeyPress precedes focus");
        run.Key(key);
        run.Named(start).IsFocused.Should().BeTrue();
        run.Key(key, KeyModifiers.Alt);
        run.Named(target).IsFocused.Should().BeTrue();
        run.Lib.Printed.Select(v => v.Value?.ToString()).Should().Equal(printed.Length == 0 ? [] : new[] { printed });
    }

    // These are HexIDE's existing results, not claimed VB6 measurements. Part B must leave them alone.
    [AvaloniaFact]
    public async Task Unmeasured_TypeName_and_New_behaviour_is_preserved()
    {
        using var run = new Run("");
        await run.Code("Debug.Print TypeName(uc1)\n");
        run.Lib.Printed.Should().Equal(new Vb6Value("Canvas"));
        var act = () => run.Context.Interpreter!.NewObject("Dial");
        await act.Should().ThrowAsync<VBCompileErrorException>().WithMessage("*User-defined type not defined*");
    }

    // VB6 SP6 native/p-code: #1631, issuecomment-6425849231.
    [AvaloniaFact]
    public void AccessKeys_can_be_read_and_changed_in_the_controls_own_code()
    {
        using var run = new Run("AccessKeys = \"q\"", controlCode:
            "Private Sub UserControl_AccessKeyPress(KeyAscii As Integer)\nDebug.Print UserControl.AccessKeys\nUserControl.AccessKeys = \"z\"\nEnd Sub\n");
        run.Named("Text1").Focus();
        run.Key(Avalonia.Input.Key.Q, KeyModifiers.Alt);
        run.Lib.Printed.Should().Equal(new Vb6Value("q"));
        run.Key(Avalonia.Input.Key.Q, KeyModifiers.Alt);
        run.Lib.Printed.Should().Equal(new Vb6Value("q"));
        run.Key(Avalonia.Input.Key.Z, KeyModifiers.Alt);
        run.Lib.Printed.Should().Equal(new Vb6Value("q"), new Vb6Value("z"));
    }

    // VB6 SP6 native/p-code: #1631, issuecomment-6425849231.
    [AvaloniaTheory]
    [InlineData("CanGetFocus = 0\nAccessKeys = \"z\"", Avalonia.Input.Key.Z, 122, "Text1.TabIndex = 12\n")]
    [InlineData("", Avalonia.Input.Key.T, 116, "ucPlain.TabIndex = 4\n")]
    public async Task A_forwarded_key_is_raised_on_the_UserControl_that_receives_focus(string settings, Key key, int ascii, string order)
    {
        using var run = new Run(settings, plain: true, controlCode:
            "Private Sub UserControl_AccessKeyPress(KeyAscii As Integer)\nDebug.Print KeyAscii\nEnd Sub\n");
        await run.Code(order);
        run.Named("Command1").Focus();
        run.Key(key, KeyModifiers.Alt);
        run.Named("ucPlain").IsFocused.Should().BeTrue();
        run.Lib.Printed.Should().ContainSingle().Which.Should().Be(new Vb6Value(ascii));
    }

    // VB6 SP6 native/p-code: #1631, issuecomment-6425849231.
    [AvaloniaTheory]
    [InlineData(Avalonia.Input.Key.Enter, 13)]
    [InlineData(Avalonia.Input.Key.Escape, 27)]
    public async Task Default_and_Cancel_deliver_AccessKeyPress_instead_of_TextBox_KeyPress(Key key, int ascii)
    {
        using var run = new Run("DefaultCancel = -1", formCode:
            "Private Sub Text1_KeyPress(KeyAscii As Integer)\nDebug.Print \"textbox\"\nEnd Sub\n", controlCode:
            "Private Sub UserControl_AccessKeyPress(KeyAscii As Integer)\nDebug.Print KeyAscii\nEnd Sub\n");
        await run.Code("uc1.Default = True\nuc1.Cancel = True\n");
        run.Named("Text1").Focus();
        run.Key(key);
        run.Lib.Printed.Should().ContainSingle().Which.Should().Be(new Vb6Value(ascii));
    }

    // VB6 SP6 native/p-code: #1631, issuecomment-6425849231.
    [AvaloniaFact]
    public async Task DisplayAsDefault_changes_with_CommandButton_focus_and_Enter_clicks_that_button()
    {
        using var run = new Run("DefaultCancel = -1", formCode:
            "Private Sub Command1_Click()\nDebug.Print \"click\"\nEnd Sub\n", controlCode:
            "Private Sub UserControl_AmbientChanged(PropertyName As String)\nIf PropertyName = \"DisplayAsDefault\" Then Debug.Print Ambient.DisplayAsDefault\nEnd Sub\n");
        run.Named("Text1").Focus();
        await run.Code("uc1.Default = True\n");
        run.Lib.Printed.Should().ContainSingle().Which.Should().Be(new Vb6Value(true));
        run.Named("Command1").Focus();
        run.Key(Avalonia.Input.Key.Enter);
        run.Lib.Printed.Should().Equal(new Vb6Value(true), new Vb6Value(false), new Vb6Value("click"));
    }
}
