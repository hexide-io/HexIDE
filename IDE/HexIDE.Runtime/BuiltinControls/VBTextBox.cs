using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

public class VBTextBox : TextBox
{
    protected override Type StyleKeyOverride => VbBorderStyle == 0 ? typeof(TextBox) : typeof(VBTextBox);

    internal static readonly StyledProperty<VBAppearance> AppearanceProperty =
        AvaloniaProperty.Register<VBTextBox, VBAppearance>(nameof(Appearance), VBAppearance._3D);

    internal VBAppearance Appearance
    {
        get => GetValue(AppearanceProperty);
        set => SetValue(AppearanceProperty, value);
    }

    static VBTextBox()
    {
        AppearanceProperty.Changed.AddClassHandler<VBTextBox>((box, _) =>
        {
            if (box.VbBorderStyle != 0)
                box.Theme = box.Appearance == VBAppearance._3D ? null
                    : Application.Current?.FindResource("FlatVBTextBox") as ControlTheme;
        });
        TextProperty.Changed.AddClassHandler<VBTextBox>((textBox, e) =>
        {
            textBox.ExecuteSub(TextBoxComponentClass.ChangeEvent);
        });
        // Before Click, so MouseUp comes first, as VB6 raises them (#974).
        AttachedEvents.AttachMouse<VBTextBox>(AttachedEvents.DblClick.InPlaceOfMouseDown);
        AttachedEvents.AttachClick<VBTextBox>();
        // A TextBox raises GotFocus and LostFocus, as SetFocus's are measured to come (#1061).
        AttachedEvents.AttachFocusEvents<VBTextBox>();
        TextBoxKeyEvents.Attach();
    }

    public VBTextBox()
    {
        Padding = new Thickness(1);
        ApplyLayout(false, 0);
    }

    // BorderStyle 0 keeps the stock drawing: its edge has not been measured (#918).
    internal int VbBorderStyle { get; init; } = 1;

    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (e.Handled || string.IsNullOrEmpty(e.Text))
            return;
        if (System.Linq.Enumerable.Any(e.Text, character => character > 127))
        {
            base.OnTextInput(e);
            return;
        }
        e.Handled = true;
        foreach (var character in e.Text)
            TextBoxKeyEvents.Deliver(this, ComponentBaseClass.KeyPressEvent, character, null, ascii =>
            {
                if (ascii is < 0 or > 127)
                    throw new NotImplementedException("Non-ASCII KeyAscii conversion is not implemented (#2325)");
                if (ascii != 0)
                    base.OnTextInput(new TextInputEventArgs { Text = ((char)ascii).ToString() });
            });
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!e.Handled && e.KeyModifiers == KeyModifiers.None && e.Key is Key.Enter or Key.Escape)
        {
            e.Handled = true;
            TextBoxKeyEvents.Deliver(this, ComponentBaseClass.KeyPressEvent, e.Key == Key.Enter ? 13 : 27, null, ascii =>
            {
                if (ascii is < 0 or > 127)
                    throw new NotImplementedException("Non-ASCII KeyAscii conversion is not implemented (#2325)");
                if (ascii == 13 && MultiLine)
                    base.OnKeyDown(new KeyEventArgs { Key = Key.Enter, RoutedEvent = KeyDownEvent });
                else if (ascii != 0 && ascii is not (13 or 27))
                    base.OnTextInput(new TextInputEventArgs { Text = ((char)ascii).ToString() });
            });
            return;
        }
        if (!MultiLine && e.KeyModifiers == KeyModifiers.None && e.Key is Key.Home or Key.End)
        {
            // Avalonia's ClearSelection returns to the old anchor when End leaves CaretIndex unchanged (#2423).
            var position = e.Key == Key.Home ? 0 : Text?.Length ?? 0;
            SetCurrentValue(CaretIndexProperty, position);
            SetCurrentValue(SelectionStartProperty, position);
            SetCurrentValue(SelectionEndProperty, position);
            e.Handled = true;
            return;
        }
        if (!e.Handled && (e.Key == Key.Back || e.Key == Key.A && e.KeyModifiers == KeyModifiers.Control))
        {
            e.Handled = true;
            var keyAscii = e.Key == Key.Back ? e.KeyModifiers == KeyModifiers.Control ? 127 : 8 : 1;
            TextBoxKeyEvents.Deliver(this, ComponentBaseClass.KeyPressEvent, keyAscii, null, ascii =>
            {
                if (ascii is < 0 or > 127)
                    throw new NotImplementedException("Non-ASCII KeyAscii conversion is not implemented (#2325)");
                if (ascii == 0)
                    return;
                if (ascii == 8 || ascii == 1 || ascii == keyAscii)
                {
                    e.Handled = false;
                    base.OnKeyDown(e);
                }
                else
                    base.OnTextInput(new TextInputEventArgs { Text = ((char)ascii).ToString() });
            });
            return;
        }
        base.OnKeyDown(e);
    }

    private TextPresenter? textPresenter;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        textPresenter = e.NameScope.Find<TextPresenter>("PART_TextPresenter");
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var desired = base.MeasureOverride(availableSize);
        if (!MultiLine && textPresenter is not null)
        {
            // The default Windows font needs more than the 15 px a fixed inset left at 285 twips (#914).
            var inset = Math.Clamp(Math.Floor((availableSize.Height - BorderThickness.Top - BorderThickness.Bottom
                - textPresenter.TextLayout.Height) / 2), 0, 1);
            var padding = new Thickness(1, inset);
            if (Padding != padding)
            {
                Padding = padding;
                desired = base.MeasureOverride(availableSize);
            }
        }
        return desired;
    }

    /// <summary>Whether the box holds several lines: read-only at run time, as in VB6 (#1340).</summary>
    public bool MultiLine { get; private set; }

    /// <summary>The scroll bars it asks for: 0 none, 1 horizontal, 2 vertical, 3 both. Read-only at run time (#1340).</summary>
    public int ScrollBars { get; private set; }

    /// <summary>
    /// Lays the box out as its MultiLine and ScrollBars say. Measured on vb6.exe, native and p-code agreeing (the oracle's
    /// "A TextBox's MultiLine and ScrollBars"):
    /// <list type="bullet">
    /// <item>A single-line box has no scroll bars, whatever ScrollBars says, and scrolls sideways as the caret goes.</item>
    /// <item>A MultiLine box wraps unless it has a horizontal scroll bar, and scrolls down as lines are added.</item>
    /// <item>Enter breaks a MultiLine box's line with CR LF, the only line break VB6 knows, so on every host.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// VB6 draws a scroll bar it asks for even when there is nothing to scroll, so a bar here is Visible rather than Auto.
    /// Where VB6 has none, a direction the box still scrolls in is Hidden, and one it never does is Disabled, which also
    /// stops a single-line box showing part of a vertical bar (#914).
    /// </remarks>
    internal void ApplyLayout(bool multiLine, int scrollBars)
    {
        MultiLine = multiLine;
        ScrollBars = scrollBars;
        var horizontal = multiLine && scrollBars is 1 or 3;
        var vertical = multiLine && scrollBars is 2 or 3;
        AcceptsReturn = multiLine;
        NewLine = "\r\n";
        TextWrapping = multiLine && !horizontal ? TextWrapping.Wrap : TextWrapping.NoWrap;
        ScrollViewer.SetHorizontalScrollBarVisibility(this,
            horizontal ? ScrollBarVisibility.Visible : multiLine ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Hidden);
        ScrollViewer.SetVerticalScrollBarVisibility(this,
            vertical ? ScrollBarVisibility.Visible : multiLine ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Disabled);
    }
}
