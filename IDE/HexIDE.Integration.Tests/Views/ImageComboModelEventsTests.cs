using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Runtime;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

// #1092 group 2 corrects group 1: code Change is posted, including unchanged assignments.
public class ImageComboModelEventsTests
{
    private sealed class Sink : IBasicStandardLibrary
    {
        public void DebugPrint(Vb6Value value) { }
        public Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon)
            => throw new NotImplementedException();
        public Task<string?> InputBox(string prompt, string? title, string defaultText)
            => throw new NotImplementedException();
    }

    [AvaloniaFact]
    public async Task Code_changes_wait_for_DoEvents_and_are_not_coalesced()
    {
        var events = new EventPump();
        var changes = new List<string>();
        var context = new ModuleExecutionContext();
        var env = new ExecutionEnvironment();
        var model = new ImageComboModel(events, () =>
        {
            changes.Add("Change");
            return Task.CompletedTask;
        });
        context.AllocVariable(env, "combo", new Vb6Value(model));
        Task Run(string code) => new BasicInterpreter(new Sink(), context, env, code) { Events = events }.Execute();
        var done = new TaskCompletionSource();
        events.Raise("Form_Load", async () =>
        {
            try
            {
                await Run("combo.Text = \"typed\"\ncombo.Text = \"typed\"\n" +
                    "Set a = combo.ComboItems.Add(, , \"Alpha\")\n" +
                    "Set combo.SelectedItem = a\nSet combo.SelectedItem = a\na.Selected = True\na.Text = \"Alef\"\n");
                changes.Should().BeEmpty();
                await Run("DoEvents\n");
                changes.Should().Equal("Change", "Change", "Change", "Change", "Change", "Change");
                await Run("Set combo.SelectedItem = Nothing\ncombo.ComboItems.Remove 1\ncombo.ComboItems.Clear\nDoEvents\n");
                changes.Should().HaveCount(6);
                done.SetResult();
            }
            catch (Exception e) { done.SetException(e); }
        });
        await done.Task;
    }
}
