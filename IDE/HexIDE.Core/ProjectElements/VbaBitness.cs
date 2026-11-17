namespace HexIDE.Runtime.ProjectElements;

/// <summary>
/// A project's VBA7 target (#4245): None for a VB6 project, or the bitness of the VBA7 host its code is written for.
/// Stored as <c>VbaBitness=</c> in the project file's trailing <c>[HexIDE]</c> section.
/// </summary>
public enum VbaBitness
{
    None = 0,
    Bit32 = 32,
    Bit64 = 64,
}
