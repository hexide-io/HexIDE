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
        public Task<MessageBoxResult> MsgBox(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
            => Task.FromResult<MessageBoxResult>(default);
        public Task<string?> InputBox(string prompt, string title, string defaultText)
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
        var window = new Window { Width = 400, Height = 300, Background = Brushes.White };

        // VBFrame and VBPictureBox both have ControlThemes of their own, so without HexIDE's dictionary they
        // get no template, no content presenter and therefore no realised children. The bare headless test
        // app carries neither this nor the SystemColors brushes it resolves, exactly as ClassicRenderTests
        // notes.
        window.Resources.MergedDictionaries.Add(
            new ResourceInclude(new Uri("avares://HexIDE.Integration.Tests/"))
            {
                Source = new Uri("avares://HexIDE.Runtime/BuiltinControls/Resources.axaml"),
            });
        foreach (var (key, brush) in SystemBrushes())
            window.Resources[key] = brush;

        window.Content = root;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(400, 300));
        window.Arrange(new Rect(0, 0, 400, 300));
        Dispatcher.UIThread.RunJobs();
        return (canvas, window, ctx, env);
    }

    private static IEnumerable<(object Key, IBrush Brush)> SystemBrushes()
    {
        yield return (Classic.CommonControls.SystemColors.ControlBrushKey, new SolidColorBrush(Color.Parse("#F0F0F0")));
        yield return (Classic.CommonControls.SystemColors.ControlLightBrushKey, new SolidColorBrush(Color.Parse("#E3E3E3")));
        yield return (Classic.CommonControls.SystemColors.ControlLightLightBrushKey, new SolidColorBrush(Colors.White));
        yield return (Classic.CommonControls.SystemColors.ControlDarkBrushKey, new SolidColorBrush(Color.Parse("#A0A0A0")));
        yield return (Classic.CommonControls.SystemColors.ControlDarkDarkBrushKey, new SolidColorBrush(Color.Parse("#696969")));
        yield return (Classic.CommonControls.SystemColors.ControlTextBrushKey, new SolidColorBrush(Colors.Black));
        yield return (Classic.CommonControls.SystemColors.WindowBrushKey, new SolidColorBrush(Colors.White));
        yield return (Classic.CommonControls.SystemColors.WindowTextBrushKey, new SolidColorBrush(Colors.Black));
        yield return (Classic.CommonControls.SystemColors.GrayTextBrushKey, new SolidColorBrush(Color.Parse("#808080")));
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
