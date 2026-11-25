using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using HexIDE.IDE;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Running;

namespace HexIDE.Integration.Tests.Automation;

public class InterpreterRunningProgramTests
{
    [AvaloniaFact]
    public void Only_a_program_menu_item_is_recognised()
    {
        RunningProgram.Current.IsMenuItem(new VBMenu()).Should().BeTrue();
        RunningProgram.Current.IsMenuItem(new MenuItem()).Should().BeFalse();
    }

    [AvaloniaFact]
    public void Nonvisual_program_controls_never_show()
    {
        RunningProgram.Current.NeverShows(new VBTimer()).Should().BeTrue();
        RunningProgram.Current.NeverShows(new VBCommonDialog(null!)).Should().BeTrue();
        RunningProgram.Current.NeverShows(new VBCommandButton()).Should().BeFalse();
    }

    [AvaloniaFact]
    public void Control_name_is_the_programs_own_name()
    {
        var control = new VBCommandButton();
        VBProps.SetName(control, "Command1");
        RunningProgram.Current.ControlName(control).Should().Be("Command1");
        RunningProgram.Current.ControlName(new Button()).Should().BeNull();
        RunningProgram.Current.ControlName(new object()).Should().BeNull();
    }

    [AvaloniaFact]
    public void Program_pointer_keeps_its_identity_and_standard_cursor()
    {
        using var cursor = VBCursorType.Hourglass.ToCursor();
        RunningProgram.Current.PointerOf(cursor).Should().Be(new ProgramPointer(VBCursorType.Hourglass,
            VBCursorType.Hourglass.ToStandardCursorType().ToString()));
        using var theme = new Cursor(StandardCursorType.Wait);
        RunningProgram.Current.PointerOf(theme).Should().BeNull();
        RunningProgram.Current.PointerOf(new object()).Should().BeNull();
    }

    [AvaloniaFact]
    public void Font_name_acceptance_matches_the_installed_families()
    {
        var family = FontManager.Current.SystemFonts.First().Name;
        RunningProgram.Current.CanNameFont(family).Should().BeTrue();
        RunningProgram.Current.CanNameFont("").Should().BeFalse();
        RunningProgram.Current.CanNameFont(null).Should().BeFalse();
    }
}
