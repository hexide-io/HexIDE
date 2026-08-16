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
        AvaloniaTestSetup.EnsureInitialized();
    }

    private CallStackToolViewModel CreateSut() => new(_localization, _debugController);

    [Fact]
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

    [Fact]
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
