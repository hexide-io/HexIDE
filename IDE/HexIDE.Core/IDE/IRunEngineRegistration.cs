using System;

namespace HexIDE.IDE;

/// <summary>
/// Registers the engine that runs projects in the IDE (#5616), through the host an add-in is given. Internal while phase 2
/// of #3411 composes the Basic provider alone: the call moves onto <c>IHexIdeHost</c>, with the engine contract it reaches
/// baselined as add-in API, when a second engine registers (#3411 task 3.1).
/// </summary>
/// <remarks>
/// <para>
/// <b>Only the owners HexIDE grants it may call it,</b> checked where it's implemented, so reaching it by reflection gains
/// nothing. Today the only grant is the Basic provider's.
/// </para>
/// <para>
/// <b>One at a time.</b> While one registration holds, another is refused, naming both owners.
/// </para>
/// <para>
/// <b>Its code is contained.</b> A throw from the engine is logged, and that call answers as with no engine.
/// </para>
/// </remarks>
internal interface IRunEngineRegistration
{
    /// <summary>Makes <paramref name="engine"/>'s result what runs projects; it's called once, at once.</summary>
    /// <returns>
    /// What takes the engine out again. A program it is running is ended first, as End ends it, and an engine that is
    /// <see cref="IDisposable"/> is then disposed, to put back anything it installed. Turning the owner off or revoking its
    /// consent takes it out too.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The owner holds no grant, another owner's engine is registered (the message names both owners), or
    /// <paramref name="engine"/> threw or made nothing. Also thrown by a host that isn't an owner's own.
    /// </exception>
    IDisposable RegisterRunEngine(Func<RunEngineServices, IRunEngine> engine);
}
