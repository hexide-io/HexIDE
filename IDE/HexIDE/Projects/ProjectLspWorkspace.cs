using System;
using System.Collections.Generic;
using System.IO;
using HexIDE.IDE;
using HexIDE.Lsp;

namespace HexIDE.Projects;

/// <summary>
/// Answers "where is the workspace" for the language layer, from whichever project is currently the
/// startup one.
///
/// <para>
/// Deliberately thin, and deliberately here rather than in the language layer. Reusing
/// <see cref="ProjectService.ProjectFilesDirectory"/> is the whole point: that is already the rule
/// deciding where a project's files live, including the fallback for a project that has never been saved,
/// and a second answer to the same question would drift from it.
/// </para>
///
/// <para>
/// <b>An unsaved project's directory is a private temp path that does not exist yet.</b> It used to be
/// shared — keyed on the project's NAME, so every new "Project1" resolved to the same place, and a server
/// could be rooted among another project's leavings. hexide-io/HexIDE#260 fixed that by appending a GUID,
/// which means the path is now unique per project and, in exchange, <b>never exists before the first file
/// is written into it</b>: <c>ProjectFilesDirectory</c> mints and memoises the string, and the four call
/// sites that write a file there create the directory at that moment.
/// </para>
///
/// <para>
/// Answering with it anyway is right, and it is what this type does. It is the directory the project's own
/// files are about to be written into and the parent of every document URI the server will be sent, so it
/// is the correct answer to "which tree should you analyse" even while empty. What must NOT be done with it
/// is launch a process there — that throws, and cost the whole connection until hexide-io/HexIDE#278; see
/// <c>LspLaunchDirectory</c>, which separates the two questions.
/// </para>
///
/// <para>
/// <b>The project manager arrives as a factory, and it has to.</b> This type closes a dependency cycle —
/// <c>ProjectManager</c> needs an editor service, which builds code editors, which need the language
/// client, which needs this. Pure.DI cannot order a cycle, and it does not refuse one either: it emits the
/// singleton field <em>unguarded</em>, so whichever participant is constructed first receives
/// <see langword="null"/> for the back edge. Taking it as a factory defers the resolution past
/// construction, which is the same way <c>EditorService</c> holds its view-model factories.
/// </para>
///
/// <para>
/// This was not a hypothetical. The direct dependency compiled, passed every test, and made
/// <c>Directory</c> throw a <see cref="NullReferenceException"/> on the first document opened — which
/// <c>CodeEditorViewModel.Initialize</c> logged and swallowed, so no language server ever started and the
/// IDE simply had no language features. Only running it found that.
/// </para>
///
/// <para>
/// The manager is matched rather than dereferenced for the same reason. The factory defers the back edge
/// past construction, but the generated code still reads an unguarded field in some paths, so a null is
/// reachable in principle. This type already has a defined answer for "there is no workspace yet" —
/// <see langword="null"/> — and giving that answer costs a lazily started server nothing, where throwing
/// costs every language feature in the IDE.
/// </para>
/// </summary>
public sealed class ProjectLspWorkspace(Func<IProjectManager> projectManager) : ILspWorkspace
{
    public string? Directory =>
        projectManager() is { StartupProject: { } project }
            ? ProjectService.ProjectFilesDirectory(project)
            : null;

    /// <summary>One folder per loaded project, named as the user sees the project named.</summary>
    /// <remarks>
    /// <para>
    /// Deduplicated by full path, and <b>ordinally</b>. Two projects genuinely sharing a directory produce
    /// the same string and collapse to one entry, which is what a server wants — it should not index the
    /// same tree twice. Two differently-cased spellings of one Windows directory do not collapse, and that
    /// is the deliberate direction to err: a duplicate folder is harmless, whereas merging two folders that
    /// only look alike on a case-insensitive filesystem would drop one project's root on Linux, where the
    /// paths are genuinely distinct.
    /// </para>
    ///
    /// <para>
    /// Empty rather than null when nothing is loaded — "no folders" is a state the protocol can express,
    /// and the caller turns it into the null the wire wants.
    /// </para>
    /// </remarks>
    public IReadOnlyList<LspWorkspaceFolder> Folders
    {
        get
        {
            if (projectManager() is not { LoadedProjects: { } projects }) return [];

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var folders = new List<LspWorkspaceFolder>();
            foreach (var project in projects)
            {
                var directory = ProjectService.ProjectFilesDirectory(project);
                if (string.IsNullOrWhiteSpace(directory)) continue;

                var full = TryFullPath(directory);
                if (full is null || !seen.Add(full)) continue;

                folders.Add(new LspWorkspaceFolder(project.Name, full));
            }
            return folders;
        }
    }

    // A path that cannot be resolved costs that folder, not the workspace: the others are still worth
    // sending, and the one that failed was never going to root a server usefully.
    private static string? TryFullPath(string directory)
    {
        try { return Path.GetFullPath(directory); }
        catch (Exception) { return null; }
    }
}
