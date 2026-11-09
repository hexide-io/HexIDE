using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Runtime;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.BuiltinControls;

namespace HexIDE.Integration.Tests.Views;

// #1092 group 2 corrects group 1: code Change is posted, including unchanged assignments.
public class ImageComboModelEventsTests
{
    private sealed class Sink : IBasicStandardLibrary
    {
        internal readonly List<Vb6Value> Values = [];
        public void DebugPrint(Vb6Value value) => Values.Add(value);
        public Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon)
            => throw new NotImplementedException();
        public Task<string?> InputBox(string prompt, string? title, string defaultText)
            => throw new NotImplementedException();
    }

    // #1092 issuecomment-6445666226: native and p-code retain Text1 with Set.
    [AvaloniaFact]
    public async Task Item_Tag_retains_a_TextBox_object_with_Set()
    {
        var context = new ModuleExecutionContext();
        var env = new ExecutionEnvironment();
        var items = new ImageComboItems();
        var textBox = new VBTextBox { Text = "Text1" };
        var sink = new Sink();
        context.AllocVariable(env, "items", new Vb6Value(items));
        context.AllocVariable(env, "Text1", new Vb6Value(textBox));
        await new BasicInterpreter(sink, context, env,
            "Set a = items.Add()\nSet a.Tag = Text1\nDebug.Print a.Tag Is Text1\n").Execute();
        sink.Values.Should().Equal(new Vb6Value(true));
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
