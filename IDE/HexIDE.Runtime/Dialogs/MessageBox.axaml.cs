using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using HexIDE.IDE;

namespace HexIDE.Runtime.Dialogs;

public partial class MessageBox : UserControl
{
    public string Text { get; set; } = "";
    public MessageBoxButtons Buttons { get; set; } = MessageBoxButtons.Ok;
    public MessageBoxIcon Icon { get; set; } = MessageBoxIcon.None;
    public MessageBoxLayout Layout { get; set; }

    public event Action<MessageBoxResult>? AcceptRequest;

    public static MessageBoxResult? AnswerOnClose(MessageBoxButtons buttons) =>
        MessageBoxClosePolicy.AnswerOnClose(buttons);

    /// <summary>
    /// Whether the window hosting a box should refuse a close, as Windows refuses one for a box with no close.
    /// </summary>
    /// <remarks>
    /// Only a person's close is refused: the title-bar button or Alt+F4. A close HexIDE makes itself still goes
    /// through, which is how ending the run or shutting down reaches a box that is still open, and so does one
    /// forced by the owner closing or the session ending.
    /// </remarks>
    public static bool RefusesClose(MessageBoxButtons buttons, bool isProgrammatic, WindowCloseReason reason) =>
        AnswerOnClose(buttons) is null && !isProgrammatic && reason == WindowCloseReason.WindowClosing;

    /// <summary>
    /// The box's answer once its window has closed: the button pressed, or what a person's close answers. None when
    /// HexIDE closed it, or its owner did, without one.
    /// </summary>
    public MessageBoxResult Answer { get; private set; } = MessageBoxResult.None;

    /// <summary>
    /// Shows the box in <paramref name="window"/>, modal over <paramref name="owner"/>, and waits for its answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A button answers, and closes the window. A person's close (the title-bar button or Alt+F4) answers as
    /// <see cref="AnswerOnClose"/> says, or is refused where the box has no close.
    /// </para>
    /// <para>
    /// Hiding the owner, as <c>Me.Visible = False</c> does, hides the box too, and Avalonia then completes the
    /// dialog with no close at all. VB6 keeps the box waiting, hidden (measured: it still exists, and pressing its
    /// button afterwards returns that button). A box nobody can see cannot be answered, so it is shown again here,
    /// unowned, and still waits; it is closed, unanswered, if its owner closes. The program's statement used to
    /// carry on as though the box had been closed: Cancel answered for OK/Cancel, and a Yes/No handler stopped.
    /// </para>
    /// </remarks>
    public async Task<MessageBoxResult> ShowAndWait(Window window, Window owner)
    {
        MessageBoxFormHold.EnterNative();
        window.FlowDirection = (Layout & MessageBoxLayout.RtlReading) != 0
            ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        ApplyWindowStyles(window);
        ProgramDialogChrome.Apply(window);
        var current = window;
        AcceptRequest += r =>
        {
            Answer = r;
            current.Close();
        };

        if (owner.IsVisible)
        {
            var shown = Watch(window);
            await window.ShowDialog(owner);
            if (shown.IsCompleted)
                return Answer;
            // Hidden with its owner: Avalonia completed the dialog without closing it.
        }

        // The owner is hidden, before the box opened or while it waited. VB6 keeps the box waiting, out of sight
        // (measured both ways). A box nobody can see could never be answered, and Avalonia keeps an owned window hidden
        // while its owner is, and refuses to show a dialog over a hidden owner at all. So the box moves to a window of
        // its own, and the one it was given is closed.
        window.Content = null;
        var again = new Window
        {
            Title = window.Title,
            FlowDirection = window.FlowDirection,
            Content = this,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        ApplyWindowStyles(again);
        ProgramDialogChrome.Apply(again);
        current = again;
        window.Close();
        MessageBoxFormHold.RefreshNative(owner);
        var closed = Watch(again);
        void OnOwnerClosed(object? sender, EventArgs e) => again.Close();
        owner.Closed += OnOwnerClosed;
        again.Show();
        await closed;
        owner.Closed -= OnOwnerClosed;
        return Answer;
    }

    private void ApplyWindowStyles(Window window)
    {
        var callback = WindowStylesCallback(OperatingSystem.IsWindows());
        if (callback is not null)
        {
            Win32Properties.AddWindowStylesCallback(window, callback);
            Win32Properties.AddWndProcHookCallback(window, MouseHookCallback(OperatingSystem.IsWindows())!);
        }
    }

    internal Win32Properties.CustomWindowStylesCallback? WindowStylesCallback(bool isWindows)
    {
        // VB6's box has WS_EX_LAYOUTRTL with vbMsgBoxRtlReading, which mirrors the native title bar (measured, #3958).
        // The content keeps Avalonia's own right-to-left layout, and MouseHookCallback un-mirrors the mouse (#3973).
        if (!isWindows || (Layout & MessageBoxLayout.RtlReading) == 0)
            return null;
        return (style, exStyle) => (style, exStyle | 0x00400000u); // WS_EX_LAYOUTRTL
    }

    internal Win32Properties.CustomWndProcHookCallback? MouseHookCallback(bool isWindows,
        Func<IntPtr, int>? clientWidth = null, Func<IntPtr, uint, IntPtr, IntPtr, IntPtr>? send = null)
    {
        if (WindowStylesCallback(isWindows) is null)
            return null;
        clientWidth ??= hwnd => GetClientRect(hwnd, out var rect) ? rect.Right - rect.Left : 0;
        send ??= SendMessage;
        var resending = false;
        return (IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            // Wheel, WM_CONTEXTMENU and WM_NCHITTEST use screen coordinates; leave them and the native frame alone.
            if (resending || !(msg is >= 0x0200 and <= 0x0209 or >= 0x020B and <= 0x020D))
                return IntPtr.Zero;
            var width = clientWidth(hwnd);
            if (width <= 0)
                return IntPtr.Zero;
            var x = (short)(lParam.ToInt64() & 0xffff);
            var mapped = new IntPtr(unchecked((int)((lParam.ToInt64() & 0xffff0000L)
                | (ushort)(width - 1 - x))));
            resending = true;
            try
            {
                handled = true;
                return send(hwnd, msg, wParam, mapped);
            }
            finally
            {
                resending = false;
            }
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ClientRect
    {
        public int Left, Top, Right, Bottom;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(IntPtr hwnd, out ClientRect rect);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Completes when <paramref name="window"/> closes, and makes a person's close of it answer as
    /// <see cref="AnswerOnClose"/> says, or be refused where the box has no close.
    /// </summary>
    private Task Watch(Window window)
    {
        var closed = new TaskCompletionSource();
        window.Closed += (_, _) => closed.TrySetResult();
        window.Closing += (_, e) =>
        {
            if (Answer != MessageBoxResult.None)
                return;
            if (RefusesClose(Buttons, e.IsProgrammatic, e.CloseReason))
                e.Cancel = true;
            else if (!e.IsProgrammatic && e.CloseReason == WindowCloseReason.WindowClosing)
                Answer = AnswerOnClose(Buttons) ?? MessageBoxResult.None;
        };
        return closed.Task;
    }

    public MessageBox()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        new KeyUpCommit(this);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Built once: a box hidden with its owner moves to a window of its own, and is loaded again there.
        if (PART_Buttons.Children.Count > 0)
            return;
        FlowDirection = (Layout & MessageBoxLayout.RtlReading) != 0
            ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        if (Icon != MessageBoxIcon.None)
            PART_Content.Children.Add(BuildIconElement(Icon));

        var prompt = new TextBlock
        {
            Text = Text,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 360,
            VerticalAlignment = VerticalAlignment.Center
        };
        // #3014 / measuring PR #3958: Right changes only the text alignment.
        prompt.TextAlignment = (Layout & MessageBoxLayout.Right) != 0
            ? TextAlignment.Right : TextAlignment.Left;
        Grid.SetColumn(prompt, Icon == MessageBoxIcon.None ? 0 : 1);
        Grid.SetColumnSpan(prompt, Icon == MessageBoxIcon.None ? 2 : 1);
        PART_Content.Children.Add(prompt);

        var defaultButton = (Layout & MessageBoxLayout.ThirdButtonDefault) != 0 ? 2
            : (Layout & MessageBoxLayout.SecondButtonDefault) != 0 ? 1 : 0;
        var definitions = new List<(string Label, MessageBoxResult Result)>(GetButtonDefs());
        if (defaultButton >= definitions.Count)
            defaultButton = 0;
        foreach (var (label, result) in definitions)
        {
            var captured = result;
            var caption = ProgramButtonCaptions.Get(result, label);
            var btn = new Button
            {
                Content = caption, MinWidth = 75,
                IsDefault = PART_Buttons.Children.Count == defaultButton,
                IsCancel = result == AnswerOnClose(Buttons)
            };
            btn.Click += (_, _) => AcceptRequest?.Invoke(captured);
            PART_Buttons.Children.Add(btn);
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(() => PART_Buttons.Children[defaultButton].Focus());
    }

    public static Control BuildIconElement(MessageBoxIcon icon)
    {
        var (symbol, bg) = icon switch
        {
            MessageBoxIcon.Error       => ("✕", new SolidColorBrush(Colors.Crimson)),
            MessageBoxIcon.Warning     => ("!", new SolidColorBrush(Color.FromRgb(220, 120, 0))),
            MessageBoxIcon.Question    => ("?", new SolidColorBrush(Colors.RoyalBlue)),
            MessageBoxIcon.Information => ("i", new SolidColorBrush(Colors.SteelBlue)),
            _                          => ("", new SolidColorBrush(Colors.Gray))
        };
        return new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = bg,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = symbol,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 16,
                FontWeight = FontWeight.Bold
            }
        };
    }

    private IEnumerable<(string Label, MessageBoxResult Result)> GetButtonDefs() => Buttons switch
    {
        MessageBoxButtons.OkCancel =>
            [("OK", MessageBoxResult.Ok), ("Cancel", MessageBoxResult.Cancel)],
        MessageBoxButtons.AbortRetryIgnore =>
            [("Abort", MessageBoxResult.Abort), ("Retry", MessageBoxResult.Retry), ("Ignore", MessageBoxResult.Ignore)],
        MessageBoxButtons.YesNoCancel =>
            [("Yes", MessageBoxResult.Yes), ("No", MessageBoxResult.No), ("Cancel", MessageBoxResult.Cancel)],
        MessageBoxButtons.YesNo =>
            [("Yes", MessageBoxResult.Yes), ("No", MessageBoxResult.No)],
        MessageBoxButtons.RetryCancel =>
            [("Retry", MessageBoxResult.Retry), ("Cancel", MessageBoxResult.Cancel)],
        _ => [("OK", MessageBoxResult.Ok)]
    };
}
