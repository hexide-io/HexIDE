using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HexIDE.Debugging;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.Localization;

namespace HexIDE.Integration.Tests.Views;

public class AddWatchDialogTests
{
    [AvaloniaTheory]
    [InlineData("Form1")]
    [InlineData("Form1.Square")]
    public void EditShowsAndKeepsStoredContext(string context)
    {
        var localization = Substitute.For<ILocalizationService>();
        var store = new WatchService();
        var watch = new WatchExpression("inner", WatchType.Expression, context);
        store.Add(watch);
        localization.GetString("Str.Dialog.AddWatch.AllContext").Returns("(All Procedures)");
        localization.GetString("Str.Dialog.AddWatch.AllModules").Returns("(All Modules)");
        var vm = new AddWatchDialogViewModel(localization, store, context, editing: watch,
            modules: [new WatchContextModule("Form1", ["Square", "Form_Load"]), new WatchContextModule("Module2", ["Other"]) ]);
        var view = new AddWatchDialog { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();
        try
        {
            view.GetVisualDescendants().OfType<ComboBox>().Should().HaveCount(2);
            vm.OkCommand.Execute(null);
            store.Watches.Single().Context.Should().Be(context);
            var module = view.FindControl<ComboBox>("ModulePicker")!;
            var procedure = view.FindControl<ComboBox>("ProcedurePicker")!;
            vm.Modules.Select(m => m.Name).Should().Equal("(All Modules)", "Form1", "Module2");
            vm.Procedures.Should().Equal("(All Procedures)", "Square", "Form_Load");
            module.SelectedIndex = 2;
            vm.Procedures.Should().Equal("(All Procedures)", "Other");
            procedure.SelectedIndex.Should().Be(0);
            module.SelectedIndex = 1;
            procedure.SelectedIndex = 1;
            vm.OkCommand.Execute(null);
            store.Watches.Single().Context.Should().Be("Form1.Square");
        }
        finally { window.Close(); }
    }
    // VB6's Module list starts with (All Modules), and a watch stored on it reopens there (measured, #4452).
    [AvaloniaFact]
    public void AllModulesHeadsTheListAndAnEditReopensOnIt()
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.Dialog.AddWatch.AllContext").Returns("(All Procedures)");
        localization.GetString("Str.Dialog.AddWatch.AllModules").Returns("(All Modules)");
        var store = new WatchService();
        var watch = new WatchExpression("g", WatchType.Expression, WatchExpression.AllModulesContext);
        store.Add(watch);
        var vm = new AddWatchDialogViewModel(localization, store, watch.Context, editing: watch,
            modules: [new WatchContextModule("Module1", ["Helper"])]);
        var view = new AddWatchDialog { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();
        try
        {
            var module = view.FindControl<ComboBox>("ModulePicker")!;
            module.SelectedIndex.Should().Be(0);
            vm.Modules.Select(m => m.Name).Should().Equal("(All Modules)", "Module1");
            vm.Procedures.Should().Equal("(All Procedures)");
            vm.OkCommand.Execute(null);
            store.Watches.Single().Context.Should().Be(WatchExpression.AllModulesContext);
        }
        finally { window.Close(); }
    }

    // A module whose symbols are unavailable lists no procedures; an unchanged Edit must still keep the stored one (#4354).
    [AvaloniaFact]
    public void EditKeepsAStoredProcedureTheListCannotName()
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.Dialog.AddWatch.AllContext").Returns("(All Procedures)");
        var store = new WatchService();
        var watch = new WatchExpression("inner", WatchType.Expression, "Form1.Square");
        store.Add(watch);
        var vm = new AddWatchDialogViewModel(localization, store, "Form1.Square", editing: watch,
            modules: [new WatchContextModule("Form1", []), new WatchContextModule("Module2", ["Other"])]);
        var view = new AddWatchDialog { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();
        try
        {
            vm.Procedures.Should().Equal("(All Procedures)", "Square");
            view.FindControl<ComboBox>("ProcedurePicker")!.SelectedItem.Should().Be("Square");
            vm.OkCommand.Execute(null);
            store.Watches.Single().Context.Should().Be("Form1.Square");
        }
        finally { window.Close(); }
    }

    // An external session's dialog is expression-only: it shows the frame's context as fixed text and offers no pickers.
    [AvaloniaFact]
    public void AnExpressionOnlyDialogShowsItsContextWithoutPickers()
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.Dialog.AddWatch.AllContext").Returns("(All Procedures)");
        var vm = new AddWatchDialogViewModel(localization, new WatchService(), "Helper", "n", expressionOnly: true);
        var view = new AddWatchDialog { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();
        try
        {
            var context = view.FindControl<TextBlock>("FixedContext")!;
            context.IsEffectivelyVisible.Should().BeTrue();
            context.Text.Should().Be("Helper");
            view.GetVisualDescendants().OfType<ComboBox>().Should().OnlyContain(c => !c.IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    // Measured in docs/vb6-fidelity-oracle/a-watch-and-its-context.md.
    [AvaloniaTheory]
    [InlineData("modvar", false, false, "5")]
    [InlineData("inner", false, false, "<Expression not defined in context>")]
    [InlineData("inner", true, false, "49")]
    [InlineData("inner", true, true, "<Out of context>")]
    public async System.Threading.Tasks.Task DialogChoiceFeedsMeasuredEvaluation(string expression, bool procedure, bool stepOut, string expected)
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.Dialog.AddWatch.AllContext").Returns("(All Procedures)");
        var store = new WatchService();
        var vm = new AddWatchDialogViewModel(localization, store, "Form1.Square", expression);
        var view = new AddWatchDialog { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();
        try
        {
            var picker = view.GetVisualDescendants().OfType<ComboBox>().Last();
            picker.SelectedIndex = procedure ? 1 : 0;
            vm.OkCommand.Execute(null);
            var context = procedure ? "Form1.Square" : "Form1";
            store.Watches.Single().Context.Should().Be(context);
            new HexIDE.Tools.WatchRowViewModel(store.Watches.Single(), localization).Context.Should().Be(context);
            var debugger = new HexIDE.Runtime.Debugging.DebugController();
            var interpreter = new HexIDE.Runtime.Interpreter.BasicInterpreter(
                Substitute.For<HexIDE.Runtime.Interpreter.IBasicStandardLibrary>(),
                new HexIDE.Runtime.Interpreter.ModuleExecutionContext(),
                new HexIDE.Runtime.Interpreter.ExecutionEnvironment(), """
                Dim modvar As Integer
                Form_Load
                Sub Form_Load()
                    Dim total As Integer
                    modvar = 5
                    total = Square(7)
                    Stop
                End Sub
                Function Square(ByVal n As Integer) As Integer
                    Dim inner As Integer
                    inner = n * n
                    Stop
                    Square = inner
                End Function
                """, "Form1") { DebugController = debugger };
            System.Threading.Tasks.Task NextStop()
            {
                var completion = new System.Threading.Tasks.TaskCompletionSource();
                void Stopped(HexIDE.Runtime.Debugging.StoppedInfo info)
                { debugger.Stopped -= Stopped; completion.SetResult(); }
                debugger.Stopped += Stopped;
                return completion.Task.WaitAsync(System.TimeSpan.FromSeconds(15));
            }
            var stopped = NextStop();
            var run = interpreter.Execute();
            try
            {
                await stopped;
                if (stepOut) { stopped = NextStop(); debugger.StepOut(); await stopped; }
                var result = await ((HexIDE.Runtime.Debugging.IDebugController)debugger)
                    .EvaluateScopedWatchAsync(expression, store.Watches.Single().Context);
                result!.Display.Should().Be(expected);
            }
            finally { debugger.Stop(); await run; }
        }
        finally { window.Close(); }
    }
}
