using System;
using System.IO;

namespace HexIDE.Tests.Infrastructure;

public class ProgramDialogChromeRegistrationTests
{
    [Fact]
    public void Native_caption_callback_is_guarded_by_Windows_and_program_dialogs()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "openspec")))
            root = root.Parent;
        root.Should().NotBeNull();
        string Source(string file) => File.ReadAllText(Path.Combine(root!.FullName,
            "IDE", "HexIDE.Runtime", "Dialogs", file)).ReplaceLineEndings("\n");

        // #4099: the IDE's own dialogs and non-Windows hosts must not register this callback.
        Source("ProgramDialogChrome.cs").Should().Contain(
            "if (window.Content is not MessageBox { IsProgramDialog: true } and not InputBox { IsProgramDialog: true })\n"
            + "            return;\n        if (OperatingSystem.IsWindows())\n        {\n"
            + "            Win32Properties.AddWindowStylesCallback(window, WindowsStyles);\n"
            + "            Win32Properties.AddWndProcHookCallback(window, NoIcon);\n        }");
        Source("MessageBox.axaml.cs").Should().Contain(
            "if (IsProgramDialog)\n            ProgramDialogChrome.Apply(window);")
            .And.Contain("if (IsProgramDialog)\n            ProgramDialogChrome.Apply(again);");
        Source("InputBox.axaml.cs").Should().Contain(
            "if (IsProgramDialog)\n            ProgramDialogChrome.Apply(window);");
        // Before the window is shown, or Avalonia's icon is already in its title bar (live, 2026-11-16).
        File.ReadAllText(Path.Combine(root!.FullName, "IDE", "HexIDE", "IDE", "ProgramDialogs.cs")).ReplaceLineEndings("\n")
            .Should().Contain("var window = manager.MakeDialogWindow(caption, inputBox);\n            inputBox.ApplyProgramChrome(window);");
    }
}
