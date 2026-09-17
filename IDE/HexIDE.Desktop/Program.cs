using System;
using System.IO;
using Avalonia;
using Avalonia.Media.Fonts;
using Classic.CommonControls;

namespace HexIDE.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Read before FixCurrentWorkingDictionary moves it. A relative path on the command line means
        // relative to the shell it was typed in; resolved after the move, ../demo/x.vbp pointed inside
        // bin/ and the IDE opened nothing, without a word.
        var invocationDirectory = Environment.CurrentDirectory;
        FixCurrentWorkingDictionary();
        ServerOptions.ParseArgs(args, invocationDirectory);

        // Before anything Avalonia touches: --help answers and leaves, so asking what the flags are never
        // starts an IDE, loads a project or opens a port.
        if (ServerOptions.HelpRequested)
        {
            ConsoleOutput.Write(ServerOptions.HelpText);
            return;
        }

        if (ServerOptions.UserDataDirectoryMissing)
        {
            ConsoleOutput.Write(
                "--user-data-dir needs a directory after it. HexIDE has not started, rather than start on "
              + "the settings you asked it to keep away from." + Environment.NewLine);
            Environment.ExitCode = 2;
            return;
        }

        // Before anything else starts: UserDataPath refuses a redirect once any per-user file has been looked up.
        if (ServerOptions.UserDataDirectory is { } userData)
            HexIDE.IDE.UserDataPath.RedirectTo(userData);

        DesktopStartup.Register();
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    private static void FixCurrentWorkingDictionary()
    {
        if (Path.GetDirectoryName(Environment.ProcessPath) is { } dir)
        {
            Environment.CurrentDirectory = dir;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseMessageBoxSounds()
            .LogToTrace()
            .ConfigureFonts(manager =>
            {
                manager.AddFontCollection(new EmbeddedFontCollection(new Uri("fonts:App", UriKind.Absolute),
                    new Uri("avares://HexIDE/Resources", UriKind.Absolute)));
            });
}
