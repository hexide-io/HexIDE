using Avalonia.Input;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Runtime.Integration.Tests.Automation;

/// <summary>
/// inspect_element says which mouse pointer a control shows and where it is set (#2453). Nothing did: a program's
/// MousePointer, Screen.MousePointer over it, or a TextBox's I-beam could not be checked in the running IDE, as a
/// snapshot draws no pointer and Avalonia's Cursor does not say what it shows.
/// </summary>
public class InspectPointerTests
{
    [AvaloniaFact]
    public void Each_cursor_the_runtime_makes_is_its_own_and_remembers_its_pointer()
    {
        var first = VBCursorType.Hourglass.ToCursor();
        var second = VBCursorType.Hourglass.ToCursor();

        first.Should().NotBeSameAs(second, "callers compare cursors by reference, so recording must not cache them");
        VBCursorTypeExtensions.PointerOf(second).Should().Be(VBCursorType.Hourglass);
        VBCursorTypeExtensions.PointerOf(new Cursor(StandardCursorType.Wait)).Should().BeNull();
        VBCursorTypeExtensions.PointerOf(null).Should().BeNull();
    }
}
