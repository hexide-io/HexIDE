using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using HexIDE.Office;
using HexIDE.TwinBasic;

namespace HexIDE.Projects;

internal sealed class OfficeBrowserProject : IContainerProject
{
    public static string[] Patterns { get; } = ["*.xlsm", "*.xltm", "*.xlam", "*.xlsb", "*.docm", "*.dotm",
        "*.pptm", "*.potm", "*.ppsm", "*.ppam", "*.xls", "*.xlt", "*.xla", "*.doc", "*.dot"];
    private readonly Dictionary<ArchiveEntryIdentity, ContainerEntry> entries = new();
    public string ContainerPath { get; }
    public string Name { get; }
    public ContainerFolder Root { get; }
    public bool CanSynchronizeFolder => false;
    public bool SupportsLanguageServers => false;
    public bool IsLocked { get; }
    public string? NoticeKey => "Str.Office.SourceNotice";
    public string? BuildType => null;

    internal OfficeBrowserProject(string path, VbaProject project)
    {
        ContainerPath = path;
        Name = project.Name;
        IsLocked = project.IsLocked;
        ContainerEntry Entry(string name, string[] segments, ContainerEntry[] children) =>
            new(new ArchiveEntryIdentity(path, [.. segments]), name, children);
        var folders = new List<ContainerEntry>();
        if (!IsLocked)
        {
            foreach (var (kind, key) in new[]
            {
                (VbaModuleKind.Document, HostObjectsKey(path)),
                (VbaModuleKind.UserForm, "Str.Office.Forms"),
                (VbaModuleKind.Standard, "Str.Office.Modules"),
                (VbaModuleKind.Class, "Str.Office.Classes")
            })
            {
                var id = kind.ToString();
                var modules = project.Modules.Where(m => m.Kind == kind).Select(m =>
                    new ContainerDocument(new ArchiveEntryIdentity(path, [id, m.Name]), m.Name)
                    {
                        Source = m.Source,
                        IsDocument = true,
                        NoticeKey = kind == VbaModuleKind.UserForm ? "Str.Office.FormNotice" : "Str.Office.SourceNotice"
                    }).ToArray();
                if (modules.Length != 0)
                    folders.Add(new ContainerFolder(new ArchiveEntryIdentity(path, [id]), id, modules) { CaptionKey = key });
            }
        }
        if (project.References.Count != 0)
        {
            var references = project.References.Select((r, i) => Entry(
                string.IsNullOrEmpty(r.Name) ? r.Libid : r.Name,
                ["References", i.ToString(System.Globalization.CultureInfo.InvariantCulture)], [])).ToArray();
            folders.Add(new ContainerFolder(new ArchiveEntryIdentity(path, ["References"]), "References", references)
                { CaptionKey = "Str.Office.References" });
        }
        Root = new ContainerFolder(new ArchiveEntryIdentity(path, []), Name, folders.ToArray());
        void Add(ContainerEntry entry)
        {
            entries.Add(entry.Identity, entry);
            foreach (var child in entry.Children) Add(child);
        }
        Add(Root);
        // Only copied text and metadata escape the locator lifetime. Designer storage is borrowed;
        // #1114 will provide a detached representation before the browser can show the designer.
        // VBA tabs get no language-server session yet: routing a fileless module to a server is #4453.
    }

    public static OfficeBrowserProject Read(string path)
    {
        path = Path.GetFullPath(path);
        using var located = VbaProjectLocator.Locate(path);
        if (located.Status == VbaProjectStatus.NoProject) throw new OfficeProjectMissingException();
        if (located.Status == VbaProjectStatus.Unreadable) throw located.Error!;
        return new OfficeBrowserProject(path, VbaProjectReader.Read(located.Project!));
    }

    private static string HostObjectsKey(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".xlsm" or ".xltm" or ".xlam" or ".xlsb" or ".xls" or ".xlt" or ".xla" => "Str.Office.ExcelObjects",
        ".docm" or ".dotm" or ".doc" or ".dot" => "Str.Office.WordObjects",
        ".pptm" or ".potm" or ".ppsm" or ".ppam" => "Str.Office.PowerPointObjects",
        _ => "Str.Office.DocumentObjects"
    };

    public bool TryGetEntry(ArchiveEntryIdentity identity, [NotNullWhen(true)] out ContainerEntry? entry) =>
        entries.TryGetValue(identity, out entry);
}

internal sealed class OfficeProjectMissingException : Exception;
