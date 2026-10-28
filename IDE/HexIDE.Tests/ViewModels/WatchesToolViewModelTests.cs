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
