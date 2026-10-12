
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
    public override void Initialize()
    {
        // The user's regional settings, as the IDE takes them (#777). This host pins no culture of its own, so the
        // thread's culture would give the same answer today; set explicitly, so the two hosts cannot drift apart.
        VbLocale.Default = System.Globalization.CultureInfo.CurrentCulture;
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Program.StartupForm != null)
            {
                // End exits the program, with code 0, as the exe vb6.exe makes does (#775). Posted, because End is
                // reached in the middle of a walk on this thread.
                BasicInterpreter.EndReached += _ => Dispatcher.UIThread.Post(() => desktop.Shutdown(0));
                VBLoader.RunForm(Program.StartupForm, default, out var window);
                window.Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://HexIDE.Standalone/form.ico")));
                desktop.MainWindow = window;
            }
            else
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
