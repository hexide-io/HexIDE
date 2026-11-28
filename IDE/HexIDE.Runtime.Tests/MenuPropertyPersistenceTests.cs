using HexIDE.IDE;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

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
