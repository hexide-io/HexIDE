namespace HexIDE.Language;

/// <summary>
/// Registers a language's editing helpers (#5403), through the host an add-in is given. Internal while phase 2 of #3411
/// composes the Basic provider alone: the call moves onto <c>IHexIdeHost</c>, with the contracts it reaches baselined as add-in
/// API, when the registration is published (#3411 task 3.1). <see cref="ConditionalConstants"/> has the VB family's shape,
/// which is why it waits.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only the owners HexIDE grants it may call it,</b> checked where it's implemented, so reaching it by reflection gains
/// nothing. Today the only grant is the Basic provider's.
/// </para>
/// <para>
/// <b>It only adds.</b> A registration naming a language id another owner already serves is refused whole, naming both owners.
/// </para>
/// <para>
/// <b>Its code is contained.</b> A throw from a helper is logged, that call answers as though nothing were registered, and the
/// registration stays. A helper that never returns isn't contained: only first-party code registers in phase 2.
/// </para>
/// </remarks>
internal interface ILanguageEditingRegistration
{
    /// <summary>Makes <paramref name="editing"/>'s helpers the ones its languages are edited with.</summary>
    /// <returns>
    /// What takes them out again. Turning the owner off or revoking its consent takes them out too, and an open code window
    /// then stops colouring and greying.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The owner holds no grant, or another owner serves one of its languages (the message names both owners). Also thrown by
    /// a host that isn't an owner's own.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="editing"/> names no language, or a blank one.</exception>
    IDisposable RegisterLanguageEditing(LanguageEditing editing);
}
