using HexIDE.Repack;

const string usage = "usage: HexIDE.Repack --input <published stock build> --brand <brand.json> --output <new directory> [--include <folder of your own files>]";

string? input = null, brand = null, output = null, include = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--input" when i + 1 < args.Length:
            input = args[++i];
            break;
        case "--brand" when i + 1 < args.Length:
            brand = args[++i];
            break;
        case "--include" when i + 1 < args.Length:
            include = args[++i];
            break;
        case "--output" when i + 1 < args.Length:
            output = args[++i];
            break;
        default:
            Console.Error.WriteLine(usage);
            return 2;
    }
}

if (input is null || brand is null || output is null)
{
    Console.Error.WriteLine(usage);
    return 2;
}

try
{
    var target = RepackTarget.For(input);
    var result = Repacker.Run(target, input, brand, output, include);
    Console.WriteLine($"Wrote {output}");
    foreach (var line in target.Describe(result))
        Console.WriteLine(line);
    if (result.IncludeDirectory is { } source)
        Console.WriteLine($"Included {result.IncludedFiles} file(s) from {source}");
    return 0;
}
catch (RepackException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
