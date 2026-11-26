using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using HexIDE.IDE;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Running;

namespace HexIDE.Runtime.Integration.Tests.Automation;

/// <summary>
/// This project composes no running program, as <c>HexIDE.Standalone</c> doesn't, so each test asks one of its own.
/// Its window handlers are static and installed once per process, however many instances the tests make.
/// </summary>
public class InterpreterRunningProgramTests
{
    [AvaloniaFact]
    public void Only_a_program_menu_item_is_recognised()
    {
        IRunningProgram program = new InterpreterRunningProgram();
        program.IsMenuItem(new VBMenu()).Should().BeTrue();
        program.IsMenuItem(new MenuItem()).Should().BeFalse();
    }

    [AvaloniaFact]
    public void Nonvisual_program_controls_never_show()
    {
        IRunningProgram program = new InterpreterRunningProgram();
        program.NeverShows(new VBTimer()).Should().BeTrue();
        program.NeverShows(new VBCommonDialog(null!)).Should().BeTrue();
        program.NeverShows(new VBCommandButton()).Should().BeFalse();
    }

    [AvaloniaFact]
    public void Control_name_is_the_programs_own_name()
    {
        IRunningProgram program = new InterpreterRunningProgram();
        var control = new VBCommandButton();
        VBProps.SetName(control, "Command1");
        program.ControlName(control).Should().Be("Command1");
        program.ControlName(new Button()).Should().BeNull();
        program.ControlName(new object()).Should().BeNull();
    }

    [AvaloniaFact]
    public void Program_pointer_keeps_its_identity_and_standard_cursor()
    {
        IRunningProgram program = new InterpreterRunningProgram();
        using var cursor = VBCursorType.Hourglass.ToCursor();
        program.PointerOf(cursor).Should().Be(new ProgramPointer(VBCursorType.Hourglass,
            VBCursorType.Hourglass.ToStandardCursorType().ToString()));
        using var theme = new Cursor(StandardCursorType.Wait);
        program.PointerOf(theme).Should().BeNull();
        program.PointerOf(new object()).Should().BeNull();
    }

    [AvaloniaFact]
    public void Font_name_acceptance_matches_the_installed_families()
    {
        IRunningProgram program = new InterpreterRunningProgram();
        var family = FontManager.Current.SystemFonts.First().Name;
        program.CanNameFont(family).Should().BeTrue();
        program.CanNameFont("").Should().BeFalse();
        program.CanNameFont(null).Should().BeFalse();
    }
}
