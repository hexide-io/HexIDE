using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
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

    public Task ShowManagedWindow(MDIWindow window)
    {
        var tcs = new TaskCompletionSource<bool>();
        window.CloseCommand = new DelegateCommand(() => tcs.SetResult(true), () => true);
        var blocker = new MDIBlockerControl();
        var mainView = Static.MainView;
        mainView.PART_ModalHost.Children.Add(blocker);
        mainView.PART_ModalHost.Children.Add(window);

        async Task WindowTask()
        {
            await tcs.Task;
            mainView.PART_ModalHost.Children.Remove(window);
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

    public async Task<string?> InputBox(string prompt, string? caption, string defaultText)
    {
        caption ??= "HexIDE";
        if (Static.SingleView)
        {
            var inputBox = new InputBox
            {
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
            var inputBox = new InputBox { Prompt = prompt, Text = defaultText };
            var window = MakeDialogWindow(caption, inputBox);
            inputBox.TextRequest += r => { result = r; window.Close(); };
            await window.ShowDialog(GetTopWindow(desktop));
            return result;
        }
        else
            throw new NotImplementedException();
    }

    public async Task<MessageBoxResult> MessageBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        caption ??= "HexIDE";
        if (Static.SingleView)
        {
            var messageBox = new MessageBox
            {
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
            MessageBoxResult result = MessageBoxResult.None;
            var messageBox = new MessageBox { Text = text, Buttons = buttons, Icon = icon };
            var window = MakeDialogWindow(caption, messageBox);
            messageBox.AcceptRequest += r => { result = r; window.Close(); };
            await window.ShowDialog(GetTopWindow(desktop));
            return result;
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
            var owner = GetTopWindow(desktop);
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
        throw new NotImplementedException();
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