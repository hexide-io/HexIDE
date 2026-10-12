using System;
using System.Collections.Generic;
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

    public event Action<MessageBoxResult>? AcceptRequest;

    /// <summary>
    /// What closing a box with these buttons answers when no button was pressed, or null where it has no close.
    /// </summary>
    /// <remarks>
    /// Measured on vb6.exe, posting WM_CLOSE and SC_CLOSE to each kind: OK alone answers vbOK; OK/Cancel, Yes/No/Cancel
    /// and Retry/Cancel answer vbCancel; Yes/No and Abort/Retry/Ignore stay open, because they have no close. That is
    /// Windows' own message box, and the same holds for every box the IDE shows.
    /// </remarks>
    public static MessageBoxResult? AnswerOnClose(MessageBoxButtons buttons) => buttons switch
    {
        MessageBoxButtons.Ok => MessageBoxResult.Ok,
        MessageBoxButtons.OkCancel or MessageBoxButtons.YesNoCancel or MessageBoxButtons.RetryCancel
            => MessageBoxResult.Cancel,
        _ => null,
    };

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
            Content = this,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        current = again;
        window.Close();
        var closed = Watch(again);
        void OnOwnerClosed(object? sender, EventArgs e) => again.Close();
        owner.Closed += OnOwnerClosed;
        again.Show();
        await closed;
        owner.Closed -= OnOwnerClosed;
        return Answer;
    }

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
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Built once: a box hidden with its owner moves to a window of its own, and is loaded again there.
        if (PART_Buttons.Children.Count > 0)
            return;
        if (Icon != MessageBoxIcon.None)
            PART_Content.Children.Add(BuildIconElement());

        PART_Content.Children.Add(new TextBlock
        {
            Text = Text,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 360,
            VerticalAlignment = VerticalAlignment.Center
        });

        foreach (var (label, result) in GetButtonDefs())
        {
            var captured = result;
            var btn = new Button { Content = label, MinWidth = 75 };
            btn.Click += (_, _) => AcceptRequest?.Invoke(captured);
            PART_Buttons.Children.Add(btn);
        }
    }

    private Control BuildIconElement()
    {
        var (symbol, bg) = Icon switch
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
