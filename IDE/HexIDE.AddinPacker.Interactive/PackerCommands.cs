namespace HexIDE.AddinPacker.Interactive;

/// <summary>What an option's answer must be. It decides how hexide-pack checks an answer and how it asks.</summary>
public enum OptionKind
{
    /// <summary>Any text that isn't blank.</summary>
    Text,
    /// <summary>A publisher or intermediate id, as <c>PublisherId.IsValid</c> accepts.</summary>
    PublisherId,
    /// <summary>A name users see, as the packer's display-name rule accepts.</summary>
    DisplayName,
    /// <summary>A comma-separated list of namespaces, each one a publisher may be issued.</summary>
    Namespaces,
    /// <summary>A HexIDE version, such as 0.7.0.</summary>
    HexideVersion,
    /// <summary>A file that exists. A key file's path is checked for existence only; it is never opened here.</summary>
    ExistingFile,
    /// <summary>A folder that exists.</summary>
    ExistingFolder,
    /// <summary>A folder holding <see cref="PackerCommands.TrustFiles"/>, checked by name only.</summary>
    TrustFolder,
    /// <summary>A file inside the answer to <c>--src</c>.</summary>
    FileInSource,
    /// <summary>A folder the command writes public output to.</summary>
    OutputFolder,
    /// <summary>A folder the command writes private keys to. Refused inside a git clone.</summary>
    KeyFolder,
    /// <summary>One of <see cref="PackerCommands.Capabilities"/>.</summary>
    Capability,
}

/// <summary>One option of a packer command. <see cref="Positional"/> marks <c>genkeys</c>' bare folder argument.</summary>
public sealed record PackerOption(string Name, OptionKind Kind, string Summary, bool Required = false,
    bool Repeatable = false, bool Positional = false, bool SuggestOnlyDll = false)
{
    /// <summary>How the option is written on a command line and in messages.</summary>
    public string Display => Positional ? $"<{Name}>" : $"--{Name}";
}

/// <summary>A packer command and its options, in the order hexide-pack asks for them and writes them.</summary>
public sealed record PackerCommand(string Name, string Summary, IReadOnlyList<PackerOption> Options);

/// <summary>
/// The packer's commands as hexide-pack knows them (#5555). The names are the ones <c>PackerCli.Run</c> reads, and
/// <c>PackerFrontEndBoundaryTests</c> fails when the two disagree, in either direction.
/// </summary>
public static class PackerCommands
{
    /// <summary>The capabilities HexIDE knows (<c>AddinCapabilities.IsKnown</c>), offered as a tick-list.</summary>
    public static readonly IReadOnlyList<string> Capabilities =
        ["project.read", "project.write", "program.run", "ui.contribute", "languageServer.contribute"];

    /// <summary>The files <c>pack --trust</c> reads, by name.</summary>
    public static readonly IReadOnlyList<string> TrustFiles =
        ["firstparty.key", "publisher.json", "publisher.sig", "intermediate.json", "intermediate.sig"];

    public static readonly IReadOnlyList<PackerCommand> All =
    [
        new("pack", "Sign an add-in: package a built add-in folder with your publisher record and key.",
        [
            new("src", OptionKind.ExistingFolder, "the folder holding the add-in's built files", Required: true),
            new("entry", OptionKind.FileInSource, "the add-in's main .dll, inside --src", Required: true,
                SuggestOnlyDll: true),
            new("id", OptionKind.Text, "the add-in's id, such as com.acme.tools", Required: true),
            new("title", OptionKind.Text, "the add-in's name, as users see it", Required: true),
            new("version", OptionKind.Text, "the add-in's version, such as 1.0.0", Required: true),
            new("trust", OptionKind.TrustFolder,
                "your trust folder: firstparty.key beside publisher.json/.sig and intermediate.json/.sig", Required: true),
            new("out", OptionKind.OutputFolder, "where to write the signed package", Required: true),
            new("description", OptionKind.Text, "a one-line description"),
            new("author", OptionKind.Text, "the author, as users see it"),
            new("capability", OptionKind.Capability, "what the add-in may do", Repeatable: true),
            new("include", OptionKind.FileInSource, "another file from --src to bundle, such as a private dependency",
                Repeatable: true),
            new("logo", OptionKind.FileInSource, "a PNG logo inside --src"),
            new("min-hexide-version", OptionKind.HexideVersion, "the oldest HexIDE the add-in runs on, such as 0.7.0"),
        ]),
        new("sign-publisher", "Issue a publisher record: sign a publisher's public key with the intermediate key.",
        [
            new("root-public-key", OptionKind.ExistingFile, "the root public key (hexide-root.pub)", Required: true),
            new("intermediate-key", OptionKind.ExistingFile, "the intermediate private key (intermediate.key)",
                Required: true),
            new("intermediate", OptionKind.ExistingFolder, "the folder holding intermediate.json and .sig",
                Required: true),
            new("publisher-id", OptionKind.PublisherId, "the publisher's id, such as acme.tools", Required: true),
            new("display-name", OptionKind.DisplayName, "the publisher's name, as users see it", Required: true),
            new("public-key", OptionKind.ExistingFile, "the publisher's public key file", Required: true),
            new("out", OptionKind.OutputFolder, "where to write publisher.json and .sig", Required: true),
            new("namespaces", OptionKind.Namespaces, "the publisher's namespaces, comma-separated, such as acme."),
        ]),
        new("genkeys-release", "Make a release trust chain: root, intermediate and first-party keys and records.",
        [
            new("root-out", OptionKind.KeyFolder, "where to write the root private key", Required: true),
            new("intermediate-out", OptionKind.KeyFolder, "where to write the intermediate private key",
                Required: true),
            new("firstparty-out", OptionKind.KeyFolder, "where to write the first-party private key", Required: true),
            new("public-out", OptionKind.OutputFolder, "where to write the public keys and records", Required: true),
            new("intermediate-id", OptionKind.PublisherId, "the intermediate's id, such as hexide-marketplace",
                Required: true),
            new("intermediate-name", OptionKind.DisplayName, "the intermediate's name, as users see it",
                Required: true),
            new("firstparty-id", OptionKind.PublisherId, "the first-party publisher's id", Required: true),
            new("firstparty-name", OptionKind.DisplayName, "the first-party publisher's name, as users see it",
                Required: true),
        ]),
        new("sign-revocations", "Sign a revocation list with the root key.",
        [
            new("in", OptionKind.ExistingFile, "the revocation list to sign (JSON)", Required: true),
            new("root-key", OptionKind.ExistingFile, "the root private key (root.key)", Required: true),
            new("out", OptionKind.OutputFolder, "where to write revocations.json and .sig", Required: true),
        ]),
        new("verify-revocations", "Check a signed revocation list against a root public key.",
        [
            new("json", OptionKind.ExistingFile, "the revocation list (revocations.json)", Required: true),
            new("sig", OptionKind.ExistingFile, "its signature (revocations.sig)", Required: true),
            new("root", OptionKind.ExistingFile, "the root public key (hexide-root.pub)", Required: true),
        ]),
        new("genkeys", "Make a throwaway development trust chain, for testing only.",
        [
            new("outDir", OptionKind.KeyFolder, "the folder to write the chain to", Required: true, Positional: true),
        ]),
    ];

    public static PackerCommand? Find(string name) =>
        All.FirstOrDefault(command => string.Equals(command.Name, name, StringComparison.Ordinal));
}
