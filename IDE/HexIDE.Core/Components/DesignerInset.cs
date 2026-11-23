namespace HexIDE.Runtime.Components;

/// <summary>How far inside a control's bounds its contained controls are measured from.</summary>
public readonly record struct DesignerInset(double Left, double Top, double Right, double Bottom);
