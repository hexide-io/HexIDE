using System.Globalization;
using HexIDE.Projects;

namespace HexIDE.Desktop.Server;

/// <summary>
/// What a Make started from the menu ended with, for <c>invoke_menu_item</c>'s reply (#2671). The menu command returns as
/// soon as Make starts, so the reply used to say only "Invoked" whether a program was made or not.
/// </summary>
internal static class MakeReply
{
    /// <summary>How long a Make is given to finish. Most of it is copying the standalone runner, a single file of
    /// .NET and the runtime's assemblies.</summary>
    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    /// <summary>How long Make may sit at its save dialog before the reply says it waits there. An answer armed with
    /// answer_next_file_dialog is given at once, so only a dialog nothing answered stays open this long.</summary>
    internal static readonly TimeSpan PickerGrace = TimeSpan.FromSeconds(1);

    internal const string AtPicker =
        "Make is waiting at its save dialog, which no answer was armed for. The dialog is the operating system's, which no "
        + "tool reaches, so it stays open until someone answers it. To make a program from automation, call "
        + "answer_next_file_dialog with the program's path, then Make.";

    public static async Task<string> AwaitAsync(MakeOutcomes.Make make, CancellationToken ct, TimeSpan? limit = null)
    {
        var wait = limit ?? Limit;
        var deadline = Environment.TickCount64 + (long)wait.TotalMilliseconds;
        while (!make.Ended.IsCompleted)
        {
            if (make.Current == MakeOutcomes.Stage.AtPicker && make.InStage >= PickerGrace)
                return AtPicker;
            if (Environment.TickCount64 >= deadline)
                return $"Make hasn't finished after {wait.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s, and "
                       + (make.Current == MakeOutcomes.Stage.Writing
                           ? "is still writing the program."
                           : "is still checking the project, before its save dialog.")
                       + " A message box it opens appears in dump_visual_tree.";
            await Task.WhenAny(make.Ended, Task.Delay(100, ct));
        }
        return Describe(await make.Ended);
    }

    internal static string Describe(MakeOutcomes.Outcome outcome) => outcome.WhyNot is { } why
        ? $"No program was made: {why}."
          + (outcome.BoxOpen ? " A message box saying so is open, and Make waits on it: answer it with interact." : "")
        : $"Made {outcome.ProgramPath} ({Bytes(outcome.ProgramSize)})"
          + (outcome.Replaced ? ", writing over the file that was there" : "")
          // What the two files are, which a first-contact caller couldn't tell from their names.
          + $". It is the standalone runner, which carries .NET, and it runs the project zipped beside it in "
          + $"{outcome.ProjectPath} ({Bytes(outcome.ProjectSize)}).";

    private static string Bytes(long n) => n.ToString("N0", CultureInfo.InvariantCulture) + " bytes";
}
