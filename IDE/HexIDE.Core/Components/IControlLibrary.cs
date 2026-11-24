using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Components;

/// <summary>A control library the IDE can reference and offer in its toolbox.</summary>
public interface IControlLibrary
{
    /// <summary>
    /// What VB6's Components dialog lists the library as: its type library's registry description, which the dialog shows
    /// as it is (measured, #1103). The service pack is part of it: "Microsoft Windows Common Controls 6.0 (SP6)".
    /// </summary>
    string Description { get; }

    /// <summary>The <c>.vbp</c> line that references the library, as VB6's Save Project writes it (measured, #1103).</summary>
    string ProjectLine { get; }

    /// <summary>The form header line that declares the library, as VB6's designer writes it when one is placed (#1082).</summary>
    string FormLine { get; }

    /// <summary>The library's name, which qualifies its controls' type names and its constants: <c>MSComctlLib</c>.</summary>
    string Name { get; }

    /// <summary>Its type library's GUID, with braces.</summary>
    string Guid { get; }

    /// <summary>The control file: MSCOMCTL.OCX.</summary>
    string File { get; }

    /// <summary>A control's type name in this library: <c>MSComctlLib.ProgressBar</c>.</summary>
    string TypeName(string className);

    /// <summary>Whether <paramref name="reference"/> names this library, whatever version it asks for.</summary>
    bool IsNamedBy(ObjectReference reference);

    /// <summary>Whether <paramref name="reference"/> names this library at its own version: GUID and version both match.</summary>
    bool IsNamedBy(VbReference reference);
}
