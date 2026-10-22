using System;
using System.Collections.Generic;

namespace HexIDE.Projects;

/// <summary>
/// A project file was not written, because it would have left out members that have never been saved, and
/// so have no file for it to list. Nothing else in the same operation was written either.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="OperationCanceledException"/>, as <see cref="HexIDE.IDE.SourceEncodingRefusal"/> is and for the
/// same reason: every caller already treats a refused save as a cancelled one, so a close that asked for this
/// save keeps the project open rather than discarding what the developer chose to keep.
/// </para>
/// <para>
/// VB6 cannot write such a file, because its save prompt is one Yes over the whole list. HexIDE's prompt lets
/// each file be ticked alone, which is how a project came to be ticked without its never-saved form. For the
/// startup form, the file written then still said <c>Startup="Form1"</c>, and vb6.exe compiles that, in silence,
/// into a program that starts at <c>Sub Main</c> (hexide-io/HexIDE#497).
/// </para>
/// <see cref="Exception.Message"/> is the sentence an automation caller receives; a person sees the localized
/// warning instead.
/// </remarks>
public sealed class UnsavedMemberRefusal : OperationCanceledException
{
    public UnsavedMemberRefusal(string project, IReadOnlyList<string> members)
        : base(Describe(project, members))
    {
        Project = project;
        Members = members;
    }

    /// <summary>The project whose file was not written, by name.</summary>
    public string Project { get; }

    /// <summary>The forms and modules it would have left out, by name, in the order the project holds them.</summary>
    public IReadOnlyList<string> Members { get; }

    private static string Describe(string project, IReadOnlyList<string> members) =>
        $"Nothing was saved: {project}'s project file would leave out {string.Join(", ", members)}, "
        + (members.Count == 1 ? "which has" : "which have") + " never been saved and so has no file to list. "
        + "Save " + (members.Count == 1 ? "it" : "them") + " with the project, or do not save the project.";
}
