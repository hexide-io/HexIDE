namespace HexIDE.Runtime.ProjectElements;

/// <summary>What a file picked through Add File becomes when it joins a project.</summary>
public enum ProjectFileKind
{
    /// <summary>A file the project carries but does not compile — the safe default.</summary>
    RelatedDocument,
    Form,
    StandardModule,
    ClassModule,
    UserControl,
    PropertyPage,
}

/// <summary>
/// Decides, from a file's extension alone, what kind of project member it should become.
///
/// <para>
/// This is the "decide by extension, offer related as non-default" half of Add File. It is deliberately
/// separate from <c>SerializedProject.IsVb6CodeFile</c>, which answers a different question about a
/// different situation, and the two disagree on purpose — see <see cref="Classify"/>.
/// </para>
/// </summary>
public static class ProjectFileClassifier
{
    private static readonly Dictionary<string, ProjectFileKind> ByExtension =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".frm"] = ProjectFileKind.Form,
            [".bas"] = ProjectFileKind.StandardModule,
            [".cls"] = ProjectFileKind.ClassModule,
            [".ctl"] = ProjectFileKind.UserControl,
            [".pag"] = ProjectFileKind.PropertyPage,
        };

    /// <summary>
    /// The kind <paramref name="path"/> should join the project as. Never throws and never returns "no
    /// answer": anything unrecognised is a related document, which is the outcome that cannot damage the
    /// file.
    ///
    /// <para>
    /// <b>Two extensions are missing from the table on purpose.</b> <c>.dob</c> and <c>.dsr</c> are VB6
    /// source, and <c>SerializedProject.IsVb6CodeFile</c> rightly says so — but HexIDE models neither
    /// (ActiveX Documents are out of scope, and a <c>UserDocument=</c> line is preserved verbatim rather
    /// than loaded). Classifying one as a module here would promise a designer that does not exist and
    /// hand a file HexIDE cannot parse to the VB6 save path. As a related document it opens as text and is
    /// written back byte-for-byte, which is the honest answer.
    /// </para>
    ///
    /// <para>
    /// <b>A file with no extension is a related document here, and a module there.</b> Not an
    /// inconsistency: reading a <c>Module=Foo; somefile</c> line means the project already claims the file
    /// is source, and the conservative move is to believe the claim. Add File has no such claim to
    /// preserve — the developer picked a file out of a dialog, and an extensionless one is far likelier to
    /// be a LICENSE or a Makefile than VB6 source.
    /// </para>
    /// </summary>
    public static ProjectFileKind Classify(string path)
    {
        var extension = System.IO.Path.GetExtension(path);
        return ByExtension.TryGetValue(extension, out var kind) ? kind : ProjectFileKind.RelatedDocument;
    }

    /// <summary>The <see cref="ModuleKind"/> for a kind that is a module, or null for a form or related document.</summary>
    public static ModuleKind? AsModuleKind(this ProjectFileKind kind) => kind switch
    {
        ProjectFileKind.StandardModule => ModuleKind.StandardModule,
        ProjectFileKind.ClassModule => ModuleKind.ClassModule,
        ProjectFileKind.UserControl => ModuleKind.UserControl,
        ProjectFileKind.PropertyPage => ModuleKind.PropertyPage,
        _ => null,
    };
}
