using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;

namespace HexIDE.Runtime.Tests;

// Member-table contract: https://github.com/hexide-io/HexIDE/issues/1116.
public class ListMemberTableTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Tables_mark_code_only_extensions_and_existing_VB_members(bool combo)
    {
        ComponentBaseClass component = combo ? ComboBoxComponentClass.Instance : ListBoxComponentClass.Instance;
        foreach (var name in new[] { "FindString", "FindStringExact" })
        {
            var row = component.Members.Single(m => m.Name == name);
            row.Kind.Should().Be(MemberKind.Method);
            row.Origin.Should().Be(MemberOrigin.HexIde);
            row.IsFrmProperty.Should().BeFalse();
        }
        foreach (var prop in component.Properties)
            component.Members.Should().Contain(m => m.Name == prop.Name.Trim('(', ')') && m.Origin == MemberOrigin.VB6);
        foreach (var evt in component.Events)
            component.Members.Should().Contain(m => m.Name == evt.Name && m.Kind == MemberKind.Event);
        component.Members.Should().Contain(m => m.Name == "ListCount" && !m.IsFrmProperty);
        // Not listed until repaint deferral exists: the Object Browser would advertise a member that always fails (#1116).
        component.Members.Should().NotContain(m => m.Name == "BeginUpdate" || m.Name == "EndUpdate");
    }

}
