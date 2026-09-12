using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AvaloniaEdit;
using AvaloniaEdit.Folding;
using HexIDE.Controls;
using HexIDE.Themes;

namespace HexIDE.Tools.ProtocolInspector;

public partial class ProtocolInspectorToolView : UserControl
{
    private FoldingManager? _folding;

    public ProtocolInspectorToolView()
    {
        InitializeComponent();

        if (this.FindControl<TextEditor>("Body") is not { } editor) return;

        // The same definition the export preview uses, so a colour means the same thing in both. Null is
        // tolerated: colouring is a nicety and this pane's job is to show what crossed the wire.
        editor.SyntaxHighlighting = ProtocolHighlighting.Definition;

        // FOLDING, because without it people copy the frame out to something that has it. A pretty-printed
        // initialize result is forty lines of capability tree and the question is almost always about one
        // branch of it.
        _folding = FoldingManager.Install(editor.TextArea);
        editor.TextChanged += (_, _) => Refold(editor);
        Refold(editor);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Recomputes the fold regions for whatever the pane is now showing.
    /// </summary>
    /// <remarks>
    /// Wholesale on every change rather than incrementally: the content is one message, replaced entirely
    /// when a row is selected or the preview is toggled, so there is no edit to track. Everything opens
    /// again with it, which is right — the folds belonged to the previous message.
    /// </remarks>
    private void Refold(TextEditor editor)
    {
        if (_folding is null) return;

        // -1 as the error offset, because this scan does not parse and so has no position to report. A
        // body that fails to parse still folds as far as its brackets close, deliberately: that body is
        // the one somebody most wants to read.
        _folding.UpdateFoldings(JsonFolding.Foldings(editor.Text ?? ""), -1);
    }

    /// <summary>
    /// Puts the selected message on the clipboard in the borrowed trace shape.
    /// </summary>
    /// <remarks>
    /// Code-behind rather than a command because the clipboard hangs off the <see cref="TopLevel"/>, which
    /// a view model has no business reaching for — the same shape the Object Browser's copy already uses.
    /// The <em>text</em> is composed in the view model, so what gets copied is asserted on by a test and
    /// readable by an automation client, neither of which can read a clipboard.
    /// </remarks>
    private async void Copy_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProtocolInspectorToolViewModel vm) return;
        if (vm.SelectedTrace is not { Length: > 0 } trace) return;

        await (TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(trace) ?? Task.CompletedTask);
    }
}
