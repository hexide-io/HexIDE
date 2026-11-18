using System;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.Debugging;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Debugging;
using HexIDE.Tools;

namespace HexIDE.Tests.ViewModels;

public class WatchesToolViewModelTests
{
    // Measured rows: docs/vb6-fidelity-oracle/a-watch-and-its-context.md (#4543).
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Interpreter_rows_follow_measured_context_then_expression_order(bool allModules)
    {
        var expressions = allModules ? new[] { "p", "Loc", "zq", "g", "1 + 1" } : new[] { "v", "m", "g" };
        var contexts = allModules ? Enumerable.Repeat(WatchExpression.AllModulesContext, 5).ToArray()
            : new[] { "Class1.X(Let)", "Class1", "Module1" };
        var sut = CreateSut();
        for (var i = 0; i < expressions.Length; ++i)
            _watchService.Add(new WatchExpression(expressions[i], WatchType.Expression, contexts[i]));

        sut.Rows.Select(r => r.Expression).Should().Equal(allModules
            ? new[] { "1 + 1", "Loc", "g", "p", "zq" } : new[] { "m", "v", "g" });
        _watchService.Watches.Select(w => w.Expression).Should().Equal(expressions);
    }

    [AvaloniaFact]
    public void Blank_context_sorts_first_and_equal_rows_keep_added_order()
    {
        var named = new WatchExpression("a", WatchType.Expression, "Class1");
        var first = new WatchExpression("z", WatchType.Expression, WatchExpression.AllModulesContext);
        var second = new WatchExpression("z", WatchType.BreakWhenTrue, WatchExpression.AllModulesContext);
        _watchService.Add(named);
        _watchService.Add(first);
        _watchService.Add(second);
        var sut = CreateSut();

        sut.Rows.Select(r => r.Watch).Should().Equal(first, second, named);
        _watchService.Watches.Should().Equal(named, first, second);
    }

    [AvaloniaFact]
    public async Task Editing_and_deleting_a_sorted_row_targets_its_definition()
    {
        var later = new WatchExpression("zq", WatchType.Expression, "Module1");
        var earlier = new WatchExpression("g", WatchType.Expression, "Module1");
        _watchService.Add(later);
        _watchService.Add(earlier);
        var sut = CreateSut();
        sut.SelectedItem = sut.Rows[0];
        await sut.EditSelected();
        var dialog = (HexIDE.Forms.ViewModels.AddWatchDialogViewModel)
            _windowManager.ReceivedCalls().Single(c => c.GetMethodInfo().Name == "ShowDialog").GetArguments()[0]!;
        dialog.Expression = "zz";
        dialog.OkCommand.Execute(null);

        earlier.Expression.Should().Be("zz");
        sut.Rows.Select(r => r.Watch).Should().Equal(later, earlier);
        _watchService.Watches.Should().Equal(later, earlier);
        sut.SelectedRow.Should().BeNull();
        sut.SelectedItem = sut.Rows[0];
        sut.DeleteSelected();
        _watchService.Watches.Should().Equal(earlier);
        sut.Rows.Single().Watch.Should().BeSameAs(earlier);
    }

    [Fact]
    public void External_watch_dialog_disables_and_refuses_break_condition_types()
    {
        var store = new WatchService();
        var dialog = new HexIDE.Forms.ViewModels.AddWatchDialogViewModel(_localization, store, "Caller", expressionOnly: true)
        { Expression = "answer", IsBreakWhenTrue = true };
        dialog.AllowsBreakConditions.Should().BeFalse();
        dialog.OkCommand.Execute(null);
        store.Watches.Single().Type.Should().Be(WatchType.Expression);
        var interpreter = new HexIDE.Forms.ViewModels.AddWatchDialogViewModel(_localization, store, "Caller")
        { Expression = "answer > 0", IsBreakWhenTrue = true };
        interpreter.AllowsBreakConditions.Should().BeTrue();
        interpreter.OkCommand.Execute(null);
        store.Watches.Last().Type.Should().Be(WatchType.BreakWhenTrue);
    }

    [AvaloniaFact]
    public async Task AllProceduresContext_IsStoredIndependentlyOfLanguage()
    {
        _localization.GetString("Str.Dialog.AddWatch.AllContext").Returns("(Alle Prozeduren)");
        var sut = CreateSut();
        await sut.OpenAddWatchDialog("count");
        var dialog = (HexIDE.Forms.ViewModels.AddWatchDialogViewModel)
            _windowManager.ReceivedCalls().Single(c => c.GetMethodInfo().Name == "ShowDialog").GetArguments()[0]!;
        dialog.OkCommand.Execute(null);
        _watchService.Watches.Single().Context.Should().Be("(All Procedures)");
        sut.Rows.Single().Context.Should().Be("(Alle Prozeduren)");
    }

    [AvaloniaFact]
    public void WatchContext_RefreshesOnLanguageSwitch_WithoutChangingNamedContext()
    {
        _localization.GetString("Str.Dialog.AddWatch.AllContext").Returns("(All Procedures)");
        _watchService.Add(new WatchExpression("count", WatchType.Expression, "(All Procedures)"));
        _watchService.Add(new WatchExpression("other", WatchType.Expression, "Module1.Go"));
        var sut = CreateSut();
        var row = sut.Rows[0];
        var notified = false;
        row.PropertyChanged += (_, e) => { if (e.PropertyName == "Context") notified = true; };
        _localization.GetString("Str.Dialog.AddWatch.AllContext").Returns("(Alle Prozeduren)");
        _localization.LanguageChanged += Raise.Event<Action>();
        row.Context.Should().Be("(Alle Prozeduren)");
        notified.Should().BeTrue();
        sut.Rows[1].Context.Should().Be("Module1.Go");
    }

    [Fact]
    public void DialogContext_RefreshesOnLanguageSwitch_AndStoresCanonicalContext()
    {
        _localization.GetString("Str.Dialog.AddWatch.AllContext").Returns("(All Procedures)");
        var dialog = new HexIDE.Forms.ViewModels.AddWatchDialogViewModel(
            _localization, _watchService, "(All Procedures)", "count");
        var notified = false;
        dialog.PropertyChanged += (_, e) => { if (e.PropertyName == "Context") notified = true; };
        _localization.GetString("Str.Dialog.AddWatch.AllContext").Returns("(Alle Prozeduren)");
        _localization.LanguageChanged += Raise.Event<Action>();
        dialog.Context.Should().Be("(Alle Prozeduren)");
        notified.Should().BeTrue();
        dialog.OkCommand.Execute(null);
        _watchService.Watches.Single().Context.Should().Be("(All Procedures)");
    }

    private readonly ILocalizationService _localization = Substitute.For<ILocalizationService>();
    private readonly WatchService _watchService = new();
    private readonly IDebugController _debug = Substitute.For<IDebugController>();
    private readonly IWindowManager _windowManager = Substitute.For<IWindowManager>();

    public WatchesToolViewModelTests()
    {
        _localization.GetString(Arg.Any<string>()).Returns(ci => ci.Arg<string>());   // echo the key
    }

    private WatchesToolViewModel CreateSut() => new(_localization, _watchService, _debug, _windowManager);

    private static DebugEvalResult OkResult(string display, string type)
        => new(true, display, type, false, new DebugNode("x", display, type));

    // Measured design-time/after-End Type column: docs/vb6-fidelity-oracle/a-watch-and-its-context.md.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InactiveWatch_HasEmptyType(bool afterEnd)
    {
        _watchService.Add(new WatchExpression("modvar", WatchType.Expression, "Module1"));
        var sut = CreateSut();
        if (afterEnd)
        {
            _debug.State.Returns(DebugState.Paused);
            _debug.EvaluateScopedWatchAsync("modvar", "Module1")
                .Returns(Task.FromResult<DebugEvalResult?>(OkResult("5", "Integer")));
            _debug.Stopped += Raise.Event<Action<StoppedInfo>>(new StoppedInfo(StopReason.StopStatement, "Module1", 7));
            await Task.Yield();
            sut.Rows[0].TypeName.Should().Be("Integer");
            _debug.State.Returns(DebugState.Stopped);
            _debug.Continued += Raise.Event<Action>();
        }
        sut.Rows[0].TypeName.Should().Be("Empty");
        sut.Rows[0].Value.Should().Be("Str.Tool.Watches.OutOfContext");
    }

    [AvaloniaFact]
    public void AddingWatchToService_CreatesRow()
    {
        var sut = CreateSut();
        _watchService.Add(new WatchExpression("count", WatchType.Expression, "Module1.Go"));
        sut.Rows.Select(r => r.Expression).Should().Equal("count");
    }

    [AvaloniaFact]
    public async Task Stopped_EvaluatesEachWatch()
    {
        _debug.State.Returns(DebugState.Paused);
        _debug.EvaluateScopedWatchAsync("count", "Module1.Go").Returns(Task.FromResult<DebugEvalResult?>(OkResult("42", "Integer")));
        _watchService.Add(new WatchExpression("count", WatchType.Expression, "Module1.Go"));
        var sut = CreateSut();

        _debug.Stopped += Raise.Event<Action<StoppedInfo>>(new StoppedInfo(StopReason.Breakpoint, "Module1", 7));
        await Task.Yield();   // let the async-void evaluation drain

        sut.Rows[0].Value.Should().Be("42");
        sut.Rows[0].TypeName.Should().Be("Integer");
    }

    [AvaloniaFact]
    public async Task Continued_BlanksValue()
    {
        _debug.State.Returns(DebugState.Paused);
        _debug.EvaluateScopedWatchAsync("count", "Module1.Go").Returns(Task.FromResult<DebugEvalResult?>(OkResult("42", "Integer")));
        _watchService.Add(new WatchExpression("count", WatchType.Expression, "Module1.Go"));
        var sut = CreateSut();
        _debug.Stopped += Raise.Event<Action<StoppedInfo>>(new StoppedInfo(StopReason.Breakpoint, "Module1", 7));
        await Task.Yield();
        sut.Rows[0].Value.Should().Be("42");

        _debug.Continued += Raise.Event<Action>();

        sut.Rows[0].Value.Should().Be("Str.Tool.Watches.OutOfContext");   // localization echoes the key
        sut.Rows[0].TypeName.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void DeleteSelected_RemovesWatch()
    {
        _watchService.Add(new WatchExpression("count", WatchType.Expression, "Module1.Go"));
        var sut = CreateSut();
        sut.SelectedItem = sut.Rows[0];

        sut.DeleteSelected();

        _watchService.Watches.Should().BeEmpty();
        sut.Rows.Should().BeEmpty();
    }
}
