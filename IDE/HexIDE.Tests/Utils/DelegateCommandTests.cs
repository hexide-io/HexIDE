using HexIDE.Utils;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace HexIDE.Tests.Utils;

public class DelegateCommandTests
{
    [Fact]
    public void ExecutionTask_is_the_task_the_command_started()
    {
        var pending = new TaskCompletionSource();
        var command = new DelegateCommand(() => pending.Task, () => true);
        command.ExecutionTask.Should().BeNull();
        command.CanExecute(null).Should().BeTrue();
        command.Execute(null);
        command.ExecutionTask.Should().BeSameAs(pending.Task);
        ((IRunningCommand)command).ExecutionTask.Should().BeSameAs(pending.Task);
        pending.SetResult();
        command.ExecutionTask.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task An_async_exception_still_reaches_ListenErrors()
    {
        var sink = new FailureSink();
        var previous = Log.Logger;
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        Log.Logger = logger;
        var pending = new TaskCompletionSource();
        var error = new InvalidOperationException("command failed");
        try
        {
            var command = new DelegateCommand(() => pending.Task);
            command.Execute(null);
            pending.SetException(error);
            var logged = await sink.Event.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            logged.Exception.Should().BeOfType<AggregateException>().Which.InnerExceptions.Should().Contain(error);
        }
        finally { Log.Logger = previous; }
    }

    private sealed class FailureSink : ILogEventSink
    {
        public TaskCompletionSource<LogEvent> Event { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Emit(LogEvent logEvent) => Event.TrySetResult(logEvent);
    }
}
