using System.Collections.Generic;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

// #1631, issuecomment-6426450412: VB6 SP6 IDE saves; native and p-code agreed.
public class UserControlFileTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string error) => throw new InvalidOperationException(error);
    }

    private static string Save(string settings, string properties)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var reader = new FormDeserializer();
        var part = reader.Deserialize(project,
            $"VERSION 5.00\nBegin VB.UserControl Dial\n{settings}\nEnd\n", new Sink())!;
        var registry = new Dictionary<string, ComponentBaseClass>();
        registry.Add("P.Dial", UserControlComponentClass.ForModule("P.Dial", part, registry));
        var form = reader.Deserialize(project,
            $"VERSION 5.00\nBegin VB.Form Form1\nBegin P.Dial Dial1\n{properties}\nEnd\nBegin VB.CommandButton Command1\nEnd\nEnd\n",
            new Sink(), extraComponents: registry)!;
        return new FormSerializer().Serialize(form, "Form1.frm").frmText.ReplaceLineEndings("\n");
    }

    [Theory]
    [InlineData("AccessKeys      =   \"q\"")]
    [InlineData("Alignable       =   -1  'True")]
    [InlineData("BackStyle       =   0  'Transparent")]
    [InlineData("CanGetFocus     =   0   'False")]
    [InlineData("ControlContainer=   -1  'True")]
    [InlineData("DataBindingBehavior=   1  'vbSimpleBound")]
    [InlineData("DefaultCancel   =   -1  'True")]
    [InlineData("ForwardFocus    =   -1  'True")]
    [InlineData("InvisibleAtRuntime=   -1  'True")]
    [InlineData("Windowless      =   -1  'True")]
    public void Ctl_settings_keep_VB6s_saved_spelling(string line)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var part = new FormDeserializer().Deserialize(project,
            $"VERSION 5.00\nBegin VB.UserControl Dial\n   {line}\nEnd\n", new Sink())!;
        new FormSerializer().Serialize(part, "Dial.ctl").frmText.ReplaceLineEndings("\n")
            .Should().Contain($"   {line}\n");
    }

    [Fact]
    public void Nonfocusable_instance_drops_TabIndex()
    {
        Save("CanGetFocus = 0", "TabIndex = 3").Should().NotContain("TabIndex");
    }

    [Fact]
    public void Invisible_instance_keeps_only_position_and_extents()
    {
        var saved = Save("InvisibleAtRuntime = -1",
            "Left = 120\nTop = 240\nHeight = 900\nWidth = 1800\nTabIndex = 3\nTag = \"discard\"\n_ExtentX = 3175\n_ExtentY = 1588");
        var block = saved[saved.IndexOf("Begin P.Dial", StringComparison.Ordinal)..];
        block = block[..block.IndexOf("End", StringComparison.Ordinal)];
        block.Should().Contain("Left").And.Contain("Top").And.Contain("_ExtentX = 3175").And.Contain("_ExtentY = 1588")
            .And.NotContain("Height").And.NotContain("Width").And.NotContain("TabIndex").And.NotContain("Tag");
    }

    [Fact]
    public void Invisible_instance_without_recorded_extents_can_render_for_read_only_queries()
    {
        // #1631 supervisor, 2026-10-27: a refusal belongs on the save path, not in read-only rendering.
        var save = () => Save("InvisibleAtRuntime = -1", "Height = 900\nWidth = 1800");
        save.Should().NotThrow();
    }

    [Fact]
    public void Windowless_instance_is_written_after_windowed_controls()
    {
        var saved = Save("Windowless = -1", "Left = 120");
        saved.IndexOf("Begin P.Dial", StringComparison.Ordinal).Should()
            .BeGreaterThan(saved.IndexOf("Begin VB.CommandButton", StringComparison.Ordinal));
    }
}
