namespace HexIDE.AddinPacker;

public sealed class ReleaseTrustRequest
{
    public required string RootOut { get; init; }
    public required string IntermediateOut { get; init; }
    public required string FirstPartyOut { get; init; }
    public required string PublicOut { get; init; }
    public required string IntermediateId { get; init; }
    public required string IntermediateName { get; init; }
    public required string FirstPartyId { get; init; }
    public required string FirstPartyName { get; init; }
}
