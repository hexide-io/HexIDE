using System.Collections.Generic;
using System.Threading.Tasks;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

// #1631, issuecomment-6425849231: vb6.exe SP6, native and p-code agreeing.
public class UserControlExtenderTests : BaseVBTestFixture
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string error) { }
    }

    private void Place(string settings, bool late)
    {
        var definition = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "probe"),
            $"VERSION 5.00\nBegin VB.UserControl Dial\n{settings}\nEnd\nAttribute VB_Name = \"Dial\"\n", new Sink())!;
        var type = UserControlComponentClass.ForModule("probe.Dial", definition, new Dictionary<string, ComponentBaseClass>());
        var control = type.Instantiate(new ComponentInstance(type, "uc1"));
        context.AllocVariable(rootEnv, "uc1", new Vb6Value(control), declaredClass: late ? "Object" : "Dial");
    }

    public static TheoryData<string, string> Missing => new()
    {
        { "CanGetFocus = 0", "TabIndex" },
        { "CanGetFocus = 0", "TabStop" },
        { "CanGetFocus = 0", "CausesValidation" },
        { "CanGetFocus = 0", "HelpContextID" },
        // #1631 part B, issuecomment-6425849231.
        { "ForwardFocus = -1", "TabStop" },
        { "ForwardFocus = -1", "CausesValidation" },
        { "ForwardFocus = -1", "HelpContextID" },
        { "", "Enabled" },
        { "", "DataField" },
        { "DataBindingBehavior = 1", "DataField" },
        { "InvisibleAtRuntime = -1", "Visible" },
        { "InvisibleAtRuntime = -1", "Width" },
        { "InvisibleAtRuntime = -1", "Height" },
        { "InvisibleAtRuntime = -1", "Container" },
        { "InvisibleAtRuntime = -1", "TabIndex" },
        { "InvisibleAtRuntime = -1", "TabStop" },
        { "InvisibleAtRuntime = -1", "Tag" },
    };

    [Theory]
    [MemberData(nameof(Missing))]
    public async Task An_early_bound_missing_member_is_461(string settings, string member)
    {
        Place(settings, false);
        var act = () => Run($"Debug.Print uc1.{member}\n");
        await act.Should().ThrowAsync<VBMethodOrDataMemberNotFoundException>();
    }

    [Theory]
    [MemberData(nameof(Missing))]
    public async Task A_late_bound_missing_member_is_438_on_read_and_write(string settings, string member)
    {
        Place(settings, true);
        await Run($"On Error Resume Next\nDebug.Print uc1.{member}\nDebug.Print Err.Number\nErr.Clear\nuc1.{member} = 1\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(438L), new Vb6Value(438L)]);
    }

    [Theory]
    [InlineData("CanGetFocus = 0", false, 461)]
    [InlineData("CanGetFocus = 0", true, 438)]
    [InlineData("ForwardFocus = -1", false, 461)]
    [InlineData("ForwardFocus = -1", true, 438)]
    [InlineData("InvisibleAtRuntime = -1", false, 461)]
    [InlineData("InvisibleAtRuntime = -1", true, 438)]
    public async Task SetFocus_is_absent_in_both_call_forms(string settings, bool late, int error)
    {
        Place(settings, late);
        if (!late)
        {
            var act = () => Run("uc1.SetFocus\n");
            await act.Should().ThrowAsync<VBMethodOrDataMemberNotFoundException>();
            return;
        }
        await Run("On Error Resume Next\nuc1.SetFocus\nDebug.Print Err.Number\nErr.Clear\nCall uc1.SetFocus\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value((long)error), new Vb6Value((long)error)]);
    }

    [Theory]
    [InlineData("Left")]
    [InlineData("Top")]
    public async Task Invisible_position_reads_are_393(string member)
    {
        Place("InvisibleAtRuntime = -1", true);
        await Run($"On Error Resume Next\nDebug.Print uc1.{member}\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(393L)]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("CanGetFocus = -1\nWindowless = 0\nInvisibleAtRuntime = 0")]
    [InlineData("Windowless = -1")]
    public async Task A_focusable_extender_exposes_TabIndex_and_TabStop(string settings)
    {
        Place(settings, false);
        await Run("uc1.TabIndex = 4\nuc1.TabStop = False\nDebug.Print uc1.TabIndex\nDebug.Print uc1.TabStop\n");
        // The assigned values are test inputs; the Integer/Boolean types are the existing TabOrder contract.
        AssertDebugLog([new Vb6Value(4), new Vb6Value(false)]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("CanGetFocus = 0")]
    [InlineData("InvisibleAtRuntime = -1")]
    [InlineData("Windowless = -1")]
    public async Task Name_and_Index_remain_members(string settings)
    {
        Place(settings, true);
        await Run("Debug.Print uc1.Name\nOn Error Resume Next\nDebug.Print uc1.Index\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value("uc1"), new Vb6Value(343L)]);
    }

    [Theory]
    [InlineData("Parent", "InvisibleAtRuntime = -1")]
    [InlineData("Object", "InvisibleAtRuntime = -1")]
    [InlineData("HelpContextID", "")]
    [InlineData("CausesValidation", "")]
    public async Task Present_members_without_bindings_are_refused_as_not_implemented(string member, string settings)
    {
        Place(settings, true);
        var act = () => Run($"Debug.Print uc1.{member}\n");
        await act.Should().ThrowAsync<NotImplementedException>();
    }

    [Fact]
    public async Task A_Variant_missing_member_is_late_bound()
    {
        Place("CanGetFocus = 0", false);
        await Run("Dim v\nSet v = uc1\nOn Error Resume Next\nDebug.Print v.TabIndex\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(438L)]);
    }

    // #1631 part B, issuecomment-6425849231: ForwardFocus keeps TabIndex; Default/Cancel start False.
    // Combined CanGetFocus/ForwardFocus: the later "Group 2 of 2" comment (2026-10-26) says ForwardFocus wins.
    [Theory]
    [InlineData("ForwardFocus = -1")]
    [InlineData("ForwardFocus = -1\nCanGetFocus = 0")]
    public async Task ForwardFocus_keeps_TabIndex(string settings)
    {
        Place(settings, false);
        await Run("uc1.TabIndex = 4\nDebug.Print uc1.TabIndex\n");
        AssertDebugLog([new Vb6Value(4)]);
    }

    // #1631 part B, issuecomment-6425849231.
    [Fact]
    public async Task DefaultCancel_adds_Boolean_Default_and_Cancel_initially_False()
    {
        Place("DefaultCancel = -1", false);
        await Run("Debug.Print uc1.Default\nDebug.Print uc1.Cancel\nuc1.Default = True\nuc1.Cancel = True\nDebug.Print uc1.Default\nDebug.Print uc1.Cancel\n");
        AssertDebugLog([new Vb6Value(false), new Vb6Value(false), new Vb6Value(true), new Vb6Value(true)]);
    }

    // Supervisor's native-handle decision, #1631, 2026-10-26: no fabricated HWNDs.
    [Fact]
    public async Task A_windowed_handle_is_explicitly_not_implemented()
    {
        Place("", false);
        var act = () => Run("Debug.Print uc1.hWnd\n");
        await act.Should().ThrowAsync<NotImplementedException>();
    }

    [Theory]
    [InlineData(false, 461)]
    [InlineData(true, 438)]
    public async Task A_windowless_handle_is_absent(bool late, int error)
    {
        Place("Windowless = -1", late);
        if (!late)
        {
            var act = () => Run("Debug.Print uc1.hWnd\n");
            await act.Should().ThrowAsync<VBMethodOrDataMemberNotFoundException>();
            return;
        }
        await Run("On Error Resume Next\nDebug.Print uc1.hWnd\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value((long)error)]);
    }
}
