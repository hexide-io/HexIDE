using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HexIDE.Recovery;
using HexIDE.Runtime.Serialization;
using Serilog;

namespace HexIDE.Projects;

// Crash recovery's restore (hexide-io/HexIDE#499 task group 2). Restored work comes back as unsaved work: the model
// takes the backup, and every baseline stays what the file on disk is, so the document reads edited and only a save
// writes it.
public partial class ProjectService
{
    /// <inheritdoc/>
    public bool ApplyRecovered(FormDefinition form, UnsavedFile content)
    {
        var blobs = content.Companion is { Length: > 0 } companion ? FrxDeserializer.Read(companion, content.Text) : null;
        var fresh = new FormDeserializer().Deserialize(form.Owner, content.Text, new DeserializeErrorSink(localization),
            blobs, BuildUserControlRegistry(form.Owner));
        if (fresh is null)
            return false;
        form.UpdateCode(fresh.Code);
        form.UpdateComponents(fresh.Components);
        form.AdoptFidelityState(fresh);
        return true;
    }

    /// <inheritdoc/>
    public bool ApplyRecovered(ModuleDefinition module, UnsavedFile content)
    {
        if (module.Kind is ModuleKind.UserControl or ModuleKind.PropertyPage)
        {
            var blobs = content.Companion is { Length: > 0 } companion ? FrxDeserializer.Read(companion, content.Text) : null;
            var fresh = new FormDeserializer().Deserialize(module.Owner, content.Text, new DeserializeErrorSink(localization), blobs);
            if (fresh is null)
                return false;
            module.UpdateCode(fresh.Code);
            if (module.FormPart is { } existing)
            {
                // In place, so an open designer's reference to it stays good, as a reload from disk does.
                existing.UpdateCode(fresh.Code);
                existing.UpdateComponents(fresh.Components);
                existing.AdoptFidelityState(fresh);
            }
            else
            {
                fresh.AbsolutePath = module.AbsolutePath;
                module.UpdateFormPart(fresh);
            }
            return true;
        }

        var (header, body) = ModuleFileFormat.SplitHeader(content.Text, module.Kind);
        module.RecordOriginalHeader(header);
        module.UpdateCode(body);
        return true;
    }

    /// <inheritdoc/>
    public bool RecoveredMatches(FormDefinition form, UnsavedFile content) =>
        string.Equals(RenderForm(form, content.FileName).Text, content.Text, StringComparison.Ordinal);

    /// <inheritdoc/>
    public bool RecoveredMatches(ModuleDefinition module, UnsavedFile content) =>
        string.Equals(RenderModule(module, content.FileName).Text, content.Text, StringComparison.Ordinal);

    /// <inheritdoc/>
    public object? AddRecovered(ProjectDefinition project, UnsavedDocument document)
    {
        if (document.Content is not { } content || CheckAdoptedName(project, document.Name) != AdoptionRefusal.None)
            return null;

        if (document.Kind == "Form")
        {
            var blobs = content.Companion is { Length: > 0 } companion ? FrxDeserializer.Read(companion, content.Text) : null;
            var form = new FormDeserializer().Deserialize(project, content.Text, new DeserializeErrorSink(localization),
                blobs, BuildUserControlRegistry(project));
            if (form is null)
                return null;
            form.TextEncoding = document.Encoding;
            project.AddForm(form);
            return form;
        }

        if (!Enum.TryParse<ModuleKind>(document.Kind, out var kind))
            return null;
        var module = new ModuleDefinition(project, document.Name, kind) { TextEncoding = document.Encoding };
        if (!ApplyRecovered(module, content))
            return null;
        project.AddModule(module);
        return module;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <b>The project is rebuilt by the loader every project goes through,</b> from its backed-up project file put back
    /// where it was rendered. Every member the text names with no file of its own was rendered beside it, so it is put
    /// back there too, as UTF-8 with a byte-order mark, which the loader reads whatever the characters. A member whose
    /// file is still on disk is read from there, and takes its backup afterwards.
    /// </para>
    /// <para>
    /// <b>Then everything put back is made unsaved again:</b> the project and those members lose their paths and take
    /// their recorded code pages, and the folder goes. Nothing put back is recorded as a file on disk.
    /// </para>
    /// <para>
    /// <b>Only a folder that is new or empty is written to,</b> so a backup can't overwrite anything.
    /// </para>
    /// </remarks>
    public async Task<ProjectDefinition?> RestoreUnsavedProject(UnsavedProject backup)
    {
        if (backup.ProjectFile is not { } projectFile || backup.RenderedAt is not { Length: > 0 } renderedAt
            || Path.GetDirectoryName(renderedAt) is not { Length: > 0 } directory)
            return null;
        // Both places a backup is rendered against are under TEMP: a never-saved project's own folder, and a scratch
        // project's. A record naming anywhere else is not followed.
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(directory).StartsWith(temp, StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning("Crash recovery did not restore {Project}: {Folder} is not under the temporary directory", backup.Name, directory);
            return null;
        }
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Log.Warning("Crash recovery did not restore {Project}: {Folder} is not empty", backup.Name, directory);
            return null;
        }

        var put = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            PutBack(renderedAt, projectFile.Text);
            foreach (var document in backup.Documents)
            {
                if (document.Content is not { } content || document.Path is { } path && File.Exists(path))
                    continue;
                if (FileNameIn(content.FileName) is not { } fileName)
                    continue;
                PutBack(Path.Combine(directory, fileName), content.Text);
                if (content is { CompanionFileName: { } companionName, Companion: { } companion }
                    && FileNameIn(companionName) is { } companionFile)
                {
                    var companionPath = Path.Combine(directory, companionFile);
                    AtomicFileWriter.WriteAllBytes(companionPath, companion);
                    put.Add(Path.GetFullPath(companionPath));
                }
            }

            var errorSink = new DeserializeErrorSink(localization);
            var (project, _, _, _) = await new ProjectLoader
            {
                HostAnsi = HostAnsi,
                FileRead = (path, bytes) =>
                {
                    if (!put.Contains(Path.GetFullPath(path)))
                        baselineStore.Record(path, bytes);
                },
            }.LoadAsync(renderedAt, errorSink);

            project.AbsolutePath = null;
            project.ItemLinesDirectory = null;
            project.WorkingDirectory = null;
            project.TextEncoding = backup.Encoding;
            var encodings = backup.Documents.ToDictionary(d => d.Name, d => d.Encoding, StringComparer.OrdinalIgnoreCase);
            foreach (var form in project.Forms)
                Unsave(form.AbsolutePath, () => form.AbsolutePath = null, encoding => form.TextEncoding = encoding, form.Name);
            foreach (var module in project.Modules)
                Unsave(module.AbsolutePath, () =>
                {
                    module.AbsolutePath = null;
                    if (module.FormPart is { } part)
                        part.AbsolutePath = null;
                }, encoding => module.TextEncoding = encoding, module.Name);

            SnapshotRenderBaselines(project);
            projectManager.AddProject(project);
            EnsureGroupIfMultiple();

            // A member with a file of its own was read from it, so it takes its backup now, after its baseline.
            foreach (var document in backup.Documents)
            {
                if (document is not { Content: { } content, Path: { } path } || !File.Exists(path))
                    continue;
                if (project.Forms.FirstOrDefault(f => Same(f.Name, document.Name)) is { } form)
                    ApplyRecovered(form, content);
                else if (project.Modules.FirstOrDefault(m => Same(m.Name, document.Name)) is { } module)
                    ApplyRecovered(module, content);
            }
            return project;

            void Unsave(string? path, Action clearPath, Action<Vb6TextEncoding> setEncoding, string name)
            {
                if (path is null || !put.Contains(Path.GetFullPath(path)))
                    return;
                clearPath();
                if (encodings.TryGetValue(name, out var encoding))
                    setEncoding(encoding);
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warning(ex, "Crash recovery could not remove {Folder} after restoring {Project}", directory, backup.Name);
            }
        }

        void PutBack(string path, string text)
        {
            AtomicFileWriter.WriteAllBytes(path, [.. Encoding.UTF8.GetPreamble(), .. new UTF8Encoding(false).GetBytes(text)]);
            put.Add(Path.GetFullPath(path));
        }
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // The last segment of a name from a backup's record, where it is a file name at all.
    private static string? FileNameIn(string candidate) =>
        Path.GetFileName(candidate) is { Length: > 0 } name && name is not ("." or "..")
            && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            ? name
            : null;
}
