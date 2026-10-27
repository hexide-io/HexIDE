using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.Automation;
using HexIDE.Runtime;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Automation;

public class ProgramWindowTests : WindowTestFixture
{
    private sealed class FormWindow : Window, IModuleExecutionRoot
    {
        public EventPump? Events { get; } = new();
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null) { }
    }

    [AvaloniaTheory]
    [InlineData("dialog", true)]
    [InlineData("message", true)]
    [InlineData("message", false)]
    [InlineData("input", true)]
    [InlineData("input", false)]
    public void An_IDE_window_is_not_the_program_even_when_owned_by_its_form(string kind, bool owned)
    {
        UiAutomationDriver.TrackWindows();
        var form = new FormWindow();
        form.Show();
        var dialog = new Window
        {
            Content = kind switch
            {
                "message" => new HexIDE.Runtime.Dialogs.MessageBox(),
                "input" => new HexIDE.Runtime.Dialogs.InputBox(),
                _ => new CheckBox()
            }
        };
        if (owned)
            dialog.Show(form);
        else
            dialog.Show();

        UiAutomationDriver.ProgramOf(dialog).Should().BeNull();
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_box_raised_by_the_program_keeps_its_program(bool message)
    {
        var form = new FormWindow();
        form.Show();
        var library = new StandaloneStandardLib(form);
        Task pending = message
            ? library.MsgBox("Question", "Program", HexIDE.IDE.MessageBoxButtons.Ok, HexIDE.IDE.MessageBoxIcon.None)
            : library.InputBox("Question", "Program", "");
        var box = form.OwnedWindows.Single();

        UiAutomationDriver.ProgramOf(form)!.Value.Pump.Should().BeSameAs(form.Events);
        UiAutomationDriver.ProgramOf(box)!.Value.Pump.Should().BeSameAs(form.Events);
        box.Close();
        await pending;
    }
}
