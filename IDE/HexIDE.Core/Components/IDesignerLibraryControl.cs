namespace HexIDE.Runtime.Components;

/// <summary>
/// A control class that a registered control library offers, such as VB6's in-box ProgressBar in MSComctlLib (#5749).
/// Optional: the form designer asks a class which library one of its controls is in, to declare that library in the
/// form's header, and tells it which library's toolbox entry a control was placed from.
/// </summary>
/// <remarks>
/// <para>
/// <b>The designer declares only a library the IDE has registered as offering the class.</b> A library's header line
/// goes into the user's form, and libraries register only under a namespace their owner's grant keeps. An answer naming
/// any other library declares nothing, and is logged. Null, for a control in no library, declares nothing.
/// </para>
/// <para>
/// <b>Each call is contained on its own.</b> A throw is logged, and that call answers as a class without the contract
/// would: the control is placed as it would be, and no library is declared for it.
/// </para>
/// </remarks>
public interface IDesignerLibraryControl
{
    /// <summary>
    /// The library <paramref name="instance"/> is in, the one whose type name it's written under, or null when it's in none.
    /// </summary>
    IControlLibrary? LibraryOf(ComponentInstance instance);

    /// <summary>
    /// Records that <paramref name="instance"/>, just drawn, was placed from <paramref name="library"/>'s toolbox entry, so
    /// that it's written under that library's type name: a ProgressBar from COMCTL32.OCX is <c>ComctlLib.ProgressBar</c>.
    /// </summary>
    void PlacedFrom(ComponentInstance instance, IControlLibrary library);
}
