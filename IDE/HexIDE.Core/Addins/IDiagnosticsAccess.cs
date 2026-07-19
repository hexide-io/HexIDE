namespace HexIDE.Addins;

public interface IDiagnosticsAccess
{
    IReadOnlyList<AddinDiagnostic> GetAll();
    IReadOnlyList<AddinDiagnostic> GetFor(string fileName);
}
