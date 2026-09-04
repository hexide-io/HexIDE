using HexIDE.Runtime;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A form's run must see the rest of its project's code (hexide-io/HexIDE#220).
///
/// <para>
/// The interpreter's cross-module resolution was already thorough and well tested — module scope,
/// <c>Public</c>/<c>Private</c> visibility, ambiguity detection — but every one of those tests constructs a
/// <see cref="BasicInterpreter"/> directly and hands it the modules. The IDE's own run path never did, so a
/// form could not call <c>Module1.DoThing</c>, read a <c>Public Const</c> from a <c>.bas</c>, or reach a
/// <c>Public</c> variable there, and nothing noticed.
/// </para>
///
/// <para>
/// So these tests deliberately do NOT test resolution. They test the <b>supply</b> — that the modules are
/// gathered and reach the interpreter — because that is the half that had no cover.
/// </para>
/// </summary>
public class FormRunModuleVisibilityTests
{
    private sealed class NullLib : IBasicStandardLibrary
    {
        public Task<HexIDE.IDE.MessageBoxResult> MsgBox(
            string text, string? caption, HexIDE.IDE.MessageBoxButtons buttons, HexIDE.IDE.MessageBoxIcon icon)
            => Task.FromResult<HexIDE.IDE.MessageBoxResult>(default);
        public Task<string?> InputBox(string prompt, string? title, string defaultText)
            => Task.FromResult<string?>(null);
        public void DebugPrint(Vb6Value value) { }
    }

    private static ProjectDefinition ProjectWith(params (string Name, ModuleKind Kind)[] modules)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        foreach (var (name, kind) in modules)
        {
            var module = new ModuleDefinition(project, name, kind);
            module.UpdateCode($"Public Sub {name}Thing()\r\nEnd Sub\r\n");
            project.AddModule(module);
        }
        return project;
    }

    // ── Gathering ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void StandardModulesAreGatheredForTheInterpreter()
    {
        var project = ProjectWith(("Helpers", ModuleKind.StandardModule), ("Utils", ModuleKind.StandardModule));

        var (standard, _) = VBLoader.InterpreterModules(project);

        standard.Select(m => m.Name).Should().BeEquivalentTo(["Helpers", "Utils"]);
    }

    [Fact]
    public void ClassModulesAreGatheredSeparately()
    {
        // Separate because the interpreter treats them differently: a class module is a TEMPLATE registered
        // so New/Dim As/TypeOf resolve the name, never run at program start. Folding them in with the
        // standard modules would execute every class's top level on startup.
        var project = ProjectWith(("Helpers", ModuleKind.StandardModule), ("Widget", ModuleKind.ClassModule));

        var (standard, classes) = VBLoader.InterpreterModules(project);

        standard.Select(m => m.Name).Should().BeEquivalentTo(["Helpers"]);
        classes.Select(m => m.Name).Should().BeEquivalentTo(["Widget"]);
    }

    [Theory]
    [InlineData(ModuleKind.UserControl)]
    [InlineData(ModuleKind.PropertyPage)]
    public void AComponentBackedModuleIsNotLoadedAsAFreeStandingModule(ModuleKind kind)
    {
        // The control. These have code, so the tempting rule is "every module with code". They are
        // instantiated through the component model with their own execution context, and their code runs
        // there — loading one here would run its declarations a second time, in the wrong scope.
        var project = ProjectWith(("Gauge", kind));

        var (standard, classes) = VBLoader.InterpreterModules(project);

        standard.Should().BeEmpty();
        classes.Should().BeEmpty();
    }

    [Fact]
    public void AProjectlessFormGathersNothingRatherThanThrowing()
    {
        // A bare form with no project behind it is a real case — the designer and several tests build one.
        var (standard, classes) = VBLoader.InterpreterModules(null);

        standard.Should().BeEmpty();
        classes.Should().BeEmpty();
    }

    // ── Supply: the modules actually reach the interpreter ────────────────────────────────────────────

    [Fact]
    public void AFormsInterpreterIsGivenTheProjectsStandardModules()
    {
        // THE regression guard. Before #220 this list held the form alone, so every call into a .bas
        // failed at run time on a project that VB6 runs without complaint.
        var context = new VBWindowContext(new NullLib());
        var (standard, classes) = VBLoader.InterpreterModules(
            ProjectWith(("Helpers", ModuleKind.StandardModule), ("Utils", ModuleKind.StandardModule)));

        context.SetCode("Private Sub Form_Load()\r\nEnd Sub\r\n", "Form1",
            additionalModules: standard, classModules: classes);

        var loaded = context.Interpreter!.Modules.All.Select(m => m.Name);
        loaded.Should().BeEquivalentTo(["Form1", "Helpers", "Utils"],
            "the form is the primary module and the project's .bas files are loaded alongside it");
    }

    [Fact]
    public void AFormsInterpreterIsGivenTheProjectsClassModules()
    {
        var context = new VBWindowContext(new NullLib());
        var (standard, classes) = VBLoader.InterpreterModules(
            ProjectWith(("Widget", ModuleKind.ClassModule)));

        context.SetCode("Private Sub Form_Load()\r\nEnd Sub\r\n", "Form1",
            additionalModules: standard, classModules: classes);

        context.Interpreter!.Modules.Contains("Widget").Should().BeTrue(
            "New Widget from a form has to resolve the name");
    }

    [Fact]
    public void AClassModuleIsRegisteredAsAClassNotAsAStandardModule()
    {
        // The kind matters beyond the name resolving: a standard module runs its top level at startup and a
        // class module must not.
        var context = new VBWindowContext(new NullLib());
        var (standard, classes) = VBLoader.InterpreterModules(ProjectWith(("Widget", ModuleKind.ClassModule)));

        context.SetCode("Private Sub Form_Load()\r\nEnd Sub\r\n", "Form1",
            additionalModules: standard, classModules: classes);

        context.Interpreter!.Modules.TryGet("Widget", out var widget).Should().BeTrue();
        widget.Kind.Should().Be(InterpreterModuleKind.Class);
    }

    [Fact]
    public void AFormWithNoOtherModulesStillRunsAsItsOwnPrimaryModule()
    {
        var context = new VBWindowContext(new NullLib());
        var (standard, classes) = VBLoader.InterpreterModules(ProjectWith());

        context.SetCode("Private Sub Form_Load()\r\nEnd Sub\r\n", "Form1",
            additionalModules: standard, classModules: classes);

        context.Interpreter!.Modules.All.Select(m => m.Name).Should().BeEquivalentTo(["Form1"]);
    }
}
