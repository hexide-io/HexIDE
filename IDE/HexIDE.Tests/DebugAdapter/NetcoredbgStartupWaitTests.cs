using System.Text.Json;
using HexIDE.Conversations;
using HexIDE.Dap;

namespace HexIDE.Tests.DebugAdapter;

public class NetcoredbgStartupWaitTests
{
    // Native log captured on Windows CI on 2026-11-19, quoted in #4509.
    private const string StartupLog = """
        0.000 I main.cpp: main(442) > Netcoredbg started
        0.000 I main.cpp: instantiate_protocol(118) > Creating protocol VSCodeProtocol
        0.002 I main.cpp: main(491) > pidDebugee 0
        0.002 I ioredirect.cpp: worker(164) > worker started
        0.004 I vscodeprotocol.cpp: operator(595) > exception '[json.exception.out_of_range.403] key 'env' not found'
        5.008 I vscodeprotocol.cpp: ReadData(907) > EOF
        5.008 W manageddebugger.cpp: Cleanup(868) > ManagedCallback was not properly released by ICorDebug
        5.008 I ioredirect.cpp: operator(154) > IORedirectHelper::worker: terminated
        """;

    [Theory]
    [InlineData("configurationDone", "0x80004005", false, "", true)]
    [InlineData("configurationDone", "0x80004005", true, "", false)]
    [InlineData("configurationDone", "0x80004005", false, "CreateProcessForLaunch : 0x80070002", false)]
    [InlineData("launch", "0x80004005", false, "", false)]
    [InlineData("configurationDone", "0x80070002", false, "", false)]
    [InlineData("configurationDone", "0x80004005", false, "ResumeProcess : 0x8000FFFF", false)]
    public void Only_the_startup_wait_signature_matches(string command, string message, bool processReceived, string callLog, bool expected)
    {
        var error = new DapRequestException(new(1, 1, command, false,
            JsonSerializer.SerializeToElement(new { message = $"Failed command '{command}' : {message}" })));
        ConversationEnvelope[] conversation = processReceived
            ? [new(1, "netcoredbg", DateTimeOffset.UtcNow, ConversationDirection.Received, ConversationEntryKind.Notification, "process", null, 0)]
            : [];

        ForeignAdapterTests.IsNetcoredbgStartupWait(error, conversation, StartupLog + "\n" + callLog).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_log_that_was_not_read_does_not_match(string? nativeLog)
    {
        var error = new DapRequestException(new(1, 1, "configurationDone", false,
            JsonSerializer.SerializeToElement(new { message = "Failed command 'configurationDone' : 0x80004005" })));

        ForeignAdapterTests.IsNetcoredbgStartupWait(error, [], nativeLog).Should().BeFalse();
    }

    [Fact]
    public void Other_exception_types_do_not_match() =>
        ForeignAdapterTests.IsNetcoredbgStartupWait(new IOException("0x80004005"), [], StartupLog).Should().BeFalse();

    [Fact]
    public void A_sent_process_notification_does_not_count_as_a_received_event()
    {
        var error = new DapRequestException(new(1, 1, "configurationDone", false,
            JsonSerializer.SerializeToElement(new { message = "0x80004005" })));
        ConversationEnvelope[] conversation =
            [new(1, "netcoredbg", DateTimeOffset.UtcNow, ConversationDirection.Sent, ConversationEntryKind.Notification, "process", null, 0)];

        ForeignAdapterTests.IsNetcoredbgStartupWait(error, conversation, StartupLog).Should().BeTrue();
    }

    [Fact]
    public async Task Startup_wait_runs_once_more_and_reports_the_mitigation()
    {
        var attempts = 0;
        var output = new List<string>();
        await ForeignAdapterTests.RunWithStartupWaitRetry(() =>
        {
            attempts++;
            if (attempts == 1) throw StartupWait();
            return Task.CompletedTask;
        }, output.Add);

        attempts.Should().Be(2);
        output.Should().Equal("netcoredbg's 5 s startup wait ran out (#4509); the session runs once more");
    }

    [Fact]
    public async Task A_second_failure_keeps_both_attempts_and_never_runs_a_third()
    {
        var first = StartupWait();
        var second = StartupWait();
        var attempts = 0;
        var run = () => ForeignAdapterTests.RunWithStartupWaitRetry(() =>
        {
            attempts++;
            throw attempts == 1 ? first : second;
        }, _ => { });

        var failure = (await run.Should().ThrowAsync<IOException>()).Which;
        attempts.Should().Be(2);
        failure.Message.Should().Contain($"First attempt: {first.Message}").And.Contain($"Second attempt: {second.Message}");
        failure.InnerException.Should().BeOfType<AggregateException>().Which.InnerExceptions.Should().Equal(first, second);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Other_failures_propagate_at_once(bool dapError)
    {
        Exception original = dapError
            ? new DapRequestException(new(1, 1, "launch", false, JsonSerializer.SerializeToElement(new { message = "0x80004005" })))
            : new IOException("CreateProcessForLaunch : 0x80070002");
        var attempts = 0;
        var output = new List<string>();
        var run = () => ForeignAdapterTests.RunWithStartupWaitRetry(() =>
        {
            attempts++;
            throw original;
        }, output.Add);

        (await run.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(original);
        attempts.Should().Be(1);
        output.Should().BeEmpty();
    }

    [Fact]
    public void A_retry_is_reported_as_a_warning_annotation_in_the_foreign_server_report()
    {
        var report = Path.Combine(Path.GetTempPath(), $"foreign-report-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(report, "rumdl: pinned download; version=0.2.64" + Environment.NewLine);
            ForeignAdapterTests.ReportRetry("netcoredbg's 5 s startup wait ran out (#4509); the session runs once more", report);

            File.ReadAllLines(report).Should().Equal(
                "rumdl: pinned download; version=0.2.64",
                "::warning title=netcoredbg startup wait (#4509)::Netcoredbg_runs_a_complete_session: "
                + "netcoredbg's 5 s startup wait ran out (#4509); the session runs once more");
        }
        finally { File.Delete(report); }
    }

    [Fact]
    public void Without_a_report_the_retry_writes_no_file()
    {
        var report = Path.Combine(Path.GetTempPath(), $"foreign-report-{Guid.NewGuid():N}.txt");
        ForeignAdapterTests.ReportRetry("netcoredbg's 5 s startup wait ran out (#4509); the session runs once more", null);
        ForeignAdapterTests.ReportRetry("netcoredbg's 5 s startup wait ran out (#4509); the session runs once more", "");
        File.Exists(report).Should().BeFalse();
    }

    [Fact]
    public async Task A_successful_session_runs_only_once()
    {
        var attempts = 0;
        var output = new List<string>();
        await ForeignAdapterTests.RunWithStartupWaitRetry(() =>
        {
            attempts++;
            return Task.CompletedTask;
        }, output.Add);

        attempts.Should().Be(1);
        output.Should().BeEmpty();
    }

    private static IOException StartupWait()
    {
        var failure = new IOException("configurationDone : 0x80004005");
        failure.Data["NetcoredbgStartupWait"] = true;
        return failure;
    }
}
