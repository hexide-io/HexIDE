using HexIDE.Forms.ViewModels;
using HexIDE.Tools;

namespace HexIDE.Tests.ViewModels;

/// <summary>
/// The context a new watch starts with (#4354). The declarations row is measured
/// (docs/vb6-fidelity-oracle/a-watch-and-its-context.md); the others keep main's choice, which is unmeasured.
/// </summary>
public class WatchDialogContextTests
{
    private static readonly WatchContextModule[] Modules =
        [new("Module1", ["Helper"]), new("Module2", ["Foo"])];

    [Theory]
    // Caret in the active module's declarations: that module, with (All Procedures). Measured.
    [InlineData("Module1", false, "Module2.Foo", "Module1")]
    [InlineData("Module1", false, "(All Procedures)", "Module1")]
    // Caret inside a procedure while paused: the paused Module.Procedure whole, never Foo under Module1.
    [InlineData("Module1", true, "Module2.Foo", "Module2.Foo")]
    [InlineData("Module1", true, "Module1.Helper", "Module1.Helper")]
    // Caret inside a procedure, nothing paused: the active module, with (All Procedures).
    [InlineData("Module1", true, "(All Procedures)", "Module1")]
    // No code window active: the paused context; with nothing paused, (All Modules), as VB6 starts (measured, #4452).
    [InlineData(null, false, "Module2.Foo", "Module2.Foo")]
    [InlineData(null, false, "(All Procedures)", "(All Modules)")]
    public void A_new_watch_starts_from_the_measured_default_or_mains_choice(
        string? activeModule, bool caretInProcedure, string pausedContext, string expected)
        => WatchDialogContextSource.DefaultContext(activeModule, caretInProcedure, pausedContext, Modules)
            .Should().Be(expected);

    [Fact]
    public void With_no_modules_and_nothing_paused_the_dialog_starts_on_all_modules()
        => WatchDialogContextSource.DefaultContext(null, false, "(All Procedures)", [])
            .Should().Be("(All Modules)");
}
