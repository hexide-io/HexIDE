using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Runtime.BuiltinControls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Runtime.Components;
using static HexIDE.Runtime.Integration.Tests.Views.UserControlKeyTests;

namespace HexIDE.Runtime.Integration.Tests.Views;

// VB6 SP6 native/p-code: #4371's measured rows (oracle PR #4376), and
// docs/vb6-fidelity-oracle/key-events-keydown-keypress-and-keyup.md.
public class CommandButtonDefaultCancelTests
{
    private static string DialogBlocks(string source = "VB.TextBox", string settings = "", string roles = "Default = -1") =>
        Block(source, "Text1", 0, extra: settings)
        + Block("VB.CommandButton", "Command1", 1, extra: roles)
        + Block("VB.CommandButton", "Command2", 2, extra: "Cancel = -1");

    private static string DialogCode =>
        "Private Sub Command1_Click()\nDebug.Print \"default\"\nEnd Sub\n"
        + "Private Sub Command2_Click()\nDebug.Print \"cancel\"\nEnd Sub\n"
        + string.Concat(new[] { "Text1", "Command1", "Form" }.Select(name =>
            $"Private Sub {name}_KeyDown(KeyCode As Integer, Shift As Integer)\nDebug.Print \"{name} down\" & CStr(KeyCode)\nEnd Sub\n"
            + $"Private Sub {name}_KeyPress(KeyAscii As Integer)\nDebug.Print \"{name} press\" & CStr(KeyAscii)\nEnd Sub\n"
            + $"Private Sub {name}_KeyUp(KeyCode As Integer, Shift As Integer)\nDebug.Print \"{name} up\" & CStr(KeyCode)\nEnd Sub\n"));

    // #4391; docs/vb6-fidelity-oracle/default-and-cancel-buttons.md, focus table.
    [AvaloniaTheory]
    [InlineData(0, "Command1")]
    [InlineData(1, "Command1")]
    [InlineData(2, "Command2")]
    [InlineData(3, "Command3")]
    public void Default_look_follows_the_real_Tab_route(int tabs, string expected)
    {
        using var run = new Run("", blocks: DialogBlocks() + Block("VB.CommandButton", "Command3", 3));
        run.Named("Text1").Focus().Should().BeTrue();
        for (var i = 0; i < tabs; i++)
            run.Key(Key.Tab);
        run.Named(tabs == 0 ? "Text1" : $"Command{tabs}").IsFocused.Should().BeTrue();
        foreach (var name in new[] { "Command1", "Command2", "Command3" })
            run.Named(name).Classes.Contains(":default").Should().Be(name == expected);
    }

    // #4391 step 3 explicitly decides runtime changes while a TextBox has focus.
    [AvaloniaFact]
    public async Task Default_look_moves_when_the_program_moves_or_clears_Default()
    {
        using var run = new Run("", blocks: DialogBlocks());
        run.Named("Text1").Focus().Should().BeTrue();
        await run.Code("Command2.Default = True");
        run.Named("Command1").Classes.Contains(":default").Should().BeFalse();
        run.Named("Command2").Classes.Contains(":default").Should().BeTrue();
        await run.Code("Command2.Default = False");
        run.Named("Command1").Classes.Contains(":default").Should().BeFalse();
        run.Named("Command2").Classes.Contains(":default").Should().BeFalse();
    }

    // #4391's pixel measurements (oracle PR #4577); 150% uses the issue's DIP decision.
    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void Default_frame_occupies_the_outer_edge_without_resizing_or_moving_the_caption(double scaling)
    {
        using var run = new Run("", blocks: DialogBlocks());
        run.Window.SetRenderScaling(scaling);
        run.Named("Text1").Focus().Should().BeTrue();
        Dispatcher.UIThread.RunJobs();
        var button = (VBCommandButton)run.Named("Command1");
        var plain = (VBCommandButton)run.Named("Command2");
        button.Bounds.Size.Should().Be(plain.Bounds.Size);
        var bounds = button.Bounds;
        var presenter = button.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter");
        var caption = presenter.TranslatePoint(default, button);
        using var frame = run.Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame");
        using var buffer = frame.Lock();
        var origin = button.TranslatePoint(default, run.Window)!.Value;
        Color Pixel(double x, double y)
        {
            var offset = (int)((origin.Y + y) * scaling) * buffer.RowBytes + (int)((origin.X + x) * scaling) * 4;
            byte At(int k) => Marshal.ReadByte(buffer.Address, offset + k);
            return buffer.Format == Avalonia.Platform.PixelFormat.Bgra8888
                ? Color.FromRgb(At(2), At(1), At(0)) : Color.FromRgb(At(0), At(1), At(2));
        }
        foreach (var point in new[] { new Point(0, 10), new Point(bounds.Width - 0.5, 10),
                     new Point(10, 0), new Point(10, bounds.Height - 0.5) })
            Pixel(point.X, point.Y).Should().Be(Color.FromRgb(100, 100, 100));
        run.Named("Command2").Focus().Should().BeTrue();
        Dispatcher.UIThread.RunJobs();
        button.Bounds.Should().Be(bounds);
        presenter.TranslatePoint(default, button).Should().Be(caption);
    }

    [AvaloniaTheory]
    [InlineData("VB.TextBox", "", false)]
    [InlineData("VB.TextBox", "MultiLine = -1", false)]
    [InlineData("VB.ListBox", "", false)]
    [InlineData("VB.TextBox", "", true)]
    [InlineData("VB.TextBox", "MultiLine = -1", true)]
    [InlineData("VB.ListBox", "", true)]
    public void Dialog_keys_click_the_holder_without_any_key_event(string source, string settings, bool preview)
    {
        using var run = new Run("", formCode: DialogCode, blocks: DialogBlocks(source, settings));
        if (preview)
            HexIDE.Runtime.BuiltinControls.AttachedProperties.SetKeyPreview(run.Window, true);
        run.Named("Text1").Focus().Should().BeTrue();
        run.Key(Key.Enter);
        run.Key(Key.Escape);
        run.Lib.Printed.Should().Equal(new Vb6Value("default"), new Vb6Value("cancel"));
    }

    [AvaloniaFact]
    public void Focused_button_owns_Enter_even_without_a_Default()
    {
        using var run = new Run("", formCode: DialogCode, blocks: DialogBlocks(roles: ""));
        run.Named("Command1").Focus().Should().BeTrue();
        run.Key(Key.Enter);
        run.Lib.Printed.Should().Equal(new Vb6Value("default"));
    }

    [AvaloniaFact]
    public void Escape_on_a_focused_button_goes_to_the_Cancel_holder()
    {
        using var run = new Run("", formCode: DialogCode, blocks: DialogBlocks());
        run.Named("Command1").Focus().Should().BeTrue();
        run.Key(Key.Escape);
        run.Lib.Printed.Should().Equal(new Vb6Value("cancel"));
    }

    [AvaloniaFact]
    public async Task Roles_can_be_read_moved_shared_and_cleared_by_a_program()
    {
        using var run = new Run("", formCode: DialogCode, blocks: DialogBlocks());
        run.Named("Text1").Focus().Should().BeTrue();
        await run.Code("Debug.Print Command1.Default\nDebug.Print Command1.Cancel\nCommand2.Default = True\nDebug.Print Command1.Default\nDebug.Print Command2.Default");
        run.Key(Key.Enter);
        await run.Code("Command1.Cancel = True\nCommand1.Default = True\nDebug.Print Command2.Cancel");
        run.Key(Key.Enter);
        run.Key(Key.Escape);
        await run.Code("Command1.Default = False\nCommand1.Cancel = False");
        run.Key(Key.Enter);
        run.Key(Key.Escape);
        run.Lib.Printed.Should().Equal(new Vb6Value(true), new Vb6Value(false), new Vb6Value(false), new Vb6Value(true),
            new Vb6Value("cancel"), new Vb6Value(false), new Vb6Value("default"), new Vb6Value("default"),
            new Vb6Value("Text1 down13"), new Vb6Value("Text1 press13"), new Vb6Value("Text1 up13"),
            new Vb6Value("Text1 down27"), new Vb6Value("Text1 press27"), new Vb6Value("Text1 up27"));
    }

    [AvaloniaTheory]
    [InlineData("Enabled")]
    [InlineData("Visible")]
    public async Task Ineligible_holders_keep_their_roles_but_do_not_take_keys(string property)
    {
        using var run = new Run("", formCode: DialogCode, blocks: DialogBlocks());
        run.Named("Text1").Focus().Should().BeTrue();
        await run.Code($"Command1.{property} = False\nCommand2.{property} = False\nDebug.Print Command1.Default\nDebug.Print Command2.Cancel");
        run.Key(Key.Enter);
        run.Key(Key.Escape);
        run.Lib.Printed.Should().Equal(new Vb6Value(true), new Vb6Value(true),
            new Vb6Value("Text1 down13"), new Vb6Value("Text1 press13"), new Vb6Value("Text1 up13"),
            new Vb6Value("Text1 down27"), new Vb6Value("Text1 press27"), new Vb6Value("Text1 up27"));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Last_file_holder_wins_both_roles(bool reverse)
    {
        var first = Block("VB.CommandButton", "Command1", 1, extra: "Default = -1\nCancel = -1");
        var second = Block("VB.CommandButton", "Command2", 2, extra: "Default = -1\nCancel = -1");
        using var run = new Run("", formCode: DialogCode, blocks: Block("VB.TextBox", "Text1", 0) + (reverse ? second + first : first + second));
        run.Named("Text1").Focus().Should().BeTrue();
        await run.Code("Debug.Print Command1.Default\nDebug.Print Command2.Cancel");
        run.Key(Key.Enter);
        run.Key(Key.Escape);
        run.Lib.Printed.Should().Equal(new Vb6Value(reverse), new Vb6Value(!reverse),
            new Vb6Value(reverse ? "default" : "cancel"), new Vb6Value(reverse ? "default" : "cancel"));
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Without_a_holder_TextBox_receives_all_three_key_events(bool multiline, bool preview)
    {
        using var run = new Run("", formCode: DialogCode, blocks: DialogBlocks(settings: multiline ? "MultiLine = -1" : "", roles: "").Replace("Cancel = -1", ""));
        HexIDE.Runtime.BuiltinControls.AttachedProperties.SetKeyPreview(run.Window, preview);
        var box = (HexIDE.Runtime.BuiltinControls.VBTextBox)run.Named("Text1");
        box.Focus().Should().BeTrue();
        box.Text = "a";
        box.CaretIndex = 1;
        box.SelectionStart = box.SelectionEnd = 1;
        run.Key(Key.Enter);
        run.Key(Key.Escape);
        var expected = new[] { "down13", "press13", "up13", "down27", "press27", "up27" }
            .SelectMany(ev => preview ? new[] { "Form " + ev, "Text1 " + ev } : new[] { "Text1 " + ev });
        run.Lib.Printed.Select(value => value.Value).Should().Equal(expected);
        box.Text.Should().Be(multiline ? "a\r\n" : "a");
    }

    [AvaloniaTheory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Escape)]
    public async Task Open_combo_keeps_the_key_instead_of_clicking_a_button(Key key)
    {
        using var run = new Run("", formCode: DialogCode, blocks: DialogBlocks("VB.ComboBox", "Style = 2"));
        await run.Code("Text1.AddItem \"apple\"");
        var combo = (HexIDE.Runtime.BuiltinControls.VBComboBox)run.Named("Text1");
        combo.Focus().Should().BeTrue();
        combo.IsDropDownOpen = true;
        run.Key(key);
        combo.IsDropDownOpen.Should().BeFalse();
        var code = key == Key.Enter ? 13 : 27;
        run.Lib.Printed.Should().Equal(new Vb6Value($"Text1 down{code}"), new Vb6Value($"Text1 press{code}"));
    }

    [AvaloniaFact]
    public void Default_and_Cancel_are_real_serialized_properties()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project,
            "VERSION 5.00\nBegin VB.Form Form1\n" + DialogBlocks(roles: "Default = -1\nCancel = -1") + "End\n", new Sink())!;
        var button = form.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "Command1");
        button.GetPropertyOrDefault((PropertyClass<bool>)CommandButtonComponentClass.Instance.PropertiesByName["Default"]).Should().BeTrue();
        button.GetPropertyOrDefault((PropertyClass<bool>)CommandButtonComponentClass.Instance.PropertiesByName["Cancel"]).Should().BeTrue();
        var text = new FormSerializer().Serialize(form, "Form1.frm").frmText;
        var loaded = new FormDeserializer().Deserialize(project, text, new Sink())!;
        loaded.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "Command1").GetPropertyOrDefault((PropertyClass<bool>)CommandButtonComponentClass.Instance.PropertiesByName["Default"]).Should().BeTrue();
        loaded.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "Command1").GetPropertyOrDefault((PropertyClass<bool>)CommandButtonComponentClass.Instance.PropertiesByName["Cancel"]).Should().BeTrue();
    }
}
