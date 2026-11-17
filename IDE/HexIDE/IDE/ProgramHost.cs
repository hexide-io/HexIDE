using System;
using System.Linq;
using Avalonia.Interactivity;
using HexIDE.Controls;
using HexIDE.Utils;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using HexIDE.Localization;

namespace HexIDE.IDE;

/// <summary>The IDE's dialogs and failure texts for a running program (#4066).</summary>
public sealed class ProgramHost(IWindowManager windowManager, ILocalizationService localization) : IProgramHost
{
    public IHostedForm CreateForm(HostedFormRequest request) => new HostedForm(windowManager, request);

    private sealed class HostedForm : IHostedForm
    {
        private readonly MDIWindow window;
        private readonly IWindowManager windowManager;
        private Task? inHost;
        private System.Windows.Input.ICommand? hostClose;
        public object Window => window;
        public event Action? Resized;
        public event Action? SizeSet;
        public event Action? Shown;
        public event Action? Activated;
        public event Action? HeldPressed;
        public Func<bool>? Closing { get; set; }

        public HostedForm(IWindowManager windowManager, HostedFormRequest request)
        {
            this.windowManager = windowManager;
            window = new MDIWindow
            {
                Title = request.Caption,
                Width = request.Width,
                Height = request.Height,
                [MDIHostPanel.WindowLocationProperty] = new Point(request.Left, request.Top)
            };
            // Held by a modal form, the window takes no input of its own (#822), and a press on it brings the modal form
            // forward instead.
            foreach (var input in new RoutedEvent[] { MDIWindow.KeyDownEvent, MDIWindow.KeyUpEvent, MDIWindow.TextInputEvent, MDIWindow.PointerReleasedEvent, MDIWindow.PointerWheelChangedEvent })
                window.AddHandler(input, new EventHandler<RoutedEventArgs>((_, e) => e.Handled |= IsHeld), RoutingStrategies.Tunnel);
            window.AddHandler(MDIWindow.PointerPressedEvent, (_, e) =>
            {
                if (!IsHeld)
                    return;
                e.Handled = true;
                HeldPressed?.Invoke();
            }, RoutingStrategies.Tunnel);
            // Report layout and setters through the same extent comparison (#994, #1570). A size setter reports
            // immediately, even during Load; the later layout finds nothing new.
            window.GetObservable(MDIWindow.BoundsProperty).Subscribe(new ActionObserver<Rect>(_ =>
            {
                sizePending = false;
                Resized?.Invoke();
            }));
            window.GetObservable(MDIWindow.WidthProperty).Subscribe(new ActionObserver<double>(_ => SizeSetNotification()));
            window.GetObservable(MDIWindow.HeightProperty).Subscribe(new ActionObserver<double>(_ => SizeSetNotification()));
            window.GetObservable(MDIHostPanel.WindowStateProperty).Subscribe(new ActionObserver<WindowState>(_ => Resized?.Invoke()));
            window.GetObservable(MDIWindow.IsVisibleProperty).Subscribe(new ActionObserver<bool>(visible =>
            {
                if (visible)
                    Shown?.Invoke();
            }));
            // The host's active window is its topmost, which stays so while the IDE itself is inactive, as a form stays the
            // program's active one while another application is. A held form is never active (#994).
            window.GetObservable(MDIHostPanel.IsActiveProperty).Subscribe(new ActionObserver<bool>(active =>
            {
                if (active && !IsHeld)
                    Activated?.Invoke();
            }));
        }

        private bool sizePending;
        private void SizeSetNotification()
        {
            sizePending = true;
            SizeSet?.Invoke();
        }

        public double Width { get => window.Width; set => window.Width = value; }
        public double Height { get => window.Height; set => window.Height = value; }
        public (double Width, double Height, HostedFormState State) Extent
        {
            get
            {
                var state = MDIHostPanel.GetWindowState(window);
                if (state == WindowState.Minimized)
                    return (0, 0, HostedFormState.Minimized);
                var size = sizePending || window.Bounds.Size == default ? new Size(Width, Height) : window.Bounds.Size;
                var scaling = TopLevel.GetTopLevel(window)?.RenderScaling ?? 1;
                return (Math.Round(size.Width * scaling), Math.Round(size.Height * scaling), (HostedFormState)state);
            }
        }

        public Task Show(bool blockBehind)
        {
            window.IsVisible = true;
            if (inHost is not null)
                return inHost;
            inHost = blockBehind ? windowManager.ShowManagedWindow(window) : windowManager.ShowManagedWindow(window, blockBehind: false);
            // The host's own close takes the window away. The title bar's close asks the program first, as a desktop form's
            // close box does, QueryUnload with UnloadMode 0 and then Unload (#973), and is refused while a modal form holds
            // this one (#822). The window goes once the form has unloaded, through the host's close.
            hostClose = window.CloseCommand;
            window.CloseCommand = new DelegateCommand(() =>
            {
                if (Closing?.Invoke() != true)
                    hostClose?.Execute(null);
            }, () => true);
            return inHost;
        }

        public void SetContent(object content) => window.Content = content;
        public void Hide() => window.IsVisible = false;
        public void Close() => (hostClose ?? window.CloseCommand)?.Execute(null);
        public void Activate() => window.ActivateMDIForm();
        public bool IsShown => inHost is { IsCompleted: false } && window.IsVisible;
        private bool held;
        public bool IsHeld
        {
            get => held;
            set
            {
                held = value;
                // Its controls stop taking hits, which disabling them would do too, greying each one (#822).
                if (window.Content is Avalonia.Input.InputElement content)
                    content.IsHitTestVisible = !value;
            }
        }
    }

    public Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxLayout layout) =>
        windowManager.MessageBox(text, caption, buttons, icon, isProgramDialog: true, layout: layout);

    public Task<string?> InputBox(string prompt, string? title, string defaultText) =>
        windowManager.InputBox(prompt, title, defaultText, isProgramDialog: true);

    public Task<bool> PrintDialog() => windowManager.ShowPrintDialog();

    public Task<int?> ColorDialog(int initial) => windowManager.ShowColorDialog(initial);

    public string StartupFormFailedToLoad(string formName, string description) =>
        string.Format(localization.GetString("Str.ProjectRunner.StartupFormFailedToLoad"), formName, description);

    public string NotImplementedYet => localization.GetString("Str.ProjectRunner.NotImplementedYet");

    public async Task<VbFileDialogAnswer?> FileDialog(VbFileDialogRequest request)
    {
#if DEBUG
        // An armed answer stands for a name typed with the filter left as the program set it.
        int? scriptedIndex = HexIDE.IDE.ScriptedFileDialogs.Pending > 0 && request.FilterIndex >= 1 ? request.FilterIndex : null;
#else
        int? scriptedIndex = null;
#endif
        var start = Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime { MainWindow: { } main }
                    && TopLevel.GetTopLevel(main)?.StorageProvider is { } provider
            ? await ProgramFileDialogOptions.StartFolder(provider, request)
            : null;
        if (request.Save)
        {
            var path = await windowManager.SaveFilePickerAsync(ProgramFileDialogOptions.Save(request, start));
            return path is null ? null : new VbFileDialogAnswer(path, scriptedIndex);
        }
        var paths = await windowManager.OpenFilePickerAsync(ProgramFileDialogOptions.Open(request, start));
        return paths is { Count: > 0 }
            ? new VbFileDialogAnswer(paths[0], scriptedIndex, paths.Skip(1).ToArray())
            : null;
    }

    /// <summary>A CommonDialog's ShowFont (#1084), through the IDE's font dialog.</summary>
    public async Task<VbFontChoice?> FontDialog(VbFontChoice initial)
    {
        var start = initial.Name.Length == 0
            ? null
            : new FontDialogResult(new Avalonia.Media.FontFamily(initial.Name),
                initial.Italic ? Avalonia.Media.FontStyle.Italic : Avalonia.Media.FontStyle.Normal,
                initial.Bold ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal, initial.Size);
        var chosen = await windowManager.ShowFontDialog(start, isProgramDialog: true);
        return chosen is null
            ? null
            : new VbFontChoice(chosen.Family.Name, (float)chosen.Size, chosen.Weight >= Avalonia.Media.FontWeight.Bold,
                chosen.Style != Avalonia.Media.FontStyle.Normal);
    }
}
