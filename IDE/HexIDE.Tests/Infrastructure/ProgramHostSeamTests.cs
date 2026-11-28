using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// The run engine shows a program's dialogs and gets its failure texts through Core's <c>IProgramHost</c> (#4066), so an
/// engine outside this repository inherits that contract and not the IDE's window manager or localization service.
/// </summary>
public class ProgramHostSeamTests
{
    private static readonly Regex Forbidden = new(
        @"\b(MDIWindow|MDIHostPanel|IWindowManager|ShowManagedWindow|ActivateMDIForm|ActionObserver|DelegateCommand|ILocalizationService|HexIDE\.Localization|OpenFilePickerAsync|SaveFilePickerAsync)\b|windowManager\.(MessageBox|InputBox|ShowPrintDialog|ShowColorDialog|ShowFontDialog)");

    [Fact]
    public void Run_engine_shows_program_dialogs_and_gets_failure_texts_through_Core()
    {
        // The engine is the provider's now (#5616), where the IDE's window types can't even be named. Finding the file is part
        // of the test, so a rename can't leave the guard passing over nothing.
        var engine = Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Runtime", "Running", "InterpreterRunEngine.cs");
        File.Exists(engine).Should().BeTrue("the guard reads the run engine where the provider keeps it");
        File.Exists(Path.Combine(RepoTree.Root(), "IDE", "HexIDE", "IDE", "InterpreterRunEngine.cs")).Should().BeFalse();
        Forbidden.IsMatch(File.ReadAllText(engine)).Should().BeFalse();
    }
    [Fact]
    public void Program_host_and_file_picker_mapping_do_not_name_the_runtime()
    {
        // #4139: the IDE must retain its picker mapping when the run engine moves out.
        ProductSource.FilesMatching(new Regex(@"\bHexIDE\.Runtime\b"))
            .Where(file => Path.GetFileName(file) is "ProgramHost.cs" or "ProgramFileDialogOptions.cs")
            .Should().BeEmpty();
        File.Exists(Path.Combine(RepoTree.Root(), "IDE", "HexIDE", "IDE", "ProgramFileDialogOptions.cs"))
            .Should().BeTrue();
    }
}
