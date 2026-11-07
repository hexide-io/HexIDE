using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.RichText;

namespace HexIDE.Runtime.BuiltinControls;

public sealed partial class VBRichTextBox
{
    internal bool InputLocked { get; set; }
    internal int InputMaxLength { get; set; }

    internal int Appearance { get; init; } = 1;
    internal int BorderStyle { get; init; } = 1;
    internal VBColor SavedBackColor { get; init; } = VBColor.FromSystemColor(VbSystemColor.Window);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsEffectivelyEnabledProperty) InvalidateVisual();
    }

    public VBRichTextBox()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        InvalidateVisual();
    }

    // A refusal met while handling input reaches the program as its run-time error, through the form's event queue as
    // the TabStrip's do. Thrown out of an Avalonia input handler it would reach the UI thread and end the IDE (#643).
    private void Guarded(RoutedEventArgs e, Action input)
    {
        try { input(); }
        catch (NotImplementedException refusal)
        {
            e.Handled = true;
            Refuse(refusal);
        }
    }

    private void Refuse(NotImplementedException refusal)
    {
        if (this.FindLogicalAncestorOfType<IFormView>() is { } form)
            form.Context.ExecuteControlInput($"{VBProps.GetName(this)}_Input", () => Task.FromException(refusal));
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Handled || e.Text is not { Length: > 0 } text) return;
        Guarded(e, () => TypeInput(text));
        e.Handled = true;
    }

    private void TypeInput(string text)
    {
        if (text.Any(c => c > 127))
            throw new NotImplementedException("RichTextBox non-ASCII KeyPress conversion is not implemented (#1100)");
        foreach (var character in text)
        {
            RaiseKey(ComponentBaseClass.KeyPressEvent, character);
            InsertFromInput(character.ToString());
        }
    }

    private bool CanEdit => !InputLocked && !SelectedRuns.Any(r => r.Format.Protected);

    private void InsertFromInput(string text, bool remember = true)
    {
        if (!CanEdit) return;
        if (InputMaxLength > 0 && Document.Text.Length - Math.Min(Length, Document.Text.Length - Start) + text.Length > InputMaxLength)
            return;
        if (remember) RememberEdit();
        ReplaceSelection(RtfDocument.FromText(text), true, fromInput: true);
        lastInputDocument = Document;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        Guarded(e, () => KeyDownInput(e));
    }

    private void KeyDownInput(KeyEventArgs e)
    {
        if (KeyCode(e.Key) is { } code) RaiseKey(ComponentBaseClass.KeyDownEvent, code, e.KeyModifiers);
        if (Navigate(e) || Shortcut(e))
        {
            e.Handled = true;
            return;
        }
        switch (e.Key)
        {
            case Key.Enter when MultiLine:
                RaiseKey(ComponentBaseClass.KeyPressEvent, 13);
                InsertFromInput("\r\n");
                e.Handled = true;
                break;
            case Key.Back:
                if (CanEdit && (Length > 0 || Start > 0))
                {
                    if (Length == 0)
                    {
                        var count = Start >= 2 && Document.Text.AsSpan(Start - 2, 2).SequenceEqual("\r\n") ? 2 : 1;
                        if (Document.Slice(Start - count, count).Any(r => r.Format.Protected))
                        {
                            RaiseKey(ComponentBaseClass.KeyPressEvent, 8);
                            e.Handled = true;
                            return;
                        }
                        // The deletion has one final SelChange, not a preliminary selection event.
                        RememberEdit();
                        Start -= count;
                        Length = count;
                    }
                    else RememberEdit();
                    InsertFromInput("", false);
                }
                RaiseKey(ComponentBaseClass.KeyPressEvent, 8);
                e.Handled = true;
                break;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (KeyCode(e.Key) is { } code) RaiseKey(ComponentBaseClass.KeyUpEvent, code, e.KeyModifiers);
    }

    // Values recorded in VBRUN's KeyCodeConstants; no host enum ordinals cross into VB6.
    private static int? KeyCode(Key key) => key switch
    {
        >= Key.A and <= Key.Z => 65 + key - Key.A,
        >= Key.D0 and <= Key.D9 => 48 + key - Key.D0,
        Key.Back => 8, Key.Tab => 9, Key.Enter => 13, Key.Space => 32,
        _ => null
    };

    private void RaiseKey(EventClass eventClass, int code, KeyModifiers modifiers = KeyModifiers.None)
    {
        foreach (var handler in ControlEvents.Handlers(this, this.FindLogicalAncestorOfType<IModuleExecutionRoot>(), eventClass.Name))
        {
            var args = new List<Vb6Value>();
            if (handler.Own && VBProps.GetIndex(this) is { } index) args.Add(new Vb6Value(index));
            args.Add(new Vb6Value((short)code));
            if (!ReferenceEquals(eventClass, ComponentBaseClass.KeyPressEvent))
                args.Add(new Vb6Value((short)((modifiers.HasFlag(KeyModifiers.Shift) ? 1 : 0)
                    | (modifiers.HasFlag(KeyModifiers.Control) ? 2 : 0) | (modifiers.HasFlag(KeyModifiers.Alt) ? 4 : 0))));
            handler.Root.ExecuteSub(handler.Name, args);
        }
    }

    private TextLayout CreateLayout()
    {
        var spans = new List<ValueSpan<TextRunProperties>>();
        var offset = 0;
        foreach (var run in Document.Runs)
        {
            var format = run.Format;
            var decorations = new TextDecorationCollection();
            if (format.Underline) decorations.AddRange(TextDecorations.Underline);
            if (format.StrikeThrough) decorations.AddRange(TextDecorations.Strikethrough);
            var color = format.Color;
            TextRunProperties Properties(bool selected) => new GenericTextRunProperties(
                new Typeface(new FontFamily(format.FontName), format.Italic ? FontStyle.Italic : FontStyle.Normal,
                    format.Bold ? FontWeight.Bold : FontWeight.Normal),
                format.PointSize * 96 / 72, decorations,
                !IsEffectivelyEnabled ? VBColor.FromSystemColor(VbSystemColor.Graytext).ToBrush()
                    : selected ? Brushes.White : new SolidColorBrush(Color.FromRgb((byte)color, (byte)(color >> 8), (byte)(color >> 16))),
                selected && IsEffectivelyEnabled ? Brushes.Black : null);
            var end = offset + run.Text.Length;
            var selectedFrom = Math.Clamp(Start, offset, end);
            var selectedTo = Math.Clamp(Start + Length, selectedFrom, end);
            if (!IsFocused) selectedTo = selectedFrom;
            if (selectedFrom > offset) spans.Add(new(offset, selectedFrom - offset, Properties(false)));
            if (selectedTo > selectedFrom) spans.Add(new(selectedFrom, selectedTo - selectedFrom, Properties(true)));
            if (end > selectedTo) spans.Add(new(selectedTo, end - selectedTo, Properties(false)));
            offset = end;
        }
        return new TextLayout(Document.Text, new Typeface("MS Sans Serif"), 11, Brushes.Black,
            textWrapping: MultiLine ? TextWrapping.Wrap : TextWrapping.NoWrap,
            maxWidth: Math.Max(1, Bounds.Width - 4), textStyleOverrides: spans, textRunCache: null);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle((IsEffectivelyEnabled ? SavedBackColor
            : VBColor.FromSystemColor(VbSystemColor.Btnface)).ToBrush(), new Rect(Bounds.Size));
        using var layout = CreateLayout();
        layout.Draw(context, new Point(2, 2) - scrollOffset);
        if (IsEffectivelyEnabled && IsFocused && Length == 0)
        {
            var caret = layout.HitTestTextPosition(Start);
            context.DrawLine(new Pen(Brushes.Black), new Point(caret.X + 2, caret.Y + 2) - scrollOffset,
                new Point(caret.X + 2, caret.Bottom + 2) - scrollOffset);
        }
        DrawBorder(context);
    }

    private void DrawBorder(DrawingContext context)
    {
        if (BorderStyle == 0 || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        IBrush Brush(VbSystemColor color) => VBColor.FromSystemColor(color).ToBrush();
        if (Appearance == 0)
        {
            context.DrawRectangle(null, new Pen(Brush(VbSystemColor.Windowframe)),
                new Rect(0.5, 0.5, Math.Max(0, Bounds.Width - 1), Math.Max(0, Bounds.Height - 1)));
            return;
        }
        void Edge(double inset, IBrush top, IBrush bottom)
        {
            var width = Math.Max(0, Bounds.Width - 2 * inset);
            var height = Math.Max(0, Bounds.Height - 2 * inset);
            context.FillRectangle(top, new Rect(inset, inset, width, Math.Min(1, height)));
            context.FillRectangle(top, new Rect(inset, inset, Math.Min(1, width), height));
            context.FillRectangle(bottom, new Rect(inset, Bounds.Height - inset - 1, width, 1));
            context.FillRectangle(bottom, new Rect(Bounds.Width - inset - 1, inset, 1, height));
        }
        Edge(0, Brush(VbSystemColor.Btnshadow), Brush(VbSystemColor.Btnhighlight));
        if (Bounds.Width >= 4 && Bounds.Height >= 4)
            Edge(1, Brush(VbSystemColor._3DDKSHADOW), Brush(VbSystemColor._3DLIGHT));
    }
}
