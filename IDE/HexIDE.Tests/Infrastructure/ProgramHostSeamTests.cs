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
        // Until step 2b moves the engine out, it is this one file. Finding it is part of the test, so a rename can't leave
        // the guard passing over nothing.
        File.Exists(Path.Combine(RepoTree.Root(), "IDE", "HexIDE", "IDE", "InterpreterRunEngine.cs"))
            .Should().BeTrue("the guard reads the run engine where it stands until step 2b moves it");
        ProductSource.FilesMatching(Forbidden)
            .Where(file => Path.GetFileName(file) == "InterpreterRunEngine.cs")
            .Should().BeEmpty();
    }
}
