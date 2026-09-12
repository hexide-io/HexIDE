using HexIDE.IDE;
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Conversations;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Tools.LanguageServers;
using NSubstitute;

namespace HexIDE.Integration.Tests.ViewModels;

/// <summary>
/// Everything the connection list reacts to arrives off the user interface thread.
/// </summary>
/// <remarks>
/// <b>Here rather than in HexIDE.Tests because this one genuinely needs a dispatcher.</b> The connection
/// list marshals its arming notification, since the automation surface arms from its own thread — so the
/// assertion depends on a queued job actually running. In the unit project the dispatcher's owning thread
/// is not fixed (hexide-io/HexIDE#286), which made the same test pass on most runs and throw on the rest.
/// A headless application binds one properly and <c>RunJobs</c> drains it.
///
/// <para>
/// Everything about arming that does NOT need a dispatcher — that a row starts unarmed, that ticking one
/// arms only that connection, that the state survives the window's wholesale rebuild — stays a plain unit
/// test next to the rest of the window's behaviour.
/// </para>
/// </remarks>
public class LanguageServersMarshallingTests
{
    private static ILocalizationService Loc()
    {
        var loc = Substitute.For<ILocalizationService>();
        loc.GetString(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        return loc;
    }

    private static LanguageServerConnection Conn(
        string id, string language, LanguageConnectionState state = LanguageConnectionState.Running) =>
        new(id, id, LanguageConnectionKind.LanguageServer, state,
            [".x"], language, null, LanguageConnectionTransport.Stdio, null, 0, DateTimeOffset.UtcNow, null);

    private static (LanguageServersToolViewModel Vm, ConversationLog Capture) Sut(string id)
    {
        var registry = Substitute.For<ILanguageConnectionRegistry>();
        registry.Connections.Returns([Conn(id, "vb6")]);
        registry.ConfigurationProblems.Returns([]);

        var capture = new ConversationLog();
        return (new LanguageServersToolViewModel(registry, Loc(), capture, Substitute.For<IEventBus>()), capture);
    }

    [AvaloniaFact]
    public void ArmingFromElsewhereTellsTheRowToReRead()
    {
        // Three things arm the same connection: this window, the automation surface, and the launch flag.
        // Reading through to the capture is not enough on its own — a checkbox nobody told stays where it
        // was drawn, and a control that lies about its own state is worse than no control.
        var (vm, capture) = Sut("bundled");
        var row = vm.Groups.Single().Rows.Single();

        var raised = new List<string?>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        capture.Arm("bundled", true);
        Dispatcher.UIThread.RunJobs();

        raised.Should().Contain(nameof(LanguageServerRowViewModel.IsArmed));
        row.IsArmed.Should().BeTrue();
    }

    [AvaloniaFact]
    public void ArmingOneConnectionDoesNotDisturbAnother()
    {
        // The notification carries which connection changed, so a window with a dozen servers does not
        // repaint every checkbox because one of them was armed.
        var registry = Substitute.For<ILanguageConnectionRegistry>();
        registry.Connections.Returns([Conn("bundled", "vb6"), Conn("latex", "tex")]);
        registry.ConfigurationProblems.Returns([]);

        var capture = new ConversationLog();
        var vm = new LanguageServersToolViewModel(registry, Loc(), capture, Substitute.For<IEventBus>());

        var rows = vm.Groups.SelectMany(g => g.Rows).ToDictionary(r => r.Id);
        var other = 0;
        rows["latex"].PropertyChanged += (_, _) => other++;

        capture.Arm("bundled", true);
        Dispatcher.UIThread.RunJobs();

        other.Should().Be(0);
        rows["bundled"].IsArmed.Should().BeTrue();
    }

    [AvaloniaFact]
    public void TheFutureServersToggleFollowsTheCapture()
    {
        // The launch flag and the automation surface both set the same default this checkbox does.
        var (vm, capture) = Sut("bundled");

        capture.ArmsEveryConnection = true;
        Dispatcher.UIThread.RunJobs();

        vm.ArmsFutureServers.Should().BeTrue();
    }

    [AvaloniaFact]
    public void TheViewRebuildsWhenTheRegistrySaysSomethingChanged()
    {
        // The reason ILspClient gained StateChanged. A connection's death is otherwise observable only by
        // asking, so a view would report the past until something else happened to refresh it.
        var registry = Substitute.For<ILanguageConnectionRegistry>();
        registry.Connections.Returns([Conn("s", "vb6", LanguageConnectionState.Starting)]);
        registry.ConfigurationProblems.Returns([]);

        var vm = new LanguageServersToolViewModel(
            registry, Loc(), new ConversationLog(), Substitute.For<IEventBus>());
        vm.Groups.Single().Rows.Single().IsRunning.Should().BeFalse();

        registry.Connections.Returns([Conn("s", "vb6")]);
        registry.ConnectionsChanged += Raise.Event<EventHandler>(registry, EventArgs.Empty);
        Dispatcher.UIThread.RunJobs();

        vm.Groups.Single().Rows.Single().IsRunning.Should().BeTrue(
            "the view must refresh when the registry says a connection changed, not when something else "
          + "happens to ask");
    }
}
