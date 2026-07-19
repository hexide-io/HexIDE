namespace HexIDE.Addins;

public enum AddinDocumentKind { Form, Module, UserControl, Other }

public enum AddinDiagnosticSeverity { Error = 1, Warning = 2, Information = 3, Hint = 4 }

public record AddinDocument(string FileName, string FilePath, string Content, AddinDocumentKind Kind);

public record AddinSelection(
    string FileName,
    string SelectedText,
    int StartLine, int StartColumn,
    int EndLine, int EndColumn);

/// <summary>A text replacement. All positions are 1-based.</summary>
public record AddinTextEdit(
    int StartLine, int StartColumn,
    int EndLine, int EndColumn,
    string NewText);

public record AddinFileInfo(string FileName, string FilePath, AddinDocumentKind Kind);

public record AddinProjectInfo(string ProjectName, string ProjectPath, IReadOnlyList<AddinFileInfo> Files);

public record AddinDiagnostic(
    string FileName,
    int Line, int Column,
    string Message,
    AddinDiagnosticSeverity Severity);
