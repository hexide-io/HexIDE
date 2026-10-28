
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using HexIDE.IDE;
using HexIDE.Runtime;
using HexIDE.Runtime.Dialogs;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Standalone;

public partial class App : Application
{
    private StandaloneRun? runner;

    public override void Initialize()
    {
        // The user's regional settings, as the IDE takes them (#777). This host pins no culture of its own, so the
        // thread's culture would give the same answer today; set explicitly, so the two hosts cannot drift apart.
        VbLocale.Default = System.Globalization.CultureInfo.CurrentCulture;
        AvaloniaXamlLoader.Load(this);
    }

    private static string ErrorString(string key)
    {
        using var stream = typeof(App).Assembly.GetManifestResourceStream("HexIDE.Standalone.ErrorStrings.json")!;
        using var pack = System.Text.Json.JsonDocument.Parse(stream);
        return pack.RootElement.GetProperty("strings").GetProperty(key).GetString()!;
    }

    private static Task ShowFatalError(Window window)
    {
        var closed = new TaskCompletionSource();
        window.Closed += (_, _) => closed.TrySetResult();
        window.Show();
        return closed.Task;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Task? run = null;
            if (Program.Project != null)
            {
                // End exits the program, with code 0, as the exe vb6.exe makes does (#775). Posted, because End is
                // reached in the middle of a walk on this thread.
                BasicInterpreter.EndReached += _ => Dispatcher.UIThread.Post(() => desktop.Shutdown(0));
                // The program ends when its last form unloads, and Sub Main has returned where it starts there (#822). Not
                // through MainWindow: the lifetime would show a MainWindow as it starts, and the form is shown only once its
                // Load has run, which a MsgBox in Load can delay.
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                try
                {
                    // Every form with no Icon line runs with the runner's icon; a form's own Icon, or (None), is its own (#1594).
                    VBLoader.DefaultFormIcon = new WindowIcon(AssetLoader.Open(new Uri("avares://HexIDE.Standalone/form.ico")));
                    runner = new StandaloneRun(Program.Project, ShowFatalError, () => Environment.Exit(Environment.ExitCode), ErrorString);
                    desktop.Exit += (_, _) => runner.Dispose();
                    run = runner.Run(default);
                    run.ContinueWith(_ => Dispatcher.UIThread.Post(() =>
                    {
                        if (!runner.IsReporting)
                            desktop.Shutdown(0);
                    }));
                }
                catch (Exception e)
                {
                    // A start that never became a run: no startup object, no usable Main, or a syntax error in a module.
                    Program.Error = e.Message;
                    run = null;
                }
            }
            if (run is null)
            {
                var window = new Window()
                {
                    SizeToContent = SizeToContent.WidthAndHeight,
                    CanResize = false
                };
                var msgBox = new MessageBox()
                {
                    Text = Program.Error ?? "Unknown error",
                    Icon = MessageBoxIcon.Error,
                    Buttons = MessageBoxButtons.Ok
                };
                window.Content = msgBox;
                desktop.MainWindow = window;
                window.Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://HexIDE.Standalone/form.ico")));
                msgBox.AcceptRequest += _ => window.Close();
            }
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            throw new NotImplementedException();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
