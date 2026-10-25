namespace HexIDE.Addins;

public enum AddinDocumentKind { Form, Module, UserControl, Other }

public enum AddinDiagnosticSeverity { Error = 1, Warning = 2, Information = 3, Hint = 4 }

/// <param name="Content">
/// The document's whole file, as its code window holds it: a form's <c>VERSION</c> line and designer block, a
/// class's header and the <c>Attribute</c> lines that open the code come first, then the code. It is the text
/// every position on this surface counts in, so line N of it is line N to <see cref="AddinSelection"/>,
/// <see cref="AddinTextEdit"/>, <see cref="AddinDiagnostic"/> and <c>NavigateTo</c>. The header is the IDE's to
/// change: <c>TrySetContent</c> takes this text back with the header unchanged, or the code alone, and refuses a
/// header that differs. A member's <c>Attribute</c> lines, the run under its declaration, are the IDE's too:
/// <c>TryApplyEdits</c> refuses edits to them and sets of edits that add, extend or orphan a described run.
/// An empty range edit deleting a declaration's text also removes its Attribute run.
/// <c>TrySetContent</c> refuses a replacement that changes, adds or drops them, other than by moving or renaming them with their member or deleting them with its declaration,
/// and names the member (hexide-io/HexIDE#273 task 4.5b). Before hexide-io/HexIDE#273 this was the code alone,
/// starting at the form's <c>Attribute</c> lines or after the class's header. A form created in this session and
/// not yet saved or changed in the designer has no header yet, so for it this is still the code alone.
/// </param>
/// <param name="Project">
/// The project holding this document, by name. Trailing and optional so an add-in built against an earlier
/// version keeps compiling, on the rule this file already records for a diagnostic's Code and Source: the
/// IDE builds these records and add-ins read them, so nothing outside the IDE constructs one positionally.
/// </param>
public record AddinDocument(string FileName, string FilePath, string Content, AddinDocumentKind Kind,
    string? Project = null);

/// <param name="Project">
/// The project holding this document, by name. Trailing and optional so an add-in built against an earlier
/// version keeps compiling, on the rule this file already records for a diagnostic's Code and Source: the
/// IDE builds these records and add-ins read them, so nothing outside the IDE constructs one positionally.
/// </param>
/// <remarks>
/// Positions are 1-based, and lines count from the top of the file with its header included (a form's designer
/// block, a class's header and its Attribute lines), as the code window numbers them.
/// </remarks>
public record AddinSelection(
    string FileName,
    string SelectedText,
    int StartLine, int StartColumn,
    int EndLine, int EndColumn,
    string? Project = null);

/// <summary>
/// A text replacement. All positions are 1-based, and lines count from the top of the file with its header
/// included, as the code window numbers them.
/// </summary>
public record AddinTextEdit(
    int StartLine, int StartColumn,
    int EndLine, int EndColumn,
    string NewText);

/// <param name="Project">
/// The project holding this document, by name. Trailing and optional so an add-in built against an earlier
/// version keeps compiling, on the rule this file already records for a diagnostic's Code and Source: the
/// IDE builds these records and add-ins read them, so nothing outside the IDE constructs one positionally.
/// </param>
public record AddinFileInfo(string FileName, string FilePath, AddinDocumentKind Kind, string? Project = null);

/// <summary>
/// What a call that changes a document, or moves to a place in one, came to: built by the IDE, read by add-ins.
/// </summary>
/// <param name="Succeeded">True when the call did what it asked.</param>
/// <param name="Reason">
/// Why it did not, in words a person or a model can act on, and null when it succeeded. Written in English
/// whatever language the IDE shows, as automation replies are, because an add-in may pass it to a model or log
/// it as well as show it.
/// </param>
/// <param name="HeaderKept">
/// True when a replacement carried none of the document's header, or only part of it, so the document's own
/// header was put in front of it. False for every other call.
/// </param>
/// <remarks>
/// Trailing fields may be added, optional, on the rule the records above follow: the IDE builds this and add-ins
/// read it, so nothing outside the IDE constructs one positionally.
/// </remarks>
public record AddinEditResult(bool Succeeded, string? Reason = null, bool HeaderKept = false)
{
    /// <summary>
    /// The refusal a host gives when it implements only the members that answer true or false, and so cannot
    /// say why.
    /// </summary>
    public static AddinEditResult WithoutReason { get; } =
        new(false, "Refused. This host answers only whether a call succeeded, so it gives no reason.");
}

/// <summary>
/// The diagnostics held for one document, or why the name given did not find one document: built by the IDE,
/// read by add-ins.
/// </summary>
/// <param name="Diagnostics">
/// The document's diagnostics, which may be none. Always empty when the call was refused.
/// </param>
/// <param name="Reason">
/// Why the name did not find one document, in words a person or a model can act on, and null when it did. Written
/// in English whatever language the IDE shows, as <see cref="AddinEditResult.Reason"/> is.
/// </param>
/// <remarks>
/// An empty list with no reason is a document the IDE holds no diagnostics for. That is the distinction this
/// record exists to make: the list alone cannot tell a clean file from a name that found nothing.
/// Trailing fields may be added, optional, on the rule the records above follow.
/// </remarks>
public record AddinDiagnosticsResult(IReadOnlyList<AddinDiagnostic> Diagnostics, string? Reason = null)
{
    /// <summary>True when the name did not find one document, which <see cref="Reason"/> then explains.</summary>
    public bool Refused => Reason is not null;
}

public record AddinProjectInfo(string ProjectName, string ProjectPath, IReadOnlyList<AddinFileInfo> Files);

/// <remarks>
/// <b><c>Code</c> and <c>Source</c> are trailing and optional deliberately.</b> This type is built by the
/// IDE and read by add-ins, so adding to the end leaves every existing add-in compiling and running
/// unchanged, while an add-in that wants the rule that fired — or which of several servers reported it —
/// can now ask (hexide-io/HexIDE#426).
/// <para>
/// <c>Line</c> and <c>Column</c> are 1-based, and the line counts from the top of the file with its header
/// included, as the code window numbers it.
/// </para>
/// </remarks>
public record AddinDiagnostic(
    string FileName,
    int Line, int Column,
    string Message,
    AddinDiagnosticSeverity Severity,
    string? Code = null,
    string? Source = null,
    string? Project = null);
