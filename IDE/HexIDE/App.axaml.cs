using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Controls;
using HexIDE.Infrastructure;
using HexIDE.Runtime.Interpreter;
using HexIDE.Keymaps;
using HexIDE.Themes;
using HexIDE.VisualDesigner;
using R3;
using Serilog;

namespace HexIDE;

public partial class App : Application
{
    private DISetup? _diSetup;

    public override void Initialize()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Initialise file logging before DI so ILogger<T> instances are backed by Serilog.
        LoggingSetup.Initialise();

        var envLevel = Environment.GetEnvironmentVariable("HEXIDE_LOG_LEVEL") ?? "Information";
        Log.Information("HexIDE starting — log level={LogLevel}, runtime={Runtime}, os={OS}",
            envLevel, Environment.Version, Environment.OSVersion);

        _diSetup = new DISetup();
        _diSetup.ThemeService.Apply(_diSetup.SettingsService.ActiveTheme);
        _diSetup.KeymapService.Apply(_diSetup.SettingsService.ActiveKeymap);

        // Register the VB6 highlighting definition and subscribe it to theme changes before any editor
        // is built, so the first paint is already correct rather than light-until-you-switch. Must
        // follow ThemeService.Apply above, which is what sets the variant this reads.
        SyntaxHighlightingTheme.EnsureRegistered();
        // Localization: apply the saved language pack. The default "system" follows the OS UI culture
        // each launch; a specific id pins that language. A pack-selector string only — does NOT touch
        // thread culture (the invariant lock set in Initialize() stands). Applied before the views are
        // built so the first render resolves every {DynamicResource Str.*}.
        _diSetup.LocalizationService.Apply(_diSetup.SettingsService.ActiveLanguage);
        var rootViewModel = _diSetup.Root;
        Static.RootViewModel = rootViewModel;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // BindingPlugins.DataValidators no longer applies to compiled bindings (Avalonia 12+).
            // This project uses AvaloniaUseCompiledBindingsByDefault=true so no removal is needed.

            // Start the VB6 LSP server in the background (Desktop only).
            _ = _diSetup.LspClient.StartAsync();

            // Eagerly construct the file watcher so it subscribes to project load/unload before the
            // startup project opens. Desktop-only — file watching makes no sense headless/WASM.
            _ = _diSetup.FileWatcherService;

            var windowStateService = _diSetup.WindowStateService;

            if (Static.ForceSingleView)
            {
                Static.SingleView = true;
                Static.MainView = new MainView
                {
                    DataContext = rootViewModel
                };

                desktop.MainWindow = new Window()
                {
                    Content = Static.MainView
                };

                rootViewModel.ObservePropertyChanged(x => x.WindowTitle)
                    .Subscribe(title => desktop.MainWindow.Title = title);

#if DEBUG
                desktop.MainWindow.AttachDevTools();
#endif

                Static.MainView.WindowInitialized();
            }
            else
            {
                var mainWindow = new MainWindow
                {
                    DataContext = rootViewModel
                };
                desktop.MainWindow = mainWindow;
                Static.SingleView = false;
                Static.MainView = mainWindow.MainView;

                var windowReady   = false;
                var windowClosing = false;

                // Restore must happen after Opened — setting WindowState before Show() has no effect
                // in Avalonia 12 on Windows. Deferred to Background priority so the dock framework's
                // first layout pass completes before we apply the saved state.
                mainWindow.Opened += async (_, _) =>
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                        () => windowStateService.RestoreWindowState(mainWindow),
                        Avalonia.Threading.DispatcherPriority.Background);
                    windowReady = true;
                };

                // Crash-safe saves during normal use.
                mainWindow.PropertyChanged += (_, e) =>
                {
                    if (windowReady && !windowClosing && e.Property == Window.WindowStateProperty)
                        windowStateService.SaveWindowState(mainWindow);
                };
                mainWindow.Deactivated += (_, _) =>
                {
                    if (windowReady && !windowClosing && mainWindow.WindowState == Avalonia.Controls.WindowState.Normal)
                        windowStateService.SaveWindowState(mainWindow);
                };

                var closeConfirmed = false;

                // Offer to save unsaved work, then save window state. The prompt is async and Closing is
                // not, so the first close is vetoed, the dialog awaited, and the window re-closed once
                // confirmed. Cancel in the dialog aborts the exit entirely, matching VB6.
                mainWindow.Closing += (_, e) =>
                {
                    if (!closeConfirmed && !Static.ForceCloseWithoutPrompt)
                    {
                        e.Cancel = true;
                        ConfirmThenClose();
                        return;
                    }

                    // Window state is captured here rather than later because Closing fires before the OS
                    // restores the window from Maximized.
                    windowClosing = true;
                    windowStateService.SaveWindowState(mainWindow);
                    rootViewModel.SaveLayout();
                };

                async void ConfirmThenClose()
                {
                    try
                    {
                        // Raises the save-changes prompt for anything dirty, saves what the user ticks,
                        // and throws OperationCanceledException if they choose Cancel.
                        await _diSetup.ProjectService.UnloadAllProjects();
                    }
                    catch (OperationCanceledException)
                    {
                        return; // user cancelled — stay open
                    }
                    catch (Exception ex)
                    {
                        // A save failed. Staying open keeps the work recoverable; closing would not.
                        Log.Error(ex, "Save-on-exit failed — leaving the window open");
                        return;
                    }

                    closeConfirmed = true;
                    mainWindow.Close();
                }
            }

            desktop.ShutdownRequested += async (_, _) =>
            {
                _diSetup.FileWatcherService.Dispose();
                await _diSetup.LspClient.StopAsync();
                LoggingSetup.Shutdown();
            };

            // The language pack was applied before the shell existed, so push its reading direction
            // onto the freshly-created window now (RTL packs mirror the chrome; the form-designer
            // canvas stays LTR via its own pinned FlowDirection).
            desktop.MainWindow!.FlowDirection = _diSetup.LocalizationService.FlowDirection;

            Static.DesktopStartupHook?.Invoke(_diSetup, desktop);
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            Static.SingleView = true;
            singleViewPlatform.MainView = Static.MainView = new MainView
            {
                DataContext = rootViewModel
            };
            Static.MainView.FlowDirection = _diSetup.LocalizationService.FlowDirection;
            Static.MainView.WindowInitialized();
        }

        base.OnFrameworkInitializationCompleted();
    }
}