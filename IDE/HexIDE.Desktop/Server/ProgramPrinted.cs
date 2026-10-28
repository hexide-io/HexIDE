using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace HexIDE.Desktop.Server;

/// <summary>
/// What the running program has written with <c>Debug.Print</c> since its run began or a reply last reported it, for the
/// replies of the tools that run, step, break and stop it (#2531).
/// </summary>
/// <remarks>
/// <para>
/// Reading a program's output was the check a caller made most often after a run, and it took a dump to find the
/// Immediate window's editor and an inspection to read it, while <c>run_project</c> replied only "Running". So each of
/// those replies carries what was printed in <c>printed</c>, and its note says how much, or that there was nothing, so a
/// run that printed nothing reads as empty rather than unread.
/// </para>
/// <para>
/// Fed from the program's own output, not from the Immediate window, which also holds lines a person typed there and
/// what they evaluated to. A run that begins, whoever starts it, starts afresh: what an earlier run printed is not this
/// one's. The Immediate window keeps everything, so a caller that wants an earlier run's output reads it there.
/// </para>
/// </remarks>
internal sealed class ProgramPrinted
{
    /// <summary>The longest <c>printed</c> a reply carries; longer output keeps its end, the part just written.</summary>
    internal const int Limit = 4000;

    public static ProgramPrinted Shared { get; } = new();

    private readonly Lock gate = new();
    private readonly StringBuilder text = new();
    private bool attached;

    /// <summary>Follows the program's output and its runs. Once; later calls do nothing, so a restarted server does not
    /// count every line twice.</summary>
    public void Attach(INotifyPropertyChanged runner, Func<bool> isRunning)
    {
        lock (gate)
        {
            if (attached) return;
            attached = true;
        }
        HexIDE.Runtime.VBDebugConsole.Written += Append;
        FollowRuns(runner, isRunning);
    }

    /// <summary>Starts afresh each time a run begins, whoever starts it.</summary>
    internal void FollowRuns(INotifyPropertyChanged runner, Func<bool> isRunning)
    {
        var wasRunning = isRunning();
        runner.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != "IsRunning") return;
            var running = isRunning();
            if (running && !wasRunning) Reset();
            wasRunning = running;
        };
    }

    public void Append(string written)
    {
        lock (gate) text.Append(written);
    }

    public void Reset()
    {
        lock (gate) text.Clear();
    }

    /// <summary>What was printed since the last call, line breaks as <c>\n</c>, and forgets it.</summary>
    public string Take()
    {
        lock (gate)
        {
            var taken = text.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
            text.Clear();
            return taken;
        }
    }

    /// <summary>
    /// The reply's <c>printed</c>, and the sentence its note gains: null and "nothing" when nothing was printed, and the
    /// output's end when it is over <see cref="Limit"/>, with the note saying how much was left out.
    /// </summary>
    public static (string? Printed, string Note) Report(string taken)
    {
        if (taken.Length == 0)
            return (null, " Debug.Print wrote nothing since the run began, or since the last reply that reported its output.");

        var lines = taken.TrimEnd('\n').Split('\n').Length;
        var count = lines == 1 ? "1 line" : $"{lines.ToString(CultureInfo.InvariantCulture)} lines";
        if (taken.Length <= Limit)
            return (taken, $" Debug.Print wrote {count} since the run began, or since the last reply that reported its output; they are in 'printed'.");

        var left = taken.Length - Limit;
        return ("…" + taken[left..],
            $" Debug.Print wrote {count} since the run began, or since the last reply that reported its output; 'printed' holds the last {Limit.ToString(CultureInfo.InvariantCulture)} "
            + $"of their {taken.Length.ToString(CultureInfo.InvariantCulture)} characters, and the Immediate window holds them all.");
    }
}
