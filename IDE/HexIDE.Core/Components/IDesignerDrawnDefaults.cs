namespace HexIDE.Runtime.Components;

/// <summary>
/// What a component class sets on a control as the designer draws it, beyond the Caption or Text the designer gives
/// every control its name in (#5633). Optional.
/// </summary>
/// <remarks>A throw is logged, and the control is drawn as though the class set nothing.</remarks>
public interface IDesignerDrawnDefaults
{
    /// <summary>Sets <paramref name="instance"/>'s own starting values, for a control the designer has just drawn and named <paramref name="name"/>.</summary>
    void Drawn(ComponentInstance instance, string name);
}
