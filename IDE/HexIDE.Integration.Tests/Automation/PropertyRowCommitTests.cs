using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Automation;
using HexIDE.Controls;
using HexIDE.Runtime.Components;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// A Properties window row commits its text on Enter, and <c>set_value</c> commits it and says what came of it
/// (#625). The row's text box commits on focus loss; neither Enter nor <c>set_value</c> caused one, so a person
/// pressing Enter saw nothing change, and <c>set_value</c> answered success for a property it never set.
/// </summary>
public class PropertyRowCommitTests
{
    private sealed class RowVm : INotifyPropertyChanged
    {
        private object? value = "Form1";

        /// <summary>
        /// Takes any value, then refuses one containing "bad" by putting the old one back from a posted callback.
        /// That is the Properties window's row: it stores what the binding wrote, and its refusal reverts it later,
        /// because a change raised during the binding's write would not reach the box.
        /// </summary>
        public object? Value
        {
            get => value;
            set
            {
                var old = this.value;
                this.value = value;
                Raise();
                if (value is string s && s.Contains("bad"))
                    Dispatcher.UIThread.Post(() => { this.value = old; Raise(); });
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
    }

    private static (Window Window, TextBox Editor, RowVm Row) Row()
    {
        var row = new RowVm();
        // The theme's own template, rebuilt here: PropertyBox.axaml is compiled internal to HexIDE, so a test
        // assembly cannot load it. A panel named PART_Panel, and a text box bound to the box's Object with
        // UpdateSourceTrigger=LostFocus, as that file declares.
        var box = new PropertyBox
        {
            Template = new FuncControlTemplate<PropertyBox>((_, scope) => new Panel { Name = "PART_Panel" }.RegisterInNameScope(scope)),
            GenericTemplate = new FuncDataTemplate<object?>((_, _) =>
            {
                var editor = new TextBox();
                editor.Bind(TextBox.TextProperty, new Binding(nameof(PropertyBox.Object))
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(PropertyBox) },
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
                });
                return editor;
            }),
            PropertyClass = VBProperties.CaptionProperty,
            DataContext = row,
        };
        box.Bind(PropertyBox.ObjectProperty, new Binding(nameof(RowVm.Value)) { Mode = BindingMode.TwoWay });
        var window = new Window { Content = box, Width = 300, Height = 100 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = box.GetVisualDescendants().OfType<TextBox>().Single();
        return (window, editor, row);
    }

    [AvaloniaFact]
    public void Typing_alone_commits_nothing()
    {
        var (window, editor, row) = Row();
        try
        {
            editor.Text = "Hello";
            row.Value.Should().Be("Form1", "the row commits on focus loss or Enter, not per keystroke");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Enter_commits_the_row()
    {
        var (window, editor, row) = Row();
        try
        {
            editor.Text = "Hello";
            UiAutomationDriver.PressKey(editor, "Enter", null).Success.Should().BeTrue();
            row.Value.Should().Be("Hello");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Set_value_commits_and_says_so()
    {
        var (window, editor, row) = Row();
        try
        {
            var outcome = UiAutomationDriver.Interact(editor, "set_value", "Hello");

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Detail.Should().EndWith(", and committed it");
            row.Value.Should().Be("Hello");
            Dispatcher.UIThread.RunJobs();
            UiAutomationDriver.RefusedCommit(editor, "Hello").Should().BeNull("the value held");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Set_value_on_a_refused_value_says_what_the_row_shows_instead()
    {
        var (window, editor, row) = Row();
        try
        {
            UiAutomationDriver.Interact(editor, "set_value", "a bad caption").Success.Should().BeTrue();

            // What the interact tool does next: let the source's posted reaction run, then read the box back.
            Dispatcher.UIThread.RunJobs();

            UiAutomationDriver.RefusedCommit(editor, "a bad caption")
                .Should().StartWith(", but after committing it shows 'Form1'");
            row.Value.Should().Be("Form1");
            editor.Text.Should().Be("Form1");
        }
        finally { window.Close(); }
    }
}
