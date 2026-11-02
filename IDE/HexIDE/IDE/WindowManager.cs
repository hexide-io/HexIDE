using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using HexIDE.Controls;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.Localization;
using HexIDE.Runtime.Dialogs;
using HexIDE.Utils;

namespace HexIDE.IDE;

public class WindowManager : IWindowManager
{
    private readonly ILocalizationService _localization;

    // Avalonia's modal windows use this native API, but its reference assembly hides it. Keep the bridge local
    // rather than opting the whole project into unstable APIs; desktop regression tests exercise it (#1366).
    private static readonly Action<IWindowImpl, bool> SetNativeEnabled = typeof(IWindowImpl)
        .GetMethod("SetEnabled")!.CreateDelegate<Action<IWindowImpl, bool>>();

    public WindowManager(ILocalizationService localization)
    {
        _localization = localization;
    }

    private Window GetTopWindow(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        foreach (var window in lifetime.Windows)
        {
            if (window.IsActive)
                return window;
        }

        return lifetime.MainWindow ?? throw new Exception("No main window and trying to open a window, fatal error");
    }

    public Task ShowManagedWindow(MDIWindow window) => ShowManagedWindow(window, blockBehind: true);

    public Task ShowManagedWindow(MDIWindow window, bool blockBehind)
    {
        var tcs = new TaskCompletionSource<bool>();
        window.CloseCommand = new DelegateCommand(() => tcs.TrySetResult(true), () => true);
        var blocker = blockBehind ? new MDIBlockerControl() : null;
        var mainView = Static.MainView;
        if (blocker is not null)
            mainView.PART_ModalHost.Children.Add(blocker);
        mainView.PART_ModalHost.Children.Add(window);

        async Task WindowTask()
        {
            await tcs.Task;
            mainView.PART_ModalHost.Children.Remove(window);
            if (blocker is not null)
                mainView.PART_ModalHost.Children.Remove(blocker);
        }

        return WindowTask();
    }

    private Task ShowManagedWindow(out MDIWindow window, string? title = null, object? content = null, bool canClose = true)
    {
        window = new MDIWindow()
        {
            Title = title ?? "",
            CanClose = canClose,
            Content = new ContentControl()
            {
                Content = content,
                DataContext = content
            },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        return ShowManagedWindow(window);
    }

    public async Task ShowWindow(IDialog dialog)
    {
        if (Static.SingleView)
        {
            var task = ShowManagedWindow(out var window, dialog.Title, dialog);

            dialog.CloseRequested += DialogOnCloseRequested;

            void DialogOnCloseRequested(bool result) => window.CloseCommand.Execute(window.CloseCommandParameter);

            await task;

            dialog.CloseRequested -= DialogOnCloseRequested;
        }
        else if (Application.Current!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new DialogWindow()
            {
                Content = dialog,
                DataContext = dialog,
                SizeToContent = SizeToContent.WidthAndHeight,
                ShowActivated = true,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var lifetime = new TaskCompletionSource();
            void OnWindowClosed(object? sender, EventArgs e) => lifetime.SetResult();
            window.Closed += OnWindowClosed;
            window.Show();
            await lifetime.Task;
            window.Closed -= OnWindowClosed;
        }
        else
            throw new NotImplementedException();
    }

    public async Task<string?> InputBox(string prompt, string? caption, string defaultText, bool isProgramDialog = false)
    {
        caption ??= "HexIDE";
        if (Static.SingleView)
        {
            var inputBox = new InputBox
            {
                IsProgramDialog = isProgramDialog,
                Prompt = prompt,
                Text = defaultText
            };
            var task = ShowManagedWindow(out var window, caption, inputBox, false);

            string?[] ret = new string?[1];

            inputBox.TextRequest += InputBoxOnCloseRequested;

            void InputBoxOnCloseRequested(string? result)
            {
                ret[0] = result;
                window.CloseCommand.Execute(window.CloseCommandParameter);
            }

            await task;

            inputBox.TextRequest -= InputBoxOnCloseRequested;

            return ret[0];
        }
        else if (Application.Current!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            string? result = null;
            var inputBox = new InputBox { IsProgramDialog = isProgramDialog, Prompt = prompt, Text = defaultText };
            var window = MakeDialogWindow(caption, inputBox);
            inputBox.TextRequest += r => { result = r; window.Close(); };
            await window.ShowDialog(GetTopWindow(desktop));
            return result;
        }
        else
            throw new NotImplementedException();
    }

    public Task<MessageBoxResult> TaskModalMessageBox(string text, string? caption = null, MessageBoxButtons buttons = MessageBoxButtons.Ok, MessageBoxIcon icon = MessageBoxIcon.None, Window? owner = null)
        => !Static.SingleView && Application.Current!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? TaskModalMessageBox(desktop, text, caption, buttons, icon, owner)
            : MessageBox(text, caption, buttons, icon, owner);

    internal async Task<MessageBoxResult> TaskModalMessageBox(IClassicDesktopStyleApplicationLifetime desktop, string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon, Window? owner)
    {
        owner ??= GetTopWindow(desktop);
        var held = desktop.Windows.Select(window => (Window: window, WasEnabled: window.IsEnabled)).ToArray();
        try
        {
            foreach (var (window, _) in held)
            {
                window.SetCurrentValue(Window.IsEnabledProperty, false);
                // IsEnabled holds Avalonia input; the native window must also refuse activation and clicks (#1366).
                if (window.PlatformImpl is { } platform) SetNativeEnabled(platform, false);
            }
            return await ShowDesktopMessageBox(desktop, text, caption ?? "HexIDE", buttons, icon, owner);
        }
        finally
        {
            foreach (var (window, wasEnabled) in held)
            {
                window.SetCurrentValue(Window.IsEnabledProperty, wasEnabled);
                // An existing modal child still holds its owner after this question has finished.
                if (window.PlatformImpl is { } platform)
                    SetNativeEnabled(platform, wasEnabled && !window.OwnedWindows.Any(child => child.IsDialog));
            }
        }
    }

    private Task<MessageBoxResult> ShowDesktopMessageBox(IClassicDesktopStyleApplicationLifetime desktop, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, Window? owner, bool isProgramDialog = false)
    {
        var messageBox = new MessageBox { IsProgramDialog = isProgramDialog, Text = text, Buttons = buttons, Icon = icon };
        var window = MakeDialogWindow(caption, messageBox);
        return messageBox.ShowAndWait(window, owner ?? GetTopWindow(desktop));
    }

    public async Task<MessageBoxResult> MessageBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon, Window? owner, bool isProgramDialog = false)
    {
        caption ??= "HexIDE";
        if (Static.SingleView)
        {
            var messageBox = new MessageBox
            {
                IsProgramDialog = isProgramDialog,
                Text = text,
                Buttons = buttons,
                Icon = icon
            };
            var task = ShowManagedWindow(out var window, caption, messageBox, false);

            MessageBoxResult[] ret = [MessageBoxResult.None];

            messageBox.AcceptRequest += MessageBoxOnCloseRequested;

            void MessageBoxOnCloseRequested(MessageBoxResult result)
            {
                ret[0] = result;
                window.CloseCommand.Execute(window.CloseCommandParameter);
            }

            await task;

            messageBox.AcceptRequest -= MessageBoxOnCloseRequested;

            return ret[0];
        }
        else if (Application.Current!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // A person's close answers as Windows' does, or is refused where the box has none, and a box hidden with
            // its owner keeps waiting (see ShowAndWait).
            return await ShowDesktopMessageBox(desktop, text, caption, buttons, icon, owner, isProgramDialog);
        }
        else
            throw new NotImplementedException();
    }

    public async Task ShowAbout(AboutDialogOptions options)
    {
        var vm = new AboutDialogViewModel(options);
        var dialog = new AboutDialog { DataContext = vm };
        var title = string.Format(_localization.GetString("Str.WindowManager.AboutTitle"), options.Title);

        if (Static.SingleView)
        {
            var task = ShowManagedWindow(out var window, title, dialog, false);
            vm.CloseRequested += () => window.CloseCommand.Execute(window.CloseCommandParameter);
            await task;
        }
        else if (Application.Current!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = MakeDialogWindow(title, dialog);
            vm.CloseRequested += window.Close;
            await window.ShowDialog(GetTopWindow(desktop));
        }
        else
            throw new NotImplementedException();
    }

    public async Task<int?> ShowColorDialog(int initial)
    {
        var dialog = new ColorDialog(initial);
        int? result = null;
        var title = _localization.GetString("Str.Automation.ChooseColor");
        if (Static.SingleView)
        {
            var task = ShowManagedWindow(out var window, title, dialog, false);
            dialog.ColorRequest += r => { result = r; window.CloseCommand.Execute(window.CloseCommandParameter); };
            await task;
        }
        else if (Application.Current!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = MakeDialogWindow(title, dialog);
            dialog.ColorRequest += r => { result = r; window.Close(); };
            await window.ShowDialog(GetTopWindow(desktop));
        }
        else
            throw new NotImplementedException();
        return result;
    }

    public async Task<bool> ShowPrintDialog()
    {
        var dialog = new PrintDialog();
        var result = false;
        var title = _localization.GetString("Str.Tool.Project.Menu.Print");
        if (Static.SingleView)
        {
            var task = ShowManagedWindow(out var window, title, dialog, false);
            dialog.PrintRequest += r => { result = r; window.CloseCommand.Execute(window.CloseCommandParameter); };
            await task;
        }
        else if (Application.Current!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = MakeDialogWindow(title, dialog);
            dialog.PrintRequest += r => { result = r; window.Close(); };
            await window.ShowDialog(GetTopWindow(desktop));
        }
        else
            throw new NotImplementedException();
        return result;
    }

    public async Task<FontDialogResult?> ShowFontDialog(FontDialogResult? initial = null)
    {
        var vm = new FontDialogViewModel(initial);
        var dialog = new FontDialog { DataContext = vm };
        FontDialogResult? result = null;

        if (Static.SingleView)
        {
            var task = ShowManagedWindow(out var window, _localization.GetString("Str.WindowManager.FontTitle"), dialog, false);
            vm.AcceptRequested += r => { result = r; window.CloseCommand.Execute(window.CloseCommandParameter); };
            vm.CancelRequested += ()  => window.CloseCommand.Execute(window.CloseCommandParameter);
            await task;
        }
        else if (Application.Current!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = MakeDialogWindow(_localization.GetString("Str.WindowManager.FontTitle"), dialog);
            vm.AcceptRequested += r => { result = r; window.Close(); };
            vm.CancelRequested += window.Close;
            await window.ShowDialog(GetTopWindow(desktop));
        }
        else
            throw new NotImplementedException();

        return result;
    }

    public async Task<IReadOnlyList<string>?> OpenFilePickerAsync(FilePickerOpenOptions options)
    {
#if DEBUG
        // An automation client cannot reach a native dialog — it is outside the control tree, and a modal
        // one stops the automation server answering at all. An armed answer replaces the question a person
        // would have answered, and nothing else: every line below the picker is the code a real click
        // reaches. Compiled out of a shipped build along with the server it exists for.
        if (ScriptedFileDialogs.TryTake(options.AllowMultiple, out var scripted,
                options.AllowMultiple ? "an Open picker" : "an Open picker that takes one file"))
            return scripted;
#endif

        if (Application.Current!.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime classic)
            throw new Exception("This is supported only for desktop apps");

        var topLevel = TopLevel.GetTopLevel(GetTopWindow(classic))!;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(options);

        return files.Select(f => f.TryGetLocalPath())
            .Where(x => x != null)
            .Cast<string>()
            .ToList();
    }

    public async Task<string?> SaveFilePickerAsync(FilePickerSaveOptions options)
    {
#if DEBUG
        if (ScriptedFileDialogs.TryTake(takesSeveral: false, out var scripted, "a save picker"))
            return scripted?[0];
#endif

        if (Application.Current!.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime classic)
            throw new Exception("This is supported only for desktop apps");

        var topLevel = TopLevel.GetTopLevel(GetTopWindow(classic))!;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(options);

        return file?.TryGetLocalPath();
    }

    public async Task<bool> ShowDialog(IDialog dialog)
    {
        if (Static.SingleView)
        {
            var lifetime = ShowManagedWindow(out var window, dialog.Title, dialog);

            bool[] ret = new bool[1];
            
            dialog.CloseRequested += DialogOnCloseRequested;

            void DialogOnCloseRequested(bool result)
            {
                ret[0] = result;
                window.CloseCommand?.Execute(window.CloseCommandParameter);
            }

            await lifetime;

            dialog.CloseRequested -= DialogOnCloseRequested;

            return ret[0];
        }
        else if (Application.Current!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return await ShowDialog(desktop, dialog);
        }
        throw new NotImplementedException();
    }

    internal async Task<bool> ShowDialog(IClassicDesktopStyleApplicationLifetime desktop, IDialog dialog)
    {
        var owner = GetTopWindow(desktop);
        if (dialog is LanguageRevertGateViewModel)
        {
            // Activation can still point at the main window while Options is open (#623).
            owner = desktop.MainWindow ?? owner;
            while (owner.OwnedWindows.LastOrDefault(child => child.IsVisible) is { } child)
                owner = child;
        }
        var window = new DialogWindow()
        {
            Content = dialog,
            DataContext = dialog,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = dialog.CanResize,
            ShowActivated = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            // Inherit the chrome's reading direction so dialogs mirror under an RTL language.
            FlowDirection = owner.FlowDirection
        };
        if (dialog.CanResize)
        {
            // Let the window measure itself at content size, then unlock for user resizing
            window.Opened += (_, _) => window.SizeToContent = SizeToContent.Manual;
        }
        return await window.ShowDialog<bool>(owner);
    }

    private static Window MakeDialogWindow(string title, Control content) => new Window
    {
        Title = title,
        Content = content,
        SizeToContent = SizeToContent.WidthAndHeight,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        CanResize = false,
        ShowActivated = true
    };
}