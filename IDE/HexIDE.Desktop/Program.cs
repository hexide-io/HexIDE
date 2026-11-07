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

        if (ServerOptions.ProfileArgumentError is { } profileError)
        {
            var localization = new HexIDE.Localization.LocalizationService();
            ConsoleOutput.Write(localization.GetStringFrom("system", profileError) + Environment.NewLine);
            Environment.ExitCode = 2;
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

#if DEBUG
        if (ServerOptions.ServerPortInvalid is { } badPort)
        {
            ConsoleOutput.Write(
                $"--server-port needs a port number from 1 to 65535 after it, and got '{badPort}'. HexIDE has not "
              + "started, rather than start without the automation server you asked for." + Environment.NewLine);
            Environment.ExitCode = 2;
            return;
        }

        if (ServerOptions.SessionMissing)
        {
            ConsoleOutput.Write(
                "--session needs the name of an agent session after it. HexIDE has not started, rather than start an "
              + "automation server that answers every session's client." + Environment.NewLine);
            Environment.ExitCode = 2;
            return;
        }

        if (ServerOptions.AnsiCodePageInvalid is { } badCodePage)
        {
            ConsoleOutput.Write(
                $"--ansi-code-page needs a Windows code page number after it, such as 932, and got '{badCodePage}'. "
              + "HexIDE has not started: reading files in this machine's own code page instead would look as "
              + "though the flag had worked." + Environment.NewLine);
            Environment.ExitCode = 2;
            return;
        }

        if (ServerOptions.DpiUnaware && !OperatingSystem.IsWindows())
        {
            ConsoleOutput.Write(
                "--dpi-unaware works on Windows only. HexIDE has not started, rather than start at the display's own "
              + "scale and look as though the flag had worked. On Linux, Avalonia's X11 platform reads "
              + "AVALONIA_GLOBAL_SCALE_FACTOR instead." + Environment.NewLine);
            Environment.ExitCode = 2;
            return;
        }
#endif

        // Before anything else starts: UserDataPath refuses a redirect once any per-user file has been looked up.
        if (ServerOptions.UserDataDirectory is { } userData)
            HexIDE.IDE.UserDataPath.RedirectTo(userData);

        try
        {
            HexIDE.Static.Profile = ServerOptions.ResolveProfile(invocationDirectory, HexIDE.IDE.UserDataPath.Directory);
        }
        catch (HexIDE.Profiles.ProfileLoadException ex)
        {
            var localization = new HexIDE.Localization.LocalizationService();
            var message = string.Format(localization.GetStringFrom("system", "Str.Profiles.LoadError"), ex.SourceId, ex.File);
            ConsoleOutput.Write(message + Environment.NewLine);
            Environment.ExitCode = 2;
            return;
        }

        DesktopStartup.Register();
        // The lifetime returns the code the app shut down with. Discarding it made every exit 0, including
        // the one a server that could not bind asks for (hexide-io/HexIDE#53).
        var builder = BuildAvaloniaApp();
#if DEBUG
        // Avalonia's only scale override on Windows: unaware, Windows tells the process the display is at 96 DPI, so
        // RenderScaling is 1 and Windows stretches the window on screen to the display's scaling (#3083).
        if (ServerOptions.DpiUnaware)
            builder = builder.With(new Win32PlatformOptions { DpiAwareness = Win32DpiAwareness.Unaware });
#endif
        // On a thread of its own, because VB code runs on the UI thread and this first thread's stack is the
        // launcher's choice, 1.5 MiB on Windows (#3228).
        Environment.ExitCode = HexIDE.Runtime.UiThread.Run(() => builder.StartWithClassicDesktopLifetime(args));
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
