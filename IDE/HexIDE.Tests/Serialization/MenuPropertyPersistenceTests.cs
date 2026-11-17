using HexIDE.IDE;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.Serialization;

// VB6 SP6 measurements: https://github.com/hexide-io/HexIDE/issues/4141
// Save-format measurements: https://github.com/hexide-io/HexIDE/pull/4306.
public class MenuPropertyPersistenceTests
{
    private static FormDefinition Read(string properties) => new FormDeserializer().Deserialize(
        new ProjectDefinition(VBProjectType.EXE, "P"),
        "VERSION 5.00\nBegin VB.Form Form1\n Begin VB.Menu mnuItem\n Caption = \"Item\"\n"
        + properties + "\n End\nEnd\nAttribute VB_Name = \"Form1\"\n", Substitute.For<IDeserializeErrorSink>())!;

    private static string Save(FormDefinition form) => new FormSerializer().Serialize(form, "Form1.frm").frmText.ReplaceLineEndings("\n");

    [Theory]
    [InlineData("Shortcut", "^O", "Shortcut        =   ^O")]
    [InlineData("HelpContextID", 7, "HelpContextID   =   7")]
    [InlineData("NegotiatePosition", 1, "NegotiatePosition=   1  'Left")]
    [InlineData("NegotiatePosition", 2, "NegotiatePosition=   2  'Middle")]
    [InlineData("NegotiatePosition", 3, "NegotiatePosition=   3  'Right")]
    [InlineData("Index", 0, "Index           =   0")]
    public void Editor_property_is_applied_saved_reloaded_and_unchanged_on_second_apply(string property, object value, string line)
    {
        var form = Read("");
        var item = form.Components.Single(c => c.BaseClass is MenuComponentClass);
        var editor = new MenuViewModel(item);
        var member = typeof(MenuViewModel).GetProperty(property);
        member.Should().NotBeNull();
        member!.SetValue(editor, value);
        var edits = new List<string>();
        item.OnComponentPropertyChanged += (_, changed) => edits.Add(changed.Name);
        editor.Apply(item);
        edits.Should().Equal(property);
        var saved = Save(form);
        saved.Should().Contain(line);
        var loaded = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), saved,
            Substitute.For<IDeserializeErrorSink>())!;
        var loadedItem = loaded.Components.Single(c => c.BaseClass is MenuComponentClass);
        var reopened = new MenuViewModel(loadedItem);
        member.GetValue(reopened).Should().Be(value);
        reopened.HasChanges.Should().BeFalse();
        var changed = 0;
        loadedItem.OnComponentPropertyChanged += (_, _) => changed++;
        reopened.Apply(loadedItem);
        changed.Should().Be(0);
        Save(loaded).Should().Be(saved);
    }

    [Fact]
    public void Index_set_in_the_editor_is_the_shared_Index_and_is_written_in_the_measured_order()
    {
        // Index is every control's preserved line (#4284), so the editor reads and writes it through DesignIndexProperty.
        var form = Read(" HelpContextID = 7\n NegotiatePosition = 2\n Shortcut = ^O");
        var item = form.Components.Single(c => c.BaseClass is MenuComponentClass);
        var editor = new MenuViewModel(item) { Index = 3 };
        editor.Apply(item);
        item.GetBoxedPropertyOrDefault(VBProperties.DesignIndexProperty).Should().Be("3");
        var text = Save(form);
        text.Should().Contain("Index           =   3");
        new[] { "HelpContextID", "Index", "NegotiatePosition", "Shortcut" }
            .Select(n => text.IndexOf(n, StringComparison.Ordinal)).Should().BeInAscendingOrder();
        item.SetUntypedProperty(VBProperties.DesignIndexProperty, "5");
        new MenuViewModel(item).Index.Should().Be(5);
    }

    [Theory]
    [InlineData("Shortcut", "^O", "")]
    [InlineData("HelpContextID", "7", 0)]
    [InlineData("NegotiatePosition", "1", 0)]
    [InlineData("Index", "0", null)]
    public void Clearing_an_editor_property_removes_its_saved_line(string property, string oldValue, object? defaultValue)
    {
        var form = Read(" " + property + " = " + oldValue);
        var item = form.Components.Single(c => c.BaseClass is MenuComponentClass);
        var editor = new MenuViewModel(item);
        var member = typeof(MenuViewModel).GetProperty(property);
        member.Should().NotBeNull();
        member!.SetValue(editor, defaultValue);
        editor.Apply(item);
        Save(form).Should().NotContain(property);
    }

    [Theory]
    [InlineData("^+{F1}", "+^{F1}")]
    [InlineData("{DELETE}", "{DEL}")]
    [InlineData("%{BACKSPACE}", "%{BKSP}")]
    [InlineData("^{INS}", "")]
    [InlineData("+{INS}", "")]
    public void Loaded_shortcut_uses_the_measured_save_spelling(string input, string output)
    {
        var saved = Save(Read(" Shortcut = " + input));
        if (output.Length == 0)
            saved.Should().NotContain("Shortcut");
        else
            saved.Should().Contain("Shortcut        =   " + output + "\n");
    }

    [Fact]
    public void Measured_menu_defaults_are_omitted_even_when_loaded_explicitly()
    {
        Save(Read(" Enabled = -1\n Visible = -1\n HelpContextID = 0\n NegotiatePosition = 0"))
            .Should().NotContainAny("Enabled", "Visible", "HelpContextID", "NegotiatePosition");
    }

    [Fact]
    public void All_properties_follow_the_measured_order_and_submenu_WindowList_is_dropped()
    {
        var form = Read(" WindowList = -1\n Visible = 0\n Shortcut = ^O\n NegotiatePosition = 2\n Index = 0\n HelpContextID = 7\n Enabled = 0\n Checked = -1");
        var text = Save(form);
        var names = new[] { "Caption", "Checked", "Enabled", "HelpContextID", "Index", "NegotiatePosition", "Shortcut", "Visible", "WindowList" };
        names.Select(n => text.IndexOf(n, StringComparison.Ordinal)).Should().BeInAscendingOrder();
        var parent = form.Components.Single(c => c.BaseClass is MenuComponentClass);
        var child = new ComponentInstance(MenuComponentClass.Instance, "mnuChild");
        child.SetProperty(VBProperties.WindowListProperty, true);
        child.SetProperty(VBProperties.CaptionProperty, "Child");
        parent.SetProperty(MenuComponentClass.SubItemsProperty, new List<ComponentInstance> { child });
        form.UpdateComponents([.. form.Components, child]);
        text = Save(form);
        text.Split("WindowList").Should().HaveCount(2, "only the top-level menu retains WindowList");
        text[(text.IndexOf("Begin VB.Menu mnuChild", StringComparison.Ordinal))..].Should().NotContain("WindowList");
    }
}
