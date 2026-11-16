using Avalonia.Controls;
using Avalonia.Labs.Input;
using HexIDE.Automation;
using HexIDE.Integration.Tests.Automation;
using HexIDE.Runtime;
using HexIDE.Runtime.Debugging;
using HexIDE.Utils;

namespace HexIDE.Integration.Tests.Views;

public class RunningFormShortcutTests
{
    // vb6.exe SP6 measurements (2026-11-16): https://github.com/hexide-io/HexIDE/issues/1297
    [AvaloniaFact]
    public async Task Break_key_then_statement_gate_activates_ide()
    {
        await using var owned = new RealComposition();
        _ = owned.Composition.Root;
        var previous = Static.MainView;
        var activation = new IdeActivationProbe();
        var ide = ToolbarAutomationNameTests.ShowMainView(activation.Window);
        Static.MainView = (MainView)ide.Content!;
        var input = new TextBox();
        var form = new VBFormRuntime { Content = input };
        var debug = (IDebugGate)owned.Composition.DebugController;
        var main = Static.MainView;
        CommandManager.GetCommandBindings(main)!.Clear();
        CommandManager.GetCommandBindings(main)!.Add(new CommandBinding
        {
            Command = ApplicationCommands.BreakProjectCommand,
            ExecutingCommand = new DelegateCommand(debug.Pause)
        });
        try
        {
            form.Show();
            IdeDialogOwnershipTests.Activate(form);
            debug.Reset();
            input.Focus().Should().BeTrue();
            var gesture = ApplicationCommands.BreakProjectCommand.Gestures.Single();
            var reply = UiAutomationDriver.PressKey(input, gesture.Key.ToString(), gesture.KeyModifiers.ToString());
            reply.Success.Should().BeTrue(reply.Error);
            reply.Mechanism.Should().Be("window-input");
            var paused = debug.OnStatementAsync(1, "Form1", null);
            debug.State.Should().Be(DebugState.Paused);
            activation.Requests.Should().Be(1);
            debug.Continue();
            await paused;
        }
        finally { debug.Stop(); form.Close(); ide.Close(); Static.MainView = previous; }
    }

    [AvaloniaTheory]
    [InlineData("find")]
    [InlineData("immediate")]
    [InlineData("break")]
    public void Only_Break_reaches_ide_from_running_form(string name)
    {
        var previous = Static.MainView;
        var ide = ToolbarAutomationNameTests.ShowMainView();
        var main = (MainView)ide.Content!;
        Static.MainView = main;
        var input = new TextBox();
        var form = new VBFormRuntime { Content = input };
        var command = name switch
        {
            "find" => ApplicationCommands.FindCommand,
            "immediate" => ApplicationCommands.OpenImmediateCommand,
            _ => ApplicationCommands.BreakProjectCommand
        };
        var calls = 0;
        // Bind a real IDE command, rather than invoking the handler directly.
        CommandManager.GetCommandBindings(main)!.Clear();
        CommandManager.GetCommandBindings(main)!.Add(new CommandBinding
        {
            Command = command,
            ExecutingCommand = new DelegateCommand(() => calls++)
        });
        try
        {
            form.Show();
            IdeDialogOwnershipTests.Activate(form);
            input.Focus().Should().BeTrue();
            var gesture = command.Gestures.Single();
            var reply = UiAutomationDriver.PressKey(input, gesture.Key.ToString(), gesture.KeyModifiers.ToString());
            reply.Success.Should().BeTrue(reply.Error);
            reply.Mechanism.Should().Be("window-input");
            calls.Should().Be(name == "break" ? 1 : 0);
            if (name != "break")
            {
                var ideInput = new TextBox();
                main.Content = ideInput;
                ide.Activate();
                ideInput.Focus();
                UiAutomationDriver.PressKey(ideInput, gesture.Key.ToString(), gesture.KeyModifiers.ToString());
                calls.Should().Be(1, "the shortcut must actually be bound in the IDE");
            }
        }
        finally { form.Close(); ide.Close(); Static.MainView = previous; }
    }
}
