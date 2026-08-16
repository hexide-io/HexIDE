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
    private readonly ILocalizationService _localization = Substitute.For<ILocalizationService>();
    private readonly WatchService _watchService = new();
    private readonly IDebugController _debug = Substitute.For<IDebugController>();
    private readonly IWindowManager _windowManager = Substitute.For<IWindowManager>();

    public WatchesToolViewModelTests()
    {
        AvaloniaTestSetup.EnsureInitialized();
        _localization.GetString(Arg.Any<string>()).Returns(ci => ci.Arg<string>());   // echo the key
    }

    private WatchesToolViewModel CreateSut() => new(_localization, _watchService, _debug, _windowManager);

    private static DebugEvalResult OkResult(string display, string type)
        => new(true, display, type, false, new DebugNode("x", display, type));

    [Fact]
    public void AddingWatchToService_CreatesRow()
    {
        var sut = CreateSut();
        _watchService.Add(new WatchExpression("count", WatchType.Expression, "Module1.Go"));
        sut.Rows.Select(r => r.Expression).Should().Equal("count");
    }

    [Fact]
    public async Task Stopped_EvaluatesEachWatch()
    {
        _debug.State.Returns(DebugState.Paused);
        _debug.EvaluateWatchAsync("count").Returns(Task.FromResult<DebugEvalResult?>(OkResult("42", "Integer")));
        _watchService.Add(new WatchExpression("count", WatchType.Expression, "Module1.Go"));
        var sut = CreateSut();

        _debug.Stopped += Raise.Event<Action<StoppedInfo>>(new StoppedInfo(StopReason.Breakpoint, "Module1", 7));
        await Task.Yield();   // let the async-void evaluation drain

        sut.Rows[0].Value.Should().Be("42");
        sut.Rows[0].TypeName.Should().Be("Integer");
    }

    [Fact]
    public async Task Continued_BlanksValue()
    {
        _debug.State.Returns(DebugState.Paused);
        _debug.EvaluateWatchAsync("count").Returns(Task.FromResult<DebugEvalResult?>(OkResult("42", "Integer")));
        _watchService.Add(new WatchExpression("count", WatchType.Expression, "Module1.Go"));
        var sut = CreateSut();
        _debug.Stopped += Raise.Event<Action<StoppedInfo>>(new StoppedInfo(StopReason.Breakpoint, "Module1", 7));
        await Task.Yield();
        sut.Rows[0].Value.Should().Be("42");

        _debug.Continued += Raise.Event<Action>();

        sut.Rows[0].Value.Should().Be("Str.Tool.Watches.OutOfContext");   // localization echoes the key
        sut.Rows[0].TypeName.Should().BeEmpty();
    }

    [Fact]
    public void DeleteSelected_RemovesWatch()
    {
        _watchService.Add(new WatchExpression("count", WatchType.Expression, "Module1.Go"));
        var sut = CreateSut();
        sut.SelectedRow = sut.Rows[0];

        sut.DeleteSelected();

        _watchService.Watches.Should().BeEmpty();
        sut.Rows.Should().BeEmpty();
    }
}
