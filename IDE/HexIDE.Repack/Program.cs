using HexIDE.Repack;

const string usage = "usage: HexIDE.Repack --input <published stock build> --brand <brand.json> --output <new directory>";

string? input = null, brand = null, output = null;
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

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("This version repacks Windows builds and has to run on Windows, because it rewrites the executable through Windows' own resource functions.");
    return 1;
}

try
{
    var result = WindowsRepacker.Run(input, brand, output);
    Console.WriteLine($"Wrote {output}");
    Console.WriteLine($"  app\\{result.ExecutableName}: its name, icon and version details are now {result.ProductName}'s.");
    Console.WriteLine($"  installer\\{InstallerInputs.ScriptName}: compile this with Inno Setup's ISCC to make the setup program.");
    Console.WriteLine("  The helper programs beside it keep HexIDE's names, and every other file is as published.");
    Console.WriteLine();
    if (result.RemovedSignature)
        Console.WriteLine($"Removed the signature from {result.ExecutableName}, because rewriting the file invalidated it.");
    Console.WriteLine($"This output is not signed. Sign {result.ExecutableName} and the setup program before you ship them.");
    return 0;
}
catch (RepackException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
