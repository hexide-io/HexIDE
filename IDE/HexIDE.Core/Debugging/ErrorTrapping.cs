namespace HexIDE.Runtime.Debugging;

/// <summary>Which handled run-time errors also halt in the debugger.</summary>
public enum ErrorTrapping { BreakOnAllErrors, BreakInClassModule, BreakOnUnhandledErrors }
