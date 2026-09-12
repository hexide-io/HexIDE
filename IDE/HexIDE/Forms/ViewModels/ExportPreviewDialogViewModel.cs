using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HexIDE.Conversations;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Forms.ViewModels;

/// <summary>
/// The last look before a conversation leaves the machine.
/// </summary>
/// <remarks>
/// <b>Every outbound gate in this tree shows the payload first, and this is the one that matters most.</b>
/// A preview is the only thing that catches a secret sitting in a string literal — a connection string in a
/// constant, a token in a comment, a customer's name in a form caption. No content-agnostic redactor will
/// ever find those, and the person who can is looking at the screen.
///
/// <para>
/// <b>The disclosure is sized in terms a person can weigh.</b> "Ten messages" is a number about the
/// protocol; "forty-seven copies of Form1, two megabytes of your source" is a number about them. Full
/// document synchronisation sends the whole file on every keystroke burst, so the copy count is usually the
/// surprise, and it is the one that decides whether somebody attaches the file.
/// </para>
///
/// <para>
/// <b>It shows what is actually going, not a description of it.</b> The preview text is the exact content
/// of the messages file, already pseudonymised — so what is read here is what lands on disk, and the
/// pseudonyms are visible rather than promised.
/// </para>
/// </remarks>
public partial class ExportPreviewDialogViewModel : ObservableObject, IDialog
{
    /// <summary>
    /// How much of the messages file the preview holds.
    /// </summary>
    /// <remarks>
    /// A capture runs to megabytes, and a dialog that tried to render all of it would be the reason the
    /// preview gets dismissed unread. Cut at a size a person will actually scroll, and say so — a preview
    /// that quietly stops is the same defect as a listing that quietly stops.
    /// </remarks>
    public const int PreviewCharacters = 64 * 1024;

    private readonly ILocalizationService _localization;

    public string Title => _localization.GetString("Str.Dialog.ExportPreview.Vm.Title");
    public bool CanResize => true;
    public event Action<bool>? CloseRequested;

    public ExportPreviewDialogViewModel(
        ConversationDisclosure disclosure,
        ConversationExport export,
        ILocalizationService localization)
    {
        _localization = localization;

        Headline = Text("Str.Dialog.ExportPreview.Headline", disclosure.Messages);

        CarriesDocuments = disclosure.CarriesDocuments;
        Documents = disclosure.Documents.Count == 0
            ? ""
            : Text("Str.Dialog.ExportPreview.Documents",
                   ConversationDisclosure.Bytes(disclosure.DocumentBytes), disclosure.DocumentSummary());

        HasUnattributed = disclosure.UnattributedMessages > 0;
        Unattributed = Text("Str.Dialog.ExportPreview.Unattributed", disclosure.UnattributedMessages);

        HasBodilessMessages = disclosure.MessagesWithNoBody > 0;
        BodilessMessages = Text(
            "Str.Dialog.ExportPreview.NoBody", disclosure.MessagesWithNoBody, disclosure.Messages);

        // The redactor's own count rather than the paths a reader can see: the same value appearing five
        // times is one disclosure, not five, and the pseudonym table is the thing that knows that. It comes
        // off the export, which is where the manifest gets it too, so the dialog and the file cannot
        // disagree about a number a decision is made on.
        Pseudonyms = Text("Str.Dialog.ExportPreview.Pseudonyms", export.NamesReplaced);
        IsPseudonymised = export.Pseudonymised;

        Preview = export.Messages.Length > PreviewCharacters
            ? export.Messages[..PreviewCharacters]
            : export.Messages;

        IsPreviewTruncated = export.Messages.Length > PreviewCharacters;
        PreviewNote = Text(
            "Str.Dialog.ExportPreview.Truncated",
            ConversationDisclosure.Bytes(PreviewCharacters),
            ConversationDisclosure.Bytes(export.Messages.Length));
    }

    public string Headline { get; }
    public bool CarriesDocuments { get; }
    public string Documents { get; }
    public bool HasUnattributed { get; }
    public string Unattributed { get; }
    public bool HasBodilessMessages { get; }
    public string BodilessMessages { get; }
    public string Pseudonyms { get; }

    /// <summary>
    /// Whether anything was replaced at all.
    /// </summary>
    /// <remarks>
    /// The dialog reads very differently in the two cases, and a non-pseudonymising export is the one that
    /// most needs a person to look before it leaves — so it says so where it cannot be missed rather than
    /// leaving the absence of a reassurance to be noticed.
    /// </remarks>
    public bool IsPseudonymised { get; }
    public string Preview { get; }
    public bool IsPreviewTruncated { get; }
    public string PreviewNote { get; }

    private string Text(string key, params object?[] arguments) =>
        _localization.GetString(key) is { Length: > 0 } format
            ? string.Format(format, arguments)
            : string.Join(" ", arguments);

    [RelayCommand] private void Save() => CloseRequested?.Invoke(true);
    [RelayCommand] private void Cancel() => CloseRequested?.Invoke(false);
}
