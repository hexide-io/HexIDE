namespace HexIDE.Projects;

/// <summary>
/// What each Make did, for automation that starts one from the menu (#2671). The menu's command discards Make's task,
/// and a Make that fails ends at a message box it waits on, so a caller can neither await it nor see its result once
/// it is over. Make reports here instead: where it has got to, and then the program it wrote or why it wrote none,
/// before any box that says so opens.
/// </summary>
public static class MakeOutcomes
{
    /// <summary>Where a Make has got to. <see cref="Asking"/> is the Yes/No box that asks before replacing an archive
    /// already beside the program (#2718); a Yes goes back to <see cref="Writing"/>.</summary>
    public enum Stage { Started, AtPicker, Writing, Asking, Finished }

    /// <summary>What a Make ended with: the program and project it wrote, or <see cref="WhyNot"/>.</summary>
    /// <param name="Replaced">A file was already at <see cref="ProgramPath"/> and Make wrote over it.</param>
    /// <param name="BoxOpen">A message box saying why is open, and Make waits on it.</param>
    public sealed record Outcome(string? ProgramPath, long ProgramSize, string? ProjectPath, long ProjectSize,
        bool Replaced, string? WhyNot, bool BoxOpen);

    /// <summary>One Make, from its start to its outcome. The first outcome reported is the one that stands.</summary>
    public sealed class Make
    {
        private readonly TaskCompletionSource<Outcome> ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int stage;
        private long stageSince = Environment.TickCount64;
        private string? replacing;

        internal Make(int number) => Number = number;

        /// <summary>Counts every Make this process has started, from 1.</summary>
        public int Number { get; }

        public Stage Current => (Stage)Volatile.Read(ref stage);

        /// <summary>How long Make has been at <see cref="Current"/>.</summary>
        public TimeSpan InStage => TimeSpan.FromMilliseconds(Environment.TickCount64 - Interlocked.Read(ref stageSince));

        public Task<Outcome> Ended => ended.Task;

        /// <summary>The archive Make asks whether to replace, once it has asked.</summary>
        public string? Replacing => Volatile.Read(ref replacing);

        internal void Reached(Stage next)
        {
            Interlocked.Exchange(ref stageSince, Environment.TickCount64);
            Volatile.Write(ref stage, (int)next);
        }

        internal void Asking(string archivePath)
        {
            Volatile.Write(ref replacing, archivePath);
            Reached(Stage.Asking);
        }

        internal void Made(string programPath, string projectPath, bool replaced)
        {
            Reached(Stage.Finished);
            ended.TrySetResult(new Outcome(programPath, Length(programPath), projectPath, Length(projectPath), replaced,
                null, false));
        }

        internal void NotMade(string why, bool boxOpen)
        {
            Reached(Stage.Finished);
            ended.TrySetResult(new Outcome(null, 0, null, 0, false, why, boxOpen));
        }

        private static long Length(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;
    }

    private static Make? latest;
    private static int started;

    /// <summary>The Make started last, or null when none has been.</summary>
    public static Make? Latest => Volatile.Read(ref latest);

    /// <summary>How many Makes have started; a caller reads it before acting, and a Make numbered past it is the
    /// one its action started.</summary>
    public static int Started => Volatile.Read(ref started);

    internal static Make Begin()
    {
        var make = new Make(Interlocked.Increment(ref started));
        Volatile.Write(ref latest, make);
        return make;
    }
}
