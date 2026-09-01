using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using HexIDE.IDE;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// Spawns a .frm through the real runtime loader and, where a test needs positions or effective
/// enabled-ness, hosts it in a laid-out headless window. Shared by the Frame and PictureBox container
/// suites, which assert the same properties of two different container classes.
/// </summary>
internal static class ContainerRuntimeHarness
{
    private static readonly ProjectDefinition Project = new(VBProjectType.EXE, "MyProject");

    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    private sealed class CaptureLib(List<Vb6Value> debug) : IBasicStandardLibrary
    {
        public Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon)
            => Task.FromResult<MessageBoxResult>(default);
        public Task<string?> InputBox(string prompt, string? title, string defaultText)
            => Task.FromResult<string?>(null);
        public void DebugPrint(Vb6Value value) => debug.Add(value);
    }

    internal static (Control root, Canvas canvas, ModuleExecutionContext ctx, ExecutionEnvironment env) Spawn(string frm)
    {
        var form = new FormDeserializer().Deserialize(Project, frm, NullSink.Instance)!;
        var ctx = new ModuleExecutionContext();
        var env = new ExecutionEnvironment();
        var root = VBLoader.SpawnComponents(form, ctx, env);
        // SpawnComponents hands back a DockPanel of [menu, canvas]; the canvas is where controls live and
        // must stay the DockPanel's child, so anything hosting this hosts the whole root.
        var canvas = ((DockPanel)root).Children.OfType<Canvas>().Single();
        return (root, canvas, ctx, env);
    }

    /// <summary>Spawns, hosts in a real window and runs a layout pass, so positions and enabled-ness resolve.</summary>
    internal static (Canvas canvas, Window window, ModuleExecutionContext ctx, ExecutionEnvironment env) Laid(string frm)
    {
        var (root, canvas, ctx, env) = Spawn(frm);
        return (canvas, Host(root), ctx, env);
    }

    /// <summary>
    /// A <see cref="Control"/> that is also an <see cref="IModuleExecutionRoot"/>, recording the event
    /// dispatches that reach it instead of running VB6 code. <c>RuntimeExtensions.ExecuteSub</c> walks up for
    /// one of these, so putting it above a spawned form is how a test observes which handlers a control fired.
    /// </summary>
    internal sealed class RecordingRoot : Decorator, IModuleExecutionRoot
    {
        public readonly List<(string Name, IReadOnlyList<Vb6Value>? Args)> Calls = new();
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null) => Calls.Add((name, args));
    }

    /// <summary>
    /// As <see cref="Laid"/>, but with a <see cref="RecordingRoot"/> above the form so event dispatch is
    /// observable.
    ///
    /// The execution root goes on AFTER <see cref="Spawn"/> has built the whole tree, because that is the
    /// order the product uses — <c>VBLoader</c> instantiates every control, then hands the finished tree to a
    /// window. Anything a control does to itself while being constructed therefore reaches no root and
    /// dispatches nothing, which is why a designer-set <c>Value = -1 'True</c> raises no Click at load.
    /// Attaching the recorder first would test a tree the product never builds.
    /// </summary>
    internal static (Canvas canvas, RecordingRoot recorder, ModuleExecutionContext ctx, ExecutionEnvironment env) Recorded(string frm)
    {
        var (root, canvas, ctx, env) = Spawn(frm);
        var recorder = new RecordingRoot { Child = root };
        Host(recorder);
        return (canvas, recorder, ctx, env);
    }

    private static Window Host(Control content)
    {
        var window = new Window { Width = 400, Height = 300, Background = Brushes.White };
        MergeAppResources(window);
        window.Content = content;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(400, 300));
        window.Arrange(new Rect(0, 0, 400, 300));
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>
    /// Gives a test window the same resource dictionaries the application gives itself.
    ///
    /// Both halves matter. VBFrame and VBPictureBox have ControlThemes of their own, so without
    /// HexIDE.Runtime's dictionary they get no template, no presenter and no realised children. And without
    /// the IDE's own Classic theme these tests render against a set of dictionaries NO USER EVER SEES, so a
    /// defect that appears only under the real set passes here — which is not hypothetical: Classic.axaml
    /// carries an implicit ControlTheme for Separator that turns a menu separator into a two-pixel dot, and
    /// the menu tests were green through two rounds of fixing it.
    /// </summary>
    internal static void MergeAppResources(Window window)
    {
        foreach (var source in new[]
                 {
                     "avares://HexIDE.Runtime/BuiltinControls/Resources.axaml",
                     "avares://HexIDE/Themes/Classic.axaml",
                 })
        {
            window.Resources.MergedDictionaries.Add(
                new ResourceInclude(new Uri("avares://HexIDE.Integration.Tests/")) { Source = new Uri(source) });
        }

        // No stubbed SystemColors brushes. They used to be invented here, and inventing them is how a rule
        // drawn in the theme's shadow colour measured #A0A0A0 in a test and rendered #808080 in the product.
        // The dictionaries above supply the real ones; if a key ever stops resolving, a test failing is the
        // outcome worth having.
    }

    internal static async Task<List<Vb6Value>> Run(ModuleExecutionContext ctx, ExecutionEnvironment env, string code)
    {
        var debug = new List<Vb6Value>();
        await new BasicInterpreter(new CaptureLib(debug), ctx, env, code).Execute();
        return debug;
    }

    internal static string? NameOf(Control c) => VBProps.GetName(c);

    internal static Control Child(Canvas canvas, string name) =>
        canvas.Children.OfType<Control>().First(c => NameOf(c) == name);

    internal static Canvas HostOf(Canvas canvas, string containerName) =>
        Child(canvas, containerName) switch
        {
            VBFrame frame => frame.ChildHost!,
            VBPictureBox box => box.ChildHost!,
            var other => throw new InvalidOperationException($"'{containerName}' is a {other.GetType().Name}, not a container"),
        };
}
