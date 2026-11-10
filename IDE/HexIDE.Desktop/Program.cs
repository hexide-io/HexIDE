using System;
using System.IO;
using Avalonia;
using Avalonia.Media.Fonts;
using Classic.CommonControls;

namespace HexIDE.Desktop;

sealed partial class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [System.Runtime.InteropServices.LibraryImport("user32.dll", EntryPoint = "MessageBoxW",
        StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf16)]
    private static partial int ShowNativeMessageBox(nint owner, string text, string caption, uint type);

    private static string? EstablishDistribution()
    {
        try
        {
            HexIDE.IDE.Distribution.Establish(HexIDE.IDE.DistributionDescriptor.Resolve(AppContext.BaseDirectory));
            WindowsTaskbarIdentity.Apply(HexIDE.IDE.Distribution.Current);
            return null;
        }
        catch (HexIDE.IDE.DistributionDescriptorException ex)
        {
            return $"This installation's branding cannot be used, so the IDE has not started and no settings, logs or "
                 + $"recovery files were opened. {ex.Message}";
        }
    }

    // A shortcut or Explorer gives this program no console, and on Windows a start that says nothing looks like a hang.
    private static void ReportDistributionError(string message)
    {
        const uint iconError = 0x10;
        if (!ConsoleOutput.Write(message + Environment.NewLine) && OperatingSystem.IsWindows())
            ShowNativeMessageBox(0, message, "Cannot start", iconError);
    }

    [STAThread]
    public static void Main(string[] args)
    {
        // Read before FixCurrentWorkingDictionary moves it. A relative path on the command line means
        // relative to the shell it was typed in; resolved after the move, ../demo/x.vbp pointed inside
        // bin/ and the IDE opened nothing, without a word.
        var invocationDirectory = Environment.CurrentDirectory;
        FixCurrentWorkingDictionary();

        // First of all: every per-user path, log and caption is derived from this (#2042). A failure is held until
        // the arguments are read, so that --help still answers on a broken installation.
        var distributionError = EstablishDistribution();

        ServerOptions.ParseArgs(args, invocationDirectory);

        // Before anything Avalonia touches: --help answers and leaves, so asking what the flags are never
        // starts an IDE, loads a project or opens a port.
        if (ServerOptions.HelpRequested)
        {
            if (distributionError != null)
            {
                ConsoleOutput.Write(distributionError + Environment.NewLine);
                Environment.ExitCode = 2;
            }

            ConsoleOutput.Write(ServerOptions.HelpText);
            return;
        }

        // Refused rather than started as stock: stock would open HexIDE's own settings and recovery files.
        if (distributionError != null)
        {
            ReportDistributionError(distributionError);
            Environment.ExitCode = 2;
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
                "--user-data-dir needs a directory after it. " + HexIDE.IDE.Distribution.DisplayName + " has not started, rather than start on "
              + "the settings you asked it to keep away from." + Environment.NewLine);
            Environment.ExitCode = 2;
            return;
        }

#if DEBUG
        if (ServerOptions.ServerPortInvalid is { } badPort)
        {
            ConsoleOutput.Write(
                $"--server-port needs a port number from 1 to 65535 after it, and got '{badPort}'. " + HexIDE.IDE.Distribution.DisplayName + " has not "
              + "started, rather than start without the automation server you asked for." + Environment.NewLine);
            Environment.ExitCode = 2;
            return;
        }

        if (ServerOptions.SessionMissing)
        {
            ConsoleOutput.Write(
                "--session needs the name of an agent session after it. " + HexIDE.IDE.Distribution.DisplayName + " has not started, rather than start an "
              + "automation server that answers every session's client." + Environment.NewLine);
            Environment.ExitCode = 2;
            return;
        }

        if (ServerOptions.AnsiCodePageInvalid is { } badCodePage)
        {
            ConsoleOutput.Write(
                $"--ansi-code-page needs a Windows code page number after it, such as 932, and got '{badCodePage}'. "
              + "" + HexIDE.IDE.Distribution.DisplayName + " has not started: reading files in this machine's own code page instead would look as "
              + "though the flag had worked." + Environment.NewLine);
            Environment.ExitCode = 2;
            return;
        }

        if (ServerOptions.DpiUnaware && !OperatingSystem.IsWindows())
        {
            ConsoleOutput.Write(
                "--dpi-unaware works on Windows only. " + HexIDE.IDE.Distribution.DisplayName + " has not started, rather than start at the display's own "
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
        var builder = LinuxWindowIdentity.Apply(BuildAvaloniaApp(), HexIDE.IDE.Distribution.Current);
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
