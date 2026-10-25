using System;
using System.Linq;
using HexIDE.Localization;
using HexIDE.Runtime.Debugging;
using HexIDE.Tools;

namespace HexIDE.Tests.ViewModels;

public class CallStackToolViewModelTests
{
    private readonly ILocalizationService _localization = Substitute.For<ILocalizationService>();
    private readonly IDebugController _debugController = Substitute.For<IDebugController>();

    public CallStackToolViewModelTests()
    {
    }

    private CallStackToolViewModel CreateSut() => new(_localization, _debugController);

    [AvaloniaFact]
    public void SelectingBuiltInCallerShowsItsModuleAndLineWithoutChangingExecution()
    {
        _debugController.GetCallStack().Returns(new[]
        {
            new CallStackFrame("C", "Worker", 12),
            new CallStackFrame("B", "Module1", 7)
        });
        var sut = CreateSut();
        _debugController.Stopped += Raise.Event<Action<StoppedInfo>>(new StoppedInfo(StopReason.StopStatement, "Worker", 12));
        sut.SelectedFrame = sut.Frames[1];
        sut.ShownFrame.Should().BeSameAs(sut.Frames[1]);
        sut.ShownFrame!.Module.Should().Be("Module1");
        sut.ShownFrame.Line.Should().Be(7);
        sut.ShownFrame.IsCaller.Should().BeTrue();
        _debugController.DidNotReceive().SetNextStatement(Arg.Any<string>(), Arg.Any<int>());
        _debugController.Continued += Raise.Event<Action>();
        sut.ShownFrame.Should().BeNull();
        sut.ShowSelectedFrame();
        sut.ShownFrame.Should().BeNull("a row from the previous break is stale");
    }

    [AvaloniaFact]
    public void Stopped_PopulatesFramesFromGetCallStack_CurrentFirstWithArrow()
    {
        _debugController.GetCallStack().Returns(new[]
        {
            new CallStackFrame("C", "Module1", 12),
            new CallStackFrame("B", "Module1", 7),
            new CallStackFrame("A", "Module1", 3),
        });
        var sut = CreateSut();

        _debugController.Stopped += Raise.Event<Action<StoppedInfo>>(new StoppedInfo(StopReason.Breakpoint, "Module1", 12));

        sut.Frames.Select(f => f.Frame).Should().Equal("→ Module1.C", "   Module1.B", "   Module1.A");
        sut.Frames.Select(f => f.Line).Should().Equal(12, 7, 3);
    }

    [AvaloniaFact]
    public void Continued_ClearsFrames()
    {
        _debugController.GetCallStack().Returns(new[] { new CallStackFrame("A", "Module1", 3) });
        var sut = CreateSut();
        _debugController.Stopped += Raise.Event<Action<StoppedInfo>>(new StoppedInfo(StopReason.Breakpoint, "Module1", 3));
        sut.Frames.Should().NotBeEmpty();

        _debugController.Continued += Raise.Event<Action>();

        sut.Frames.Should().BeEmpty();
    }
}
