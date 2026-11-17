using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using HexIDE.TwinBasic;

namespace HexIDE.Projects;

/// <summary>The immutable view the read-only container browser consumes.</summary>
public interface IContainerProject
{
    string ContainerPath { get; }
    string Name { get; }
    ContainerFolder Root { get; }
    bool CanSynchronizeFolder { get; }
    bool SupportsLanguageServers { get; }
    bool IsLocked { get; }
    string? NoticeKey { get; }
    string? BuildType { get; }
    bool TryGetEntry(ArchiveEntryIdentity identity, [NotNullWhen(true)] out ContainerEntry? entry);
}

public class ContainerEntry
{
    public ArchiveEntryIdentity Identity { get; }
    public string Name { get; }
    public string? CaptionKey { get; init; }
    public IReadOnlyList<ContainerEntry> Children { get; }
    public ArchiveProjectEntry? Archive { get; }
    public string? Source { get; init; }
    public string? NoticeKey { get; init; }
    public bool IsDocument { get; init; }
    public bool IsHidden => Archive is { } archive && (archive.Source.Flags & 3) != 0;
    public ArchiveText? ReadText() => Archive is ArchiveProjectDocument document ? document.ReadText()
        : Source is { } source ? new(ArchiveTextStatus.Available, source) : null;

    internal ContainerEntry(ArchiveEntryIdentity identity, string name, IReadOnlyList<ContainerEntry> children,
        ArchiveProjectEntry? archive = null)
    {
        Identity = identity;
        Name = name;
        Children = children;
        Archive = archive;
    }

    internal static ContainerEntry FromArchive(ArchiveProjectEntry entry) => entry is ArchiveProjectFolder folder
        ? new ContainerFolder(entry.Identity, entry.Name, folder.Children.Select(FromArchive).ToArray(), entry)
        : new ContainerDocument(entry.Identity, entry.Name, entry);
}

public sealed class ContainerFolder : ContainerEntry
{
    internal ContainerFolder(ArchiveEntryIdentity identity, string name, IReadOnlyList<ContainerEntry> children,
        ArchiveProjectEntry? archive = null) : base(identity, name, children, archive) { }
}

public sealed class ContainerDocument : ContainerEntry
{
    internal ContainerDocument(ArchiveEntryIdentity identity, string name, ArchiveProjectEntry? archive = null)
        : base(identity, name, [], archive) => IsDocument = true;
    public new ArchiveText ReadText() => base.ReadText() ?? new(ArchiveTextStatus.UnsupportedType, null);
}

internal sealed class TwinBasicBrowserProject : IContainerProject
{
    private readonly ArchiveProjectSnapshot snapshot;
    private readonly Dictionary<ArchiveEntryIdentity, ContainerEntry> entries = new();
    public string ContainerPath => snapshot.ContainerPath;
    public string Name => snapshot.Name;
    public ContainerFolder Root { get; }
    public bool CanSynchronizeFolder => true;
    public bool SupportsLanguageServers => true;
    public bool IsLocked => false;
    public string? NoticeKey => null;
    public string? BuildType => TwinBasicCompilationConstants.BuildTypeOf(snapshot.Container);

    public TwinBasicBrowserProject(ArchiveProjectSnapshot snapshot)
    {
        this.snapshot = snapshot;
        Root = (ContainerFolder)ContainerEntry.FromArchive(snapshot.Root);
        void Add(ContainerEntry entry)
        {
            entries.Add(entry.Identity, entry);
            foreach (var child in entry.Children) Add(child);
        }
        Add(Root);
    }

    public bool TryGetEntry(ArchiveEntryIdentity identity, [NotNullWhen(true)] out ContainerEntry? entry) =>
        entries.TryGetValue(identity, out entry);
}
