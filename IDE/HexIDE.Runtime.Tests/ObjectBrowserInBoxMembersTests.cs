using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Every member the Object Browser lists for an in-box class or library, against a listing generated from the code before
/// move slice 2.4d (#5610) took those members out of the runtime's in-box types. The members it lists, their kinds and their
/// HexIDE-only notes must not change while they move.
/// </summary>
public class ObjectBrowserInBoxMembersTests
{
    // The Object Browser finds a table through the interface, so a class that keeps one without saying so loses its members.
    [Fact]
    public void Every_class_that_keeps_a_member_table_declares_it()
    {
        var keeping = typeof(ComponentBaseClass).Assembly.GetTypes()
            .Where(type => typeof(ComponentBaseClass).IsAssignableFrom(type)
                && type.GetProperty(nameof(ComponentBaseClass.Members))!.DeclaringType != typeof(ComponentBaseClass))
            .ToList();

        keeping.Should().NotBeEmpty();
        keeping.Where(type => !typeof(IComponentMemberTable).IsAssignableFrom(type)).Should().BeEmpty();
    }
}
