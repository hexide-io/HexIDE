namespace HexIDE.Addins;

/// <summary>
/// The diagnostics the IDE holds, as an add-in reads them.
/// </summary>
/// <remarks>
/// Lines and columns are 1-based, and a line counts from the top of the file with its header included, as the
/// code window numbers it. <see cref="TryGetFor"/> is the member to call for one document: it says why a name
/// found no single document, where <see cref="GetFor(string, string?)"/> answers an empty list for that and for
/// a document with no diagnostics alike.
/// </remarks>
public interface IDiagnosticsAccess
{
    IReadOnlyList<AddinDiagnostic> GetAll();

    /// <inheritdoc cref="GetFor(string, string?)"/>
    /// <remarks>
    /// Kept for add-ins written before a document could be named by its project. It searches every loaded
    /// project, and answers empty when the name is ambiguous rather than merging two documents' diagnostics.
    /// </remarks>
    IReadOnlyList<AddinDiagnostic> GetFor(string fileName);

    /// <summary>
    /// The diagnostics currently held for one document.
    /// </summary>
    /// <param name="fileName">The document's VB6 name, in any case.</param>
    /// <param name="project">
    /// The project to look in, by name, or null to search every loaded one.
    /// </param>
    /// <returns>
    /// The document's diagnostics; and an empty list when no loaded project has the name given as
    /// <paramref name="project"/>, when no such document was found, or when the name was ambiguous.
    /// <see cref="TryGetFor"/> says which.
    /// </returns>
    /// <remarks>Kept so that an add-in built against an earlier version keeps running unchanged.</remarks>
    IReadOnlyList<AddinDiagnostic> GetFor(string fileName, string? project);

    /// <summary>
    /// The diagnostics currently held for one document, and why not when the name does not find one document.
    /// </summary>
    /// <inheritdoc cref="GetFor(string, string?)" path="/param"/>
    /// <returns>
    /// The document's diagnostics, which may be none; or, refused, an empty list and the reason: no loaded project
    /// of the name given as <paramref name="project"/>, no such document, or a name two loaded projects share,
    /// with both listed and how to say which is meant.
    /// </returns>
    /// <remarks>
    /// <b>HexIDE's own host refuses</b>, with the reasons automation gives for the same names. The body here
    /// serves only a host that implements nothing newer than <see cref="GetFor(string, string?)"/>, such as an
    /// add-in's own test double, and answers that member's list with no reason: such a host never refuses, and an
    /// empty list from it may still mean the name found nothing. It does not invent a refusal it cannot know
    /// about, any more than <see cref="AddinEditResult.WithoutReason"/> invents a reason.
    /// </remarks>
    AddinDiagnosticsResult TryGetFor(string fileName, string? project) =>
        new(GetFor(fileName, project));
}
