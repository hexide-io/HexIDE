using Avalonia;
using AvaloniaEdit;

namespace HexIDE.Controls;

/// <summary>
/// Binds a plain string into a read-only <see cref="TextEditor"/>.
/// </summary>
/// <remarks>
/// <b>The editor binds a document, and a view model that has no business owning one.</b> AvaloniaEdit is
/// built around a mutable <c>TextDocument</c> because its usual job is editing; a viewer's content is just
/// a value, and giving a view model a document to keep in step would put an editing model behind something
/// nobody edits — and would put an AvaloniaEdit type in every test that asserts on the text.
///
/// <para>
/// So the string stays the property, and this carries it across. Scrolled back to the top on every change,
/// because the content is a different message each time and a viewer left halfway down the previous one
/// reads as a rendering fault.
/// </para>
/// </remarks>
public static class EditorText
{
    public static readonly AttachedProperty<string?> ValueProperty =
        AvaloniaProperty.RegisterAttached<TextEditor, string?>("Value", typeof(EditorText));

    static EditorText() => ValueProperty.Changed.AddClassHandler<TextEditor>(OnValueChanged);

    public static string? GetValue(TextEditor editor) => editor.GetValue(ValueProperty);

    public static void SetValue(TextEditor editor, string? value) => editor.SetValue(ValueProperty, value);

    private static void OnValueChanged(TextEditor editor, AvaloniaPropertyChangedEventArgs e)
    {
        var text = e.GetNewValue<string?>() ?? "";

        // Guarded, because assigning Text rebuilds the document and would discard a selection the reader
        // is in the middle of making every time anything else on the view model changes.
        if (editor.Text == text) return;

        editor.Text = text;
        editor.CaretOffset = 0;
        editor.ScrollToHome();
    }
}
