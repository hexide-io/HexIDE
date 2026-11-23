namespace HexIDE.Runtime.Components;

/// <summary>The facts about a control class that the form designer needs without drawing it.</summary>
public interface IDesignerComponent : IComponentClass
{
    /// <summary>Whether VB6 draws this control on its container's own surface, under every windowed control.</summary>
    bool IsWindowless { get; }

    /// <summary>The size, in twips, a control of this class takes when added by double-clicking its tool.</summary>
    (int Width, int Height) AddedSize { get; }

    /// <summary>How far inside this control's bounds its contained controls are measured from.</summary>
    DesignerInset ClientInset(ComponentInstance instance);
}
