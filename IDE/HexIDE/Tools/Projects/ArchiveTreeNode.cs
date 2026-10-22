using System.Collections.Generic;
using System.Linq;
using HexIDE.TwinBasic;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Tools;

public partial class ArchiveTreeNode : IProjectTreeElement
{
    public ArchiveProjectEntry Entry { get; }
    public string Name { get; }
    public string ContainerPath => Entry.Identity.ContainerPath;
    public string AccessibleName => Name;
    [Notify] private bool isExpanded;
    public IReadOnlyList<ArchiveTreeNode> Children { get; }
    public ArchiveTreeNode(ArchiveProjectEntry entry, bool showHidden = false, string? rootCaption = null)
    {
        Entry = entry;
        Name = rootCaption ?? entry.Name;
        Children = entry is ArchiveProjectFolder folder
            ? folder.Children.Where(child => showHidden || (child.Source.Flags & 3) == 0)
                .Select(child => new ArchiveTreeNode(child, showHidden)).ToArray() : [];
    }
}
