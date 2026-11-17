using System.Collections.Generic;
using System.Linq;
using HexIDE.TwinBasic;
using HexIDE.Projects;
using HexIDE.Localization;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Tools;

public partial class ArchiveTreeNode : IProjectTreeElement
{
    public ContainerEntry Entry { get; }
    public string Name { get; }
    public string ContainerPath => Entry.Identity.ContainerPath;
    public string Tooltip { get; }
    public bool CanSynchronizeFolder { get; }
    public string AccessibleName => Name;
    [Notify] private bool isExpanded;
    public IReadOnlyList<ArchiveTreeNode> Children { get; }
    public ArchiveTreeNode(ArchiveProjectEntry entry, bool showHidden = false, string? rootCaption = null)
        : this(ContainerEntry.FromArchive(entry), showHidden, rootCaption) { }

    public ArchiveTreeNode(ContainerEntry entry, bool showHidden = false, string? rootCaption = null, ILocalizationService? localization = null, string? tooltip = null, bool canSynchronizeFolder = true)
    {
        Entry = entry;
        Name = rootCaption ?? (entry.CaptionKey is { } key ? localization?.GetString(key) ?? key : entry.Name);
        Tooltip = tooltip ?? ContainerPath;
        CanSynchronizeFolder = canSynchronizeFolder;
        Children = entry.Children.Where(child => showHidden || !child.IsHidden)
            .Select(child => new ArchiveTreeNode(child, showHidden, localization: localization, tooltip: tooltip, canSynchronizeFolder: canSynchronizeFolder)).ToArray();
    }
}
