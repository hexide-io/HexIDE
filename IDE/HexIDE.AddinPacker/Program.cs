using HexIDE.AddinPacker;

// Tiny CLI: `genkeys <outDir>` | `pack --src <dir> --out <dir> --id <id> --title <t> --version <v>
//           --entry <file.dll> --trust <trustDir> [--author <a>] [--desc <d>] [--include <file> ...]
//           [--logo <file.png>]`
if (args.Length == 0)
{
    Console.Error.WriteLine("usage: genkeys <outDir> | pack --src .. --out .. --id .. --title .. --version .. --entry .. --trust ..");
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

        case "sign-revocations":
            var ro = ParseOptions(args.AsSpan(1));
            PackageSigner.SignRevocations(Required(ro, "in"), Required(ro, "root-key"), Required(ro, "out"));
            return 0;

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
                Include = o.GetValueOrDefault("include", []),
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

// Collects --key value pairs; repeated keys accumulate (e.g. --include a --include b).
static Dictionary<string, List<string>> ParseOptions(ReadOnlySpan<string> a)
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

static string Required(Dictionary<string, List<string>> o, string key) =>
    o.TryGetValue(key, out var v) && v.Count > 0
        ? v[0]
        : throw new ArgumentException($"missing required --{key}");
