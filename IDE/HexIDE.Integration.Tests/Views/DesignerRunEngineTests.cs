using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;
using Serilog.Events;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The form designer asks the registered run engine to preview a form and which carried controls it runs (#5633). The
/// engine's code is the provider's, so each test reads what the designer then shows: a window, a box, a label, a log line.
/// </summary>
public class DesignerRunEngineTests
{
    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private sealed class Captured : Serilog.Core.ILogEventSink, System.IDisposable
    {
        private readonly Serilog.ILogger previous = Serilog.Log.Logger;
        public List<LogEvent> Events { get; } = [];
        public Captured() => Serilog.Log.Logger = new Serilog.LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(this).CreateLogger();
        public void Emit(LogEvent logEvent) { lock (Events) Events.Add(logEvent); }
        public void Dispose() => Serilog.Log.Logger = previous;
    }

    // A form the provider's interpreter previews, carrying one control it runs: a top-level Toolbar (#2327).
    private const string ToolbarForm =
        "VERSION 5.00\r\n" +
        "Object = \"{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0\"; \"MSCOMCTL.OCX\"\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   ClientHeight    =   4500\r\n" +
        "   ClientLeft      =   60\r\n" +
        "   ClientTop       =   345\r\n" +
        "   ClientWidth     =   6000\r\n" +
        "   Begin MSComctlLib.Toolbar Toolbar1 \r\n" +
        "      Align           =   1  'Align Top\r\n" +
        "      Height          =   660\r\n" +
        "      Left            =   0\r\n" +
        "      TabIndex        =   0\r\n" +
        "      Top             =   0\r\n" +
        "      Width           =   6000\r\n" +
        "      _Version        =   393216\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n";

    private static FormEditViewModel Designer(RunEngineForwarder engine, IWindowManager? windows = null)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, ToolbarForm, new NullSink())!;
        project.AddForm(form);
        return new FormEditViewModel(null!, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(), null!,
            windows ?? Substitute.For<IWindowManager>(), Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>())
        {
            RunEngine = engine,
        }.Initialize(form);
    }

    private static Dictionary<string, bool> ViewOnly(FormEditViewModel designer) =>
        designer.UnmodelledControls.ToDictionary(c => c.Name, c => c.ViewOnly);

    // What the designer says of the carried Toolbar: view-only where the provider's interpreter runs it, and unsupported
    // otherwise. UnmodelledControlPlaceholderTests has the controls it doesn't run.
    private static readonly Dictionary<string, bool> Interpreter = new() { ["Toolbar1"] = true };
    private static readonly Dictionary<string, bool> None = new() { ["Toolbar1"] = false };

    // An engine that runs projects and doesn't implement the designer's contract.
    private static IRunEngine PlainEngine()
    {
        var engine = Substitute.For<IRunEngine>();
        engine.Debugger.Returns(Substitute.For<HexIDE.Runtime.Debugging.IDebugController>());
        return engine;
    }

    private static async Task<VBFormRuntime?> Previewed(FormEditViewModel designer)
    {
        VBFormRuntime? window = null;
        using var opened = Window.WindowOpenedEvent.AddClassHandler<VBFormRuntime>((w, _) => window = w);
        await designer.PreviewForm();
        return window;
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task With_no_engine_or_one_that_doesnt_answer_nothing_is_previewed_and_no_carried_control_runs(bool plainEngine)
    {
        using var log = new Captured();
        using var engine = new DesignerRunEngine(register: false);
        if (plainEngine)
            engine.Register(_ => PlainEngine());
        var windows = Substitute.For<IWindowManager>();
        using var designer = Designer(engine.Forwarder, windows);

        (await Previewed(designer)).Should().BeNull();

        await windows.DidNotReceiveWithAnyArgs().MessageBox(default!, default!, default, default);
        ViewOnly(designer).Should().BeEquivalentTo(None);
        log.Events.Should().Contain(e => e.Level == LogEventLevel.Information
            && e.RenderMessage().Contains("no engine previews forms", System.StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task What_the_engine_throws_is_logged_the_preview_says_so_and_the_engine_stays()
    {
        using var log = new Captured();
        var thrower = Substitute.For<IRunEngine, IDesignerRunEngine>();
        thrower.Debugger.Returns(Substitute.For<HexIDE.Runtime.Debugging.IDebugController>());
        ((IDesignerRunEngine)thrower).Preview(default!).ReturnsForAnyArgs(_ => throw new System.InvalidOperationException("preview broke"));
        ((IDesignerRunEngine)thrower).RunsWhenCarried(default!, default, default)
            .ReturnsForAnyArgs(_ => throw new System.InvalidOperationException("carried broke"));
        using var engine = new DesignerRunEngine(engine: _ => thrower);
        var windows = Substitute.For<IWindowManager>();
        using var designer = Designer(engine.Forwarder, windows);

        (await Previewed(designer)).Should().BeNull();

        await windows.Received(1).MessageBox("preview broke", Arg.Any<string>(), MessageBoxButtons.Ok, MessageBoxIcon.Error);
        ViewOnly(designer).Should().BeEquivalentTo(None, "a throw answers as no engine does");
        log.Events.Where(e => e.Level == LogEventLevel.Error).Select(e => e.Exception!.Message)
            .Should().Contain("preview broke").And.Contain("carried broke");
        log.Events.Where(e => e.Level == LogEventLevel.Error)
            .Should().OnlyContain(e => e.RenderMessage().Contains("HexIDE.Basic", System.StringComparison.Ordinal),
                "each names the engine's owner");
        engine.Engines.Current!.Engine.Should().BeSameAs(thrower);
    }

    [AvaloniaFact]
    public async Task Withdrawing_the_engine_closes_its_previews_and_the_carried_boxes_ask_again()
    {
        using var engine = new DesignerRunEngine();
        using var designer = Designer(engine.Forwarder);
        ViewOnly(designer).Should().BeEquivalentTo(Interpreter);
        var preview = await Previewed(designer);
        preview.Should().NotBeNull();
        var closed = false;
        preview!.Closed += (_, _) => closed = true;

        engine.Withdraw();

        closed.Should().BeTrue("the engine's previews are its windows, and none outlives it");
        ViewOnly(designer).Should().BeEquivalentTo(None, "the boxes were built again once the engine had gone");

        engine.Register();
        Dispatcher.UIThread.RunJobs();

        ViewOnly(designer).Should().BeEquivalentTo(Interpreter, "and again once an engine came back");
    }

    [AvaloniaFact]
    public void A_disposed_designer_no_longer_hears_the_engine()
    {
        using var engine = new DesignerRunEngine();
        var designer = Designer(engine.Forwarder);
        designer.Dispose();
        var boxes = designer.UnmodelledControls.ToList();

        engine.Withdraw();

        designer.UnmodelledControls.Should().Equal(boxes, "a closed designer builds no boxes");
        // And the forwarder, which lives as long as the IDE, holds nothing of it.
        var handlers = (System.Delegate?)typeof(RunEngineForwarder)
            .GetField(nameof(RunEngineForwarder.DesignerChanged), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(engine.Forwarder);
        handlers.Should().BeNull();
    }

    [AvaloniaFact]
    public void A_rebuild_posted_before_the_designer_closed_builds_nothing()
    {
        using var engine = new DesignerRunEngine(register: false);
        var designer = Designer(engine.Forwarder);
        // Registering posts the rebuild; the designer closes before it runs.
        engine.Register();
        designer.Dispose();
        var boxes = designer.UnmodelledControls.ToList();

        Dispatcher.UIThread.RunJobs();

        designer.UnmodelledControls.Should().Equal(boxes);
    }

    [AvaloniaFact]
    public async Task An_engine_being_withdrawn_previews_nothing_and_runs_no_carried_control()
    {
        using var engine = new DesignerRunEngine();
        using var designer = Designer(engine.Forwarder);
        // Its handle is disposed, and the withdrawal it posts hasn't run yet.
        ((System.IDisposable)engine.Engines.Current!).Dispose();

        (await Previewed(designer)).Should().BeNull();
        engine.Forwarder.RunsWhenCarried("MSComctlLib.Toolbar", false, false).Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task The_preview_is_shown_over_the_window_the_IDEs_window_maker_names()
    {
        var owner = new Window();
        owner.Show();
        try
        {
            var dialogs = Substitute.For<IProgramDialogWindows>();
            dialogs.Owner.Returns(owner);
            using var engine = new DesignerRunEngine(dialogs);
            using var designer = Designer(engine.Forwarder);

            var preview = await Previewed(designer);

            preview.Should().NotBeNull();
            preview!.Owner.Should().BeSameAs(owner);
        }
        finally { owner.Close(); }
    }

    [AvaloniaFact]
    public async Task The_composed_designer_previews_and_reads_carried_controls_through_the_providers_engine()
    {
        await using var real = new RealComposition();
        var composition = real.Composition;
        // The main view lays out the dock a designer opens in.
        var root = composition.Root;
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, ToolbarForm, new NullSink())!;
        project.AddForm(form);
        composition.ProjectManager.AddProject(project);
        composition.EditorService.EditForm(form);
        var factory = (MainViewViewModel.DockFactory)root.Layout.Factory!;
        var designer = factory.DocumentDock!.VisibleDockables!.OfType<FormEditViewModel>().Single();
        VBFormRuntime? preview = null;
        try
        {
            designer.RunEngine.Should().NotBeNull("DISetup's factory hands the designer the forwarder");
            ViewOnly(designer).Should().BeEquivalentTo(Interpreter);
            preview = await Previewed(designer);
            preview.Should().NotBeNull("the provider's interpreter previews the form");
        }
        finally
        {
            preview?.Close();
            composition.EventBus.Publish(new ProjectUnloadedEvent(project));
            designer.Dispose();
        }
    }
}
