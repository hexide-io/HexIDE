using System.Linq;
using HexIDE.Localization;
using HexIDE.Runtime.Debugging;
using HexIDE.Tools;

namespace HexIDE.Tests.ViewModels;

public class LocalsToolViewModelTests
{
    private readonly ILocalizationService _localization = Substitute.For<ILocalizationService>();
    private readonly IDebugController _debugController = Substitute.For<IDebugController>();

    public LocalsToolViewModelTests()
    {
        AvaloniaTestSetup.EnsureInitialized();
        _localization.GetString("Str.Tool.Locals.ContextReady").Returns("<Ready>");
    }

    private LocalsToolViewModel CreateSut() => new(_localization, _debugController);

    [Fact]
    public void Stopped_PopulatesRootsAndContextFromGetLocals()
    {
        _debugController.GetLocals().Returns(new DebugScope("Module1.Go", new[]
        {
            new DebugNode("i", "3", "Integer"),
            new DebugNode("s", "\"Ada\"", "String"),
        }));
        var sut = CreateSut();

        _debugController.Stopped += Raise.Event<Action<StoppedInfo>>(new StoppedInfo(StopReason.Breakpoint, "Module1", 7));

        sut.ContextLabel.Should().Be("Module1.Go");
        sut.Roots.Select(r => r.Expression).Should().Equal("i", "s");
    }

    [Fact]
    public void Stopped_PreservesExpandedNodes_AcrossRebuild()
    {
        // A fresh scope each break (as the interpreter produces), with one expandable node.
        _debugController.GetLocals().Returns(_ => new DebugScope("Module1.Go", new[]
        {
            new DebugNode("obj", string.Empty, "Thing", () => new[] { new DebugNode("field", "1", "Integer") }),
            new DebugNode("i", "3", "Integer"),
        }));
        var sut = CreateSut();

        _debugController.Stopped += Raise.Event<Action<StoppedInfo>>(new StoppedInfo(StopReason.Step, "Module1", 7));
        var objNode = sut.Roots.First(r => r.Expression == "obj");
        objNode.IsExpanded.Should().BeFalse();
        _ = objNode.Children;            // realize children, as the TreeView does on expand
        objNode.IsExpanded = true;       // the user expands it

        // Second break (same shape) → rebuild; the expansion must be preserved (D10).
        _debugController.Stopped += Raise.Event<Action<StoppedInfo>>(new StoppedInfo(StopReason.Step, "Module1", 8));

        sut.Roots.First(r => r.Expression == "obj").IsExpanded.Should().BeTrue();    // still expanded
        sut.Roots.First(r => r.Expression == "i").IsExpanded.Should().BeFalse();     // a collapsed node stays collapsed
    }

    [Fact]
    public void Continued_ClearsRootsAndResetsContext()
    {
        _debugController.GetLocals().Returns(new DebugScope("Module1.Go", new[] { new DebugNode("i", "3", "Integer") }));
        var sut = CreateSut();
        _debugController.Stopped += Raise.Event<Action<StoppedInfo>>(new StoppedInfo(StopReason.Breakpoint, "Module1", 7));
        sut.Roots.Should().NotBeEmpty();

        _debugController.Continued += Raise.Event<Action>();

        sut.ContextLabel.Should().Be("<Ready>");
        sut.Roots.Should().BeEmpty();
    }
}
