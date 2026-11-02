using System;
using System.Collections.Generic;

namespace HexIDE.Addins;

/// <summary>A snapshot of a successfully verified build's declaration, not a grant or a record of use.</summary>
public sealed class AddinCapabilityDeclaration
{
    internal AddinCapabilityDeclaration(AddinManifest manifest, string manifestHash)
    {
        Id = manifest.Id;
        PublisherId = manifest.PublisherId;
        ManifestHash = manifestHash;
        IsLegacy = manifest.Schema < 2;
        var capabilities = new List<string>();
        if (!IsLegacy)
            foreach (var capability in manifest.Capabilities!.Value.EnumerateArray())
                capabilities.Add(capability.GetString()!);
        Capabilities = Array.AsReadOnly(capabilities.ToArray());
    }

    public string Id { get; }
    public string PublisherId { get; }
    public string ManifestHash { get; }
    /// <summary>Older schemas have no declaration; their stray capability fields confer no authority.</summary>
    public bool IsLegacy { get; }
    public IReadOnlyList<string> Capabilities { get; }
}
