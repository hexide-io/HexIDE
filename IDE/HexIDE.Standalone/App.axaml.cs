
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using HexIDE.IDE;
using HexIDE.Runtime;
using HexIDE.Runtime.Dialogs;

namespace HexIDE.Standalone;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Program.StartupForm != null)
            {
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
