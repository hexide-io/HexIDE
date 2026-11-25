namespace HexIDE.AddinPacker;

/// <summary>
/// The packer's command line; the usage text in <see cref="Run"/> lists every command. <c>Program.cs</c> calls it,
/// and so does hexide-pack once it has gathered a command line, so both reach the signing code through this one
/// dispatch (#5555). Moved from <c>Program.cs</c>'s top-level statements unchanged: same messages, same exit codes
/// (<c>PackerCliRefusalTests</c>).
/// </summary>
public static class PackerCli
{
    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: genkeys <outDir> | sign-publisher --root-public-key .. --intermediate-key .. --intermediate .. --publisher-id .. --display-name .. --public-key .. --out .. [--namespaces rdcore.,..] | pack --src .. --out .. --id .. --title .. --version .. --entry .. --trust .. [--key ..]");
            Console.Error.WriteLine("       genkeys-release --root-out .. --intermediate-out .. --firstparty-out .. --public-out ..");
            Console.Error.WriteLine("         --intermediate-id .. --intermediate-name .. --firstparty-id .. --firstparty-name ..");
            Console.Error.WriteLine("       sign-revocations --in .. --root-key .. --out .. | verify-revocations --json .. --sig .. --root ..");
            Console.Error.WriteLine("       request-publisher --publisher-id .. --display-name .. --github .. --fingerprint-url .. (--new-key .. | --key ..)");
            Console.Error.WriteLine("         --out .. [--namespaces acme.,..] [--previous-key ..]");
            Console.Error.WriteLine("       verify-request --request .. [--ledger ..] [--github ..]");
            return 1;
        }

        try
        {
            switch (args[0])
            {
                case "genkeys":
                    if (args.Length < 2) { Console.Error.WriteLine("genkeys: missing <outDir>"); return 1; }
                    PackageSigner.GenerateDevTrust(args[1]);
                    return 0;

                case "genkeys-release":
                    var release = ParseOptions(args.AsSpan(1));
                    PackageSigner.GenerateReleaseTrust(new ReleaseTrustRequest
                    {
                        RootOut = Required(release, "root-out"),
                        IntermediateOut = Required(release, "intermediate-out"),
                        FirstPartyOut = Required(release, "firstparty-out"),
                        PublicOut = Required(release, "public-out"),
                        IntermediateId = Required(release, "intermediate-id"),
                        IntermediateName = Required(release, "intermediate-name"),
                        FirstPartyId = Required(release, "firstparty-id"),
                        FirstPartyName = Required(release, "firstparty-name"),
                    });
                    return 0;

                case "sign-publisher":
                    var po = ParseOptions(args.AsSpan(1));
                    string[] publisherOptions = ["root-public-key", "intermediate-key", "intermediate", "publisher-id",
                        "display-name", "public-key", "out"];
                    // --namespaces is optional: a comma-separated list such as "rdcore." (#4258).
                    var withNamespaces = po.ContainsKey("namespaces") ? 1 : 0;
                    if (args.Length != 1 + (publisherOptions.Length + withNamespaces) * 2
                        || po.Count != publisherOptions.Length + withNamespaces
                        || po.Any(pair => !(publisherOptions.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)
                            || string.Equals(pair.Key, "namespaces", StringComparison.OrdinalIgnoreCase)) || pair.Value.Count != 1))
                        throw new ArgumentException("sign-publisher requires each of --root-public-key, --intermediate-key, --intermediate, --publisher-id, --display-name, --public-key and --out exactly once, and takes --namespaces at most once");
                    PackageSigner.SignPublisher(Required(po, "root-public-key"), Required(po, "intermediate-key"),
                        Required(po, "intermediate"), Required(po, "publisher-id"), Required(po, "display-name"),
                        Required(po, "public-key"), Required(po, "out"),
                        withNamespaces == 1 ? Required(po, "namespaces").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : null);
                    return 0;

                case "sign-revocations":
                    var ro = ParseOptions(args.AsSpan(1));
                    PackageSigner.SignRevocations(Required(ro, "in"), Required(ro, "root-key"), Required(ro, "out"));
                    return 0;

                case "verify-revocations":
                    // Keyless: the release workflow runs it against hexide.io's list (#4537). 2 means the list doesn't verify.
                    var vo = ParseOptions(args.AsSpan(1));
                    string[] verifyOptions = ["json", "sig", "root"];
                    if (args.Length != 1 + verifyOptions.Length * 2 || vo.Count != verifyOptions.Length
                        || vo.Any(pair => !verifyOptions.Contains(pair.Key, StringComparer.OrdinalIgnoreCase) || pair.Value.Count != 1))
                        throw new ArgumentException("verify-revocations requires each of --json, --sig and --root exactly once");
                    return PackageSigner.VerifyRevocations(Required(vo, "json"), Required(vo, "sig"), Required(vo, "root")) ? 0 : 2;

                case "request-publisher":
                    var rq = Options(args, ["publisher-id", "display-name", "github", "fingerprint-url", "out"],
                        ["namespaces", "new-key", "key", "previous-key"]);
                    var made = PublisherRequests.Create(Required(rq, "publisher-id"), Required(rq, "display-name"),
                        rq.TryGetValue("namespaces", out var requested)
                            ? requested[0].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : null,
                        Required(rq, "github"), Required(rq, "fingerprint-url"), Optional(rq, "new-key"), Optional(rq, "key"),
                        Optional(rq, "previous-key"), Required(rq, "out"));
                    PublisherRequests.ReportCreated(made, Required(rq, "out"), Optional(rq, "new-key"));
                    return 0;

                case "verify-request":
                    // Keyless, and hostile input: #5323's Action runs it on a request pasted into an issue, and #5322 runs it
                    // again where the intermediate key is. 2 means refused; 3 means a rotation nobody checked against the ledger.
                    var vq = Options(args, ["request"], ["ledger", "github"]);
                    return PublisherRequests.Report(PublisherRequests.Verify(Required(vq, "request"), Optional(vq, "ledger"),
                        Optional(vq, "github")));

                case "pack":
                    var o = ParseOptions(args.AsSpan(1));
                    PackageSigner.Pack(new PackRequest
                    {
                        SrcDir = Required(o, "src"),
                        OutDir = Required(o, "out"),
                        Id = Required(o, "id"),
                        Title = Required(o, "title"),
                        Description = o.GetValueOrDefault("description", [""])[0],
                        Version = Required(o, "version"),
                        Author = o.GetValueOrDefault("author", [""])[0],
                        Entry = Required(o, "entry"),
                        TrustDir = Required(o, "trust"),
                        KeyFile = o.TryGetValue("key", out var keyFile) && keyFile.Count > 0 ? keyFile[0] : null,
                        Include = o.GetValueOrDefault("include", []),
                        Capabilities = o.GetValueOrDefault("capability", []),
                        MinHexideVersion = o.TryGetValue("min-hexide-version", out var minimum) && minimum.Count > 0 ? minimum[0] : null,
                        Logo = o.TryGetValue("logo", out var logo) && logo.Count > 0 ? logo[0] : null,
                    });
                    return 0;

                default:
                    Console.Error.WriteLine($"unknown command '{args[0]}'");
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    // Each required option exactly once and each optional one at most once, with nothing else: a repeated or unknown
    // option is refused rather than silently ignored, as sign-publisher's are.
    private static Dictionary<string, List<string>> Options(string[] args, string[] required, string[] optional)
    {
        var options = ParseOptions(args.AsSpan(1));
        var known = required.Concat(optional).ToArray();
        if (options.Count * 2 != args.Length - 1
            || options.Any(pair => !known.Contains(pair.Key, StringComparer.OrdinalIgnoreCase) || pair.Value.Count != 1)
            || required.Any(name => !options.ContainsKey(name)))
            throw new ArgumentException($"{args[0]} requires {string.Join(", ", required.Select(name => "--" + name))} exactly "
                + $"once, and takes {string.Join(", ", optional.Select(name => "--" + name))} at most once");
        return options;
    }

    private static string? Optional(Dictionary<string, List<string>> o, string key) =>
        o.TryGetValue(key, out var v) && v.Count > 0 ? v[0] : null;

    // Collects --key value pairs; repeated keys accumulate (e.g. --include a --include b).
    private static Dictionary<string, List<string>> ParseOptions(ReadOnlySpan<string> a)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < a.Length; i++)
        {
            if (!a[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= a.Length) continue;
            var key = a[i][2..];
            if (!map.TryGetValue(key, out var list)) map[key] = list = [];
            list.Add(a[i + 1]);
            i++;
        }
        return map;
    }

    private static string Required(Dictionary<string, List<string>> o, string key) =>
        o.TryGetValue(key, out var v) && v.Count > 0
            ? v[0]
            : throw new ArgumentException($"missing required --{key}");
}
