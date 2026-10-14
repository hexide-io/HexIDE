using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// The Form member table read out of VB6.OLB (<c>vb6-form-members.json</c>, #995): that it loads as the type library
/// declares it, and that HexIDE's own Form component names nothing VB6's Form lacks.
/// </summary>
public class VB6FormMembersTests
{
    [Fact]
    public void The_table_holds_the_eighty_seven_members_of_VB6s_Form()
    {
        // 87 names on _Form, as the dump read them on the oracle VM; a regenerated file that lost some would show here.
        VB6FormMembers.All.Should().HaveCount(87);
    }

    [Theory]
    [InlineData("Caption", false, true, true)]
    [InlineData("Left", false, true, true)]
    [InlineData("hWnd", false, true, false)]
    [InlineData("Count", false, true, false)]
    [InlineData("Controls", false, true, false)]
    [InlineData("Hide", true, false, false)]
    [InlineData("Cls", true, false, false)]
    public void A_member_reads_as_the_type_library_declares_it(string name, bool isMethod, bool canGet, bool canLet)
    {
        VB6FormMembers.TryGet(name, out var member).Should().BeTrue();

        member.IsMethod.Should().Be(isMethod);
        member.CanGet.Should().Be(canGet);
        member.CanLet.Should().Be(canLet);
    }

    [Fact]
    public void A_method_carries_its_arguments_and_a_property_none()
    {
        VB6FormMembers.TryGet("Move", out var move).Should().BeTrue();
        (move.RequiredArgs, move.MaxArgs).Should().Be((1, 4), "Move Left, [Top], [Width], [Height]");
        VB6FormMembers.TryGet("left", out var left).Should().BeTrue("a name is looked up in any case");
        (left.RequiredArgs, left.MaxArgs).Should().Be((0, 0), "so Left(\"abc\", 1) can't be the form's Left");
    }

    [Fact]
    public void A_name_the_Form_lacks_is_not_in_the_table()
    {
        VB6FormMembers.TryGet("Print", out _).Should().BeFalse("VB6 compiles Print as syntax; it isn't in the type library");
        VB6FormMembers.TryGet("Score", out _).Should().BeFalse();
    }

    [Fact]
    public void Every_property_HexIDEs_Form_component_declares_is_a_member_of_VB6s_Form()
    {
        // The Properties window spells Name as (Name), as VB6's does; in code it is Name.
        foreach (var property in FormComponentClass.Instance.Properties)
            VB6FormMembers.TryGet(property.Name.Trim('(', ')'), out _).Should().BeTrue($"{property.Name} is a property of HexIDE's Form");
    }
}
