using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Serialization;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Tests;

public class MaskedEdBoxRuntimeTests : BaseVBTestFixture
{
    private Control Create()
    {
        FormDeserializer.TryGetComponentClass("MSMask.MaskEdBox", out var component).Should().BeTrue();
        return component!.Instantiate(new ComponentInstance(component, "box"));
    }

    [Fact]
    public void Registered_component_has_the_measured_members_and_no_Text_designer_row()
    {
        FormDeserializer.TryGetComponentClass("MSMask.MaskEdBox", out var component).Should().BeTrue();
        var box = component.Should().BeAssignableTo<InBoxComponentClass>().Subject;
        box.Library.Should().Be(ControlLibraries.MSMask);
        box.Properties.Should().NotContain(p => p.Name == "Text");
        box.Member("ClipText")!.Implemented.Should().BeTrue();
        box.Member("hWnd")!.Implemented.Should().BeFalse();
        foreach (var name in new[] { "ValidationError", "KeyDown", "KeyPress", "KeyUp" })
        {
            box.Member(name)!.Implemented.Should().BeTrue();
            box.Events.Should().Contain(e => e.Name == name);
        }
        box.Members.Should().NotContain(m => m.Name == "Click");
    }

    [Theory]
    [InlineData(true, "1_-3_", "1_-3_", "13", "1 -3 ")]
    [InlineData(false, "13", "13", "13", "13-  ")]
    [InlineData(false, "12-34", "1234", "1234", "12-34")]
    public async Task Interpreter_reads_the_measured_text_views(bool include, string assigned, string text, string clip, string formatted)
    {
        context.AllocVariable(rootEnv, "box", new Vb6Value(Create()));
        await Run($"box.Mask = \"##-##\"\nbox.PromptInclude = {(include ? "True" : "False")}\nbox.Text = \"{assigned}\"\nDebug.Print box.Text\nDebug.Print box.ClipText\nDebug.Print box.FormattedText\nDebug.Print CStr(box)");
        AssertDebugLog([new(text), new(clip), new(formatted), new(text)]);
    }

    [Fact]
    public async Task Interpreter_reads_selection_under_both_ClipModes()
    {
        context.AllocVariable(rootEnv, "box", new Vb6Value(Create()));
        await Run("box.Mask = \"##-##\"\nbox.Text = \"12-34\"\nbox.SelStart = 1\nbox.SelLength = 3\nDebug.Print box.SelStart\nDebug.Print box.SelLength\nDebug.Print box.SelText\nbox.ClipMode = 1\nDebug.Print box.SelText");
        AssertDebugLog([new(1L), new(3L), new("2-3"), new("23")]);
    }

    [Fact]
    public async Task Interpreter_writes_SelText_using_the_measured_property_probe()
    {
        context.AllocVariable(rootEnv, "box", new Vb6Value(Create()));
        await Run("""
            box.Mask = "##-##"
            box.Text = "12-34"
            box.SelStart = 1
            box.SelLength = 3
            box.SelText = "99"
            Debug.Print box.Text
            Debug.Print box.ClipText
            Debug.Print box.SelStart
            """);
        AssertDebugLog([new("19-94"), new("1994"), new(0L)]);
    }

    [Fact]
    public async Task Runtime_defaults_and_measured_scalar_assignments_are_bound()
    {
        context.AllocVariable(rootEnv, "box", new Vb6Value(Create()));
        await Run("Debug.Print TypeName(box)\nDebug.Print box.Mask\nDebug.Print box.Text\nDebug.Print box.MaxLength\nDebug.Print box.PromptChar\nDebug.Print box.PromptInclude\nDebug.Print box.AllowPrompt\nDebug.Print box.AutoTab\nDebug.Print box.ClipMode\nbox.AllowPrompt = True\nbox.AutoTab = True\nbox.MaxLength = 5\nbox.Text = \"abcdefgh\"\nDebug.Print box.AllowPrompt\nDebug.Print box.AutoTab\nDebug.Print box.MaxLength");
        AssertDebugLog([new("MaskEdBox"), new(""), new(""), new(64), new("_"), new(true), new(false),
            new(false), new(0L), new(true), new(true), new(8)]);
    }

    [Theory]
    [InlineData("Refresh")]
    [InlineData("OLEDrag")]
    public async Task Unfinished_methods_refuse_through_the_interpreter(string method)
    {
        context.AllocVariable(rootEnv, "box", new Vb6Value(Create()));
        Func<Task> call = () => Run($"box.{method}");
        await call.Should().ThrowAsync<NotImplementedException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unmeasured_MaxLength_narrowing_and_large_readings_refuse(bool largeText)
    {
        var bag = (ICSharpPropertyBag)Create();
        Action access;
        if (largeText)
        {
            bag.TrySetProperty("Text", new(new string('a', short.MaxValue + 1)));
            access = () => bag.TryGetProperty("MaxLength", out _);
        }
        else access = () => bag.TrySetProperty("MaxLength", new(5L));
        access.Should().Throw<NotImplementedException>();
    }

    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string message) { }
    }

    [Fact]
    public void A_loaded_box_can_save_its_measured_defaults()
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"),
            "VERSION 5.00\nBegin VB.Form Form1\n   Begin MSMask.MaskEdBox Mask1\n   End\nEnd\nAttribute VB_Name = \"Form1\"\n", new Sink())!;
        form.Components.Should().Contain(c => c.BaseClass.VBTypeName == "MSMask.MaskEdBox");
        form.CanSaveFaithfully.Should().BeTrue();
        new FormSerializer().Serialize(form, "Form1.frm").frmText.Should().Contain("PromptChar");
        var withIcon = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"),
            "VERSION 5.00\nBegin VB.Form Form1\n   Begin MSMask.MaskEdBox Mask1\n      MouseIcon = 0\n   End\nEnd\n", new Sink())!;
        withIcon.CanSaveFaithfully.Should().BeFalse();
        Action save = () => new FormSerializer().Serialize(withIcon, "Form1.frm");
        save.Should().Throw<NotImplementedException>().WithMessage("*#1098*");
    }

    [Theory]
    [InlineData("Format")]
    [InlineData("hWnd")]
    [InlineData("HideSelection")]
    [InlineData("MouseIcon")]
    [InlineData("FontName")]
    public void Unfinished_members_refuse_instead_of_guessing(string name)
    {
        var bag = (ICSharpPropertyBag)Create();
        Action read = () => bag.TryGetProperty(name, out _);
        read.Should().Throw<NotImplementedException>();
    }
}
