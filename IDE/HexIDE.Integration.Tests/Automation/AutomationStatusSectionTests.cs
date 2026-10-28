using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Automation;
using HexIDE.Localization;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// The status bar section that says the automation server is listening, on which port and for which session, and shows
/// when a call runs (#2451). Nothing in a running IDE said so: several sessions' IDEs looked alike, and one with no
/// authentication (#352) gave no sign it could be driven.
/// </summary>
public class AutomationStatusSectionTests : IDisposable
{
    private readonly List<Window> windows = [];
    private readonly TimeSpan atLeast = AutomationServerStatus.BusyAtLeast;
    private readonly TimeSpan tail = AutomationServerStatus.BusyTail;
    // The section reads its strings from the application's resources, where the active pack puts them.
    private readonly LocalizationService localization = new();

    public AutomationStatusSectionTests() => localization.Apply("en");

    public void Dispose()
    {
        foreach (var w in windows) w.Close();
        AutomationServerStatus.BusyAtLeast = atLeast;
        AutomationServerStatus.BusyTail = tail;
        localization.Apply("en");
        GC.SuppressFinalize(this);
    }

    private (AutomationServerStatus Status, AutomationStatusSection Section) Show()
    {
        var status = new AutomationServerStatus();
        var section = new AutomationStatusSection(status);
        var window = new Window { Content = section };
        windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (status, section);
    }

    private const string Build = @"%USERPROFILE%\src\HexIDE\IDE\HexIDE.Desktop\bin\Debug\net10.0";

    private static string Shown(AutomationStatusSection section) =>
        section.GetVisualDescendants().OfType<TextBlock>().Single().Text!;

    [AvaloniaFact]
    public void Nothing_shows_until_the_server_is_listening()
    {
        var (_, section) = Show();

        section.IsVisible.Should().BeFalse("a server that has not bound its port is not listening");
    }

    [AvaloniaFact]
    public void A_listening_server_shows_its_port_and_names_itself_for_automation_and_screen_readers()
    {
        var (status, section) = Show();

        status.Listening(5140, null, Build);
        Dispatcher.UIThread.RunJobs();

        section.IsVisible.Should().BeTrue();
        Shown(section).Should().Be("MCP :5140");
        AutomationProperties.GetName(section).Should().Be("MCP :5140");
        AutomationProperties.GetAutomationId(section).Should().Be("StatusBar.Automation");
    }

    [AvaloniaFact]
    public void An_IDE_started_for_a_session_names_it_after_the_port()
    {
        var (status, section) = Show();

        status.Listening(5140, "docs-pass", Build);
        Dispatcher.UIThread.RunJobs();

        Shown(section).Should().Be("MCP :5140 · docs-pass");
    }

    [AvaloniaFact]
    public void Hovering_says_where_it_listens_whom_it_serves_what_it_is_and_that_anything_here_can_drive_it()
    {
        var (status, section) = Show();

        status.Listening(5140, null, Build);
        Dispatcher.UIThread.RunJobs();

        section.Tip().Split(Environment.NewLine).Should().Equal(
            "The automation server is listening at http://localhost:5140/mcp.",
            "It serves any caller that names no session.",
            $"Process {Environment.ProcessId}, built in {Build}.",
            "It answers this machine only, with no authentication: anything here that can open its port can drive this IDE.",
            "No call yet.");
        ToolTip.GetTip(section).Should().Be(section.Tip());
        AutomationProperties.GetHelpText(section).Should().Be(section.Tip());
    }

    [AvaloniaFact]
    public void A_running_call_is_named_and_highlighted_and_then_becomes_the_last_call()
    {
        AutomationServerStatus.BusyAtLeast = TimeSpan.Zero;
        AutomationServerStatus.BusyTail = TimeSpan.Zero;
        var (status, section) = Show();
        status.Listening(5140, "docs-pass", Build);

        status.CallStarted("open_file");
        Dispatcher.UIThread.RunJobs();

        status.IsBusy.Should().BeTrue();
        section.Tip().Should().Contain("It serves session 'docs-pass', and refuses calls from any other.")
            .And.Contain("A call is running: open_file.").And.Contain("No call yet.");
        section.Dot.Fill.Should().NotBeNull("the theme's highlight fills the dot while a call runs");

        status.CallEnded();
        Dispatcher.UIThread.RunJobs();

        status.IsBusy.Should().BeFalse();
        section.Dot.Fill.Should().BeNull();
        section.Tip().Should().NotContain("A call is running").And.Contain("Last call: open_file, at ");
    }

    [AvaloniaFact]
    public async Task A_burst_of_calls_shows_one_cue_not_a_flash_per_call()
    {
        AutomationServerStatus.BusyAtLeast = TimeSpan.FromMilliseconds(200);
        AutomationServerStatus.BusyTail = TimeSpan.FromMilliseconds(100);
        var (status, _) = Show();
        status.Listening(5140, null, Build);
        Dispatcher.UIThread.RunJobs();

        var seen = new List<bool>();
        status.Changed += () => seen.Add(status.IsBusy);
        for (var i = 0; i < 3; i++)
        {
            status.CallStarted("dump_visual_tree");
            status.CallEnded();
        }
        Dispatcher.UIThread.RunJobs();

        status.IsBusy.Should().BeTrue("a call that ended a moment ago keeps its cue a little longer");
        seen.Should().NotContain(false, "the cue did not go out between the calls of the burst");
        for (var waited = 0; status.IsBusy && waited < 50; waited++)
        {
            // Not TestContext.Current's token: on the headless dispatcher's thread it can be an earlier test's, disposed.
            await Task.Delay(20, CancellationToken.None);
            Dispatcher.UIThread.RunJobs();
        }
        status.IsBusy.Should().BeFalse("it goes out once the burst is over");
    }

    [AvaloniaFact]
    public void A_language_switch_reads_the_section_again_in_the_new_language()
    {
        var (status, section) = Show();
        status.Listening(5140, null, Build);
        Dispatcher.UIThread.RunJobs();

        localization.Apply("fr");
        Dispatcher.UIThread.RunJobs();

        section.Tip().Should().StartWith("Le serveur d'automatisation écoute sur http://localhost:5140/mcp.");
        Shown(section).Should().Be("MCP :5140");
    }

    [AvaloniaFact]
    public void Calls_refused_from_other_sessions_are_counted_and_the_last_named()
    {
        var (status, section) = Show();
        status.Listening(5140, "docs-pass", Build);

        status.Refused(null);
        Dispatcher.UIThread.RunJobs();
        section.Tip().Should().EndWith("Calls refused from other sessions: 1. The last named no session.");

        status.Refused(" stale-build ");
        Dispatcher.UIThread.RunJobs();
        section.Tip().Should().EndWith("Calls refused from other sessions: 2. The last named session 'stale-build'.");
    }
}
