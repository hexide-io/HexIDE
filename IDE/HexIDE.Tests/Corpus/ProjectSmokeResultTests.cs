using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Corpus;

public class ProjectSmokeResultTests
{
    [Theory]
    [InlineData("crashed")]
    [InlineData("refused")]
    [InlineData("ran to completion")]
    public void First_fault_is_recorded_once_despite_its_aftermath(string laterStatus)
    {
        var directory = Directory.CreateTempSubdirectory("hexide-smoke-result-").FullName;
        try
        {
            var output = Path.Join(directory, "result.json");
            var recorder = new SmokeStageRecorder(output);
            var first = new SmokeStage("crashed", "first fault");
            recorder.Record(first);
            JsonSerializer.Deserialize<SmokeStage>(File.ReadAllText(output)).Should().Be(first);
            recorder.Record(new(laterStatus, "aftermath"));
            JsonSerializer.Deserialize<SmokeStage>(File.ReadAllText(output)).Should().Be(first);
            File.Delete(output);
            recorder.Record(new(laterStatus));
            File.Exists(output).Should().BeFalse("the worker must publish only once, even if the parent removed the file");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Parent_read_retries_while_stand_in_replaces_the_result()
    {
        var directory = Directory.CreateTempSubdirectory("hexide-smoke-result-").FullName;
        try
        {
            var output = Path.Join(directory, "result.json");
            var expected = new SmokeStage("crashed", "replacement");
            var attempts = 0;
            string Read(string path)
            {
                if (Interlocked.Increment(ref attempts) != 1) return File.ReadAllText(path);
                // Stand in for a worker holding the destination during replacement. The first read
                // must encounter the lock; replacement finishes before the next attempt can run.
                try
                {
                    using var worker = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                    return File.ReadAllText(path);
                }
                finally { AtomicFileWriter.WriteAllText(path, JsonSerializer.Serialize(expected)); }
            }

            var result = await ProjectSmokeRunner.ReadResult(output, Read);
            result.Should().Be(expected);
            attempts.Should().BeGreaterThan(1);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Persistent_read_failure_is_reported_after_the_retry_budget()
    {
        var attempts = 0;
        var failure = new IOException("still locked");
        Task Read() => ProjectSmokeRunner.ReadResult("unused", _ =>
        {
            Interlocked.Increment(ref attempts);
            throw failure;
        });

        var clock = System.Diagnostics.Stopwatch.StartNew();
        await ((Func<Task>)Read).Should().ThrowAsync<IOException>().WithMessage("still locked");
        clock.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(2));
        attempts.Should().BeGreaterThan(1);
    }
}
