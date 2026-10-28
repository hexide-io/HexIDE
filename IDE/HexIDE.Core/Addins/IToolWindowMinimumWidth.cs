namespace HexIDE.Addins;

/// <summary>A tool's usable width, measured from its content rather than imposed during resizing.</summary>
public interface IToolWindowMinimumWidth
{
    double MeasureMinimumUsableWidth();
}
