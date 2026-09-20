namespace HexIDE.Addins;

public enum AddinDocumentKind { Form, Module, UserControl, Other }

public enum AddinDiagnosticSeverity { Error = 1, Warning = 2, Information = 3, Hint = 4 }

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
public record AddinSelection(
    string FileName,
    string SelectedText,
    int StartLine, int StartColumn,
    int EndLine, int EndColumn,
    string? Project = null);

/// <summary>A text replacement. All positions are 1-based.</summary>
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

public record AddinProjectInfo(string ProjectName, string ProjectPath, IReadOnlyList<AddinFileInfo> Files);

/// <remarks>
/// <b><c>Code</c> and <c>Source</c> are trailing and optional deliberately.</b> This type is built by the
/// IDE and read by add-ins, so adding to the end leaves every existing add-in compiling and running
/// unchanged, while an add-in that wants the rule that fired — or which of several servers reported it —
/// can now ask (hexide-io/HexIDE#426).
/// </remarks>
public record AddinDiagnostic(
    string FileName,
    int Line, int Column,
    string Message,
    AddinDiagnosticSeverity Severity,
    string? Code = null,
    string? Source = null,
    string? Project = null);
