namespace HexIDE.Runtime.ProjectElements;

/// <summary>
/// Why a form cannot be reproduced on save. A cause rather than a sentence, because the sentence has to be
/// shown to the developer in their own language and this is decided in the runtime, which has no
/// localisation service and should not gain one.
///
/// Flags, because a form can be unreproducible in more than one way at once — <c>Splash Screen.frm</c> is
/// both nested inside containers and carrying binary content HexIDE cannot re-emit. Everything that records
/// a cause ORs it in; nothing ever clears one.
/// </summary>
[System.Flags]
public enum UnfaithfulSaveCause
{
    None = 0,

    /// <summary>
    /// A control sits inside a container and HexIDE would write it as a sibling of that container, so the
    /// form silently becomes a different form. Menus are excluded — their hierarchy round-trips.
    /// </summary>
    NestedContainers = 1 << 0,

    /// <summary>
    /// The file references content in its companion binary through a property HexIDE does not model, so a
    /// save would drop the reference. The bytes survive — a separate guard leaves the companion alone — but
    /// nothing points at them any more, which is a picture silently disappearing from a control.
    /// </summary>
    UnreproducibleBinaryContent = 1 << 1,
}
