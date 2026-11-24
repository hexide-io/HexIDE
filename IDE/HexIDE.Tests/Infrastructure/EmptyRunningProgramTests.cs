using HexIDE.IDE;

namespace HexIDE.Tests.Infrastructure;

public class EmptyRunningProgramTests
{
    [Fact]
    public void An_absent_engine_has_no_forms_activity_or_output()
    {
        IRunningProgram program = new EmptyRunningProgram();
        var output = new List<string>();
        program.Written += output.Add;
        program.ShownWindows(new object()).Should().BeEmpty();
        program.Form(new object()).Should().BeNull();
        program.Activity(new object()).Should().BeNull();
        program.Activity(null).Should().BeNull("no activity means there is nothing to wait for");
        program.Written -= output.Add;
        output.Should().BeEmpty();
    }

    private readonly IRunningProgram program = new EmptyRunningProgram();

    [Fact]
    public void Controls_have_no_program_name() => program.ControlName(new object()).Should().BeNull();

    [Fact]
    public void Objects_are_not_program_menu_items() => program.IsMenuItem(new object()).Should().BeFalse();

    [Fact]
    public void Objects_are_not_controls_that_never_show() => program.NeverShows(new object()).Should().BeFalse();

    [Fact]
    public void Lock_keys_have_no_reading()
    {
        foreach (var key in Enum.GetValues<LockKey>())
            program.ReadLockKey(key).Should().Be(((bool?)null, LockKeySource.Window));
    }

    [Fact]
    public void Setting_a_lock_key_does_nothing()
    {
        foreach (var key in Enum.GetValues<LockKey>())
            foreach (var on in new bool?[] { true, false, null })
            {
                program.SetLockKey(key, on);
                program.ReadLockKey(key).Should().Be(((bool?)null, LockKeySource.Window));
            }
    }

    [Fact]
    public void Cursors_have_no_program_pointer() => program.PointerOf(new object()).Should().BeNull();

    [Fact]
    public void Font_families_cannot_be_named() => program.CanNameFont("Arial").Should().BeFalse();
}
