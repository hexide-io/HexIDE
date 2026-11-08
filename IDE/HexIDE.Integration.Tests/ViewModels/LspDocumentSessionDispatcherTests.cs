using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using HexIDE.Forms.ViewModels;
using HexIDE.Lsp;

namespace HexIDE.Integration.Tests.ViewModels;

// #2530: a session that a test never disposed sent its debounced change after that test had ended. The send
// finishes on a pool thread, and the post that followed asked Dispatcher.UIThread there while Avalonia.Headless
// had it cleared to set up the next test. So the pool thread became the UI thread, the setup threw, and the run
// hung. This reproduces that moment by clearing the UI dispatcher just before the send completes.
public class LspDocumentSessionDispatcherTests
{
    // Avalonia's own field. If it is ever renamed, this test fails here rather than passing without testing anything.
    private static readonly FieldInfo? UiThreadField =
        typeof(Dispatcher).GetField("s_uiThread", BindingFlags.Static | BindingFlags.NonPublic);

    [AvaloniaFact]
    public async Task A_debounced_post_from_a_pool_thread_goes_to_the_dispatcher_the_session_was_built_on()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        UiThreadField.Should().NotBeNull("the test clears Avalonia's UI dispatcher through this field");
        var ui = Dispatcher.UIThread;
        var client = Substitute.For<ILspClient>();
        client.IsRunning.Returns(true);
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ChangeDocumentAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                sending.TrySetResult();
                return release.Task;
            });
        var document = new TextDocument("a");
        using var session = new LspDocumentSession(client, document, "untitled:P/Module1.bas");
        bool? ranOnTheUiThread = null;
        session.ChangeSent += () => ranOnTheUiThread = ui.CheckAccess();
        session.Start();
        document.Insert(0, "'");
        // The debounce has fired, and the send is waiting on a pool thread.
        await sending.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

        UiThreadField!.GetValue(null).Should().BeSameAs(ui);
        object? claimed;
        UiThreadField.SetValue(null, null);
        try
        {
            // The send completes on a pool thread, which then posts ChangeSent, with no UI dispatcher set.
            release.SetResult();
            var elapsed = Stopwatch.StartNew();
            while (ranOnTheUiThread is null && elapsed.ElapsedMilliseconds < 5000)
            {
                await Task.Delay(20, cancellationToken);
                ui.RunJobs();
            }
            claimed = UiThreadField.GetValue(null);
        }
        finally
        {
            UiThreadField.SetValue(null, ui);
        }

        claimed.Should().BeNull("a post from a pool thread must not make that thread the UI thread (#2530)");
        ranOnTheUiThread.Should().BeTrue("the post goes to the dispatcher the session was built on");
    }
}
