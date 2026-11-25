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
            "if (window.Content is not MessageBox and not InputBox)\n"
            + "            return;\n        if (OperatingSystem.IsWindows())\n        {\n"
            + "            Win32Properties.AddWindowStylesCallback(window, WindowsStyles);\n"
            + "            Win32Properties.AddWndProcHookCallback(window, NoIcon);\n        }");
        Source("MessageBox.axaml.cs").Should().Contain(
            "ProgramDialogChrome.Apply(window);")
            .And.Contain("ProgramDialogChrome.Apply(again);");
        Source("InputBox.axaml.cs").Should().Contain(
            "ProgramDialogChrome.Apply(window);");
        // Before the window is shown, or Avalonia's icon is already in its title bar (live, 2026-11-16).
        // The provider's own boxes now, in windows the IDE makes for them (#5616).
        Source("ProgramDialogs.cs").Should().Contain(
            "var window = (Window)windows.MakeWindow(caption, inputBox, ProgramBox.Input);\n            inputBox.ApplyProgramChrome(window);");
    }
}
