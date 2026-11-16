using System.Globalization;
using System.Reflection;
using System.Text;
using HexIDE.Desktop;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Tests.Infrastructure;

public class DistributionStartupLocalizationTests
{
    private const string Minimal = """{"schemaVersion":1,"id":"acme-ide","name":"Acme IDE"}""";

    public static TheoryData<string, string> DescriptorReasons => new()
    {
        { "[]", "it must be a JSON object" },
        { Minimal.Replace("\"schemaVersion\":1", "\"schemaVersion\":2"), "'schemaVersion' must be 1" },
        { Minimal.Replace("acme-ide", "CON"), "'id' is 'CON', which is not usable as a folder name on every platform: lower-case letters, digits and inner hyphens, up to 64 characters, and not a name Windows reserves" },
        { Minimal.Replace("acme-ide", "hexide"), "'id' may not be 'hexide': it would share HexIDE's own settings, logs and recovery files" },
        { Minimal.Replace("Acme IDE", "Acme_{IDE}"), "'name' may not contain '_', '{' or '}', which mean something in the IDE's own strings" },
        { Minimal.Replace("Acme IDE", " Acme IDE"), "'name' has spaces at its start or end" },
        { With("\"credits\":false"), "'credits' must be an array of strings" },
        { With("\"credits\":[" + string.Join(',', Enumerable.Repeat("\"credit\"", 33)) + "]"), "'credits' has more than 32 entries" },
        { With("\"tagline\":false"), "'tagline' must be a string or a text/translations object" },
        { With("\"tagline\":{\"text\":\"hello\",\"translations\":[]}"), "'tagline.translations' must be an object" },
        { With("\"tagline\":{\"text\":\"hello\",\"translations\":{\"BAD\":\"hello\"}}"), "'tagline.translations' has an invalid language id 'BAD'" },
        { With("\"tagline\":{\"text\":\"hello\",\"translations\":{\"fr\":false}}"), "'tagline.translations.fr' must be a string" },
        { With("\"links\":[]"), "'links' must be an object" },
        { With("\"links\":{\"home\":false}"), "'links.home' must be a string" },
        { With("\"links\":{\"home\":\"http://example.test\"}"), "'links.home' must be an absolute https URL with no user name" },
        { With("\"logo\":\"logo.jpg\""), "'logo' must name a .png file" },
        { With("\"logo\":\"logo.png\""), "'logo' names 'logo.png', which is not a PNG image" },
        { With("\"defaultProfile\":\"../profile\""), "'defaultProfile' is '../profile'; it must be a profile's name, or a folder inside the installation written with '/'" },
        { With("\"unknown\":false"), "the descriptor has a member this version does not know, 'unknown'. Known members: schemaVersion, id, name, tagline, copyright, credits, links, logo, defaultProfile" },
        { With("\"tagline\":{\"unknown\":false}"), "tagline has a member this version does not know, 'unknown'. Known members: text, translations" },
        { Minimal.Replace("\"name\":\"Acme IDE\"", "\"name\":\"\""), "'name' is required" },
        { Minimal.Replace("\"Acme IDE\"", "false"), "'name' must be a string" },
        { Minimal.Replace("Acme IDE", "   "), "'name' is empty" },
        { Minimal.Replace("Acme IDE", new string('a', 65)), "'name' is longer than 64 characters" },
        { Minimal.Replace("Acme IDE", "Acme\\tIDE"), "'name' contains a control or format character" },
    };

    private static string With(string member) => Minimal[..^1] + "," + member + "}";

    [Theory]
    [MemberData(nameof(DescriptorReasons))]
    public void DescriptorReasonsAreFrenchForPeopleAndRemainEnglishForPrograms(string json, string reason)
    {
        Action parse = () => DistributionDescriptor.Parse(Encoding.UTF8.GetBytes(json), "brand.json", _ => []);
        parse.Should().Throw<DistributionDescriptorException>().WithMessage("brand.json: " + reason + ".");
        WithInstallation(directory =>
        {
            File.WriteAllText(Path.Combine(directory, "brand.json"), json);
            File.WriteAllBytes(Path.Combine(directory, "logo.png"), []);
            var output = Refusal(directory);
            output.Should().StartWith("L’identité de cette installation ne peut pas être utilisée");
            output.Should().Contain(Path.Combine(directory, "brand.json"));
            output.Should().NotContain(reason + ".", "the person must see a translated reason");
            foreach (System.Text.RegularExpressions.Match member in System.Text.RegularExpressions.Regex.Matches(reason, @"(?<!\w)'([^']*)'"))
                output.Should().Contain("« " + member.Groups[1].Value + " »", "quoted field names and values are facts, not translated prose");
        });
    }

    [Fact]
    public void InvalidJsonReasonIsLocalized()
    {
        WithInstallation(directory =>
        {
            File.WriteAllText(Path.Combine(directory, "brand.json"), "{");
            Refusal(directory).Should().Contain("JSON").And.NotContain("it is not valid JSON, or repeats a member");
        });
    }

    [Fact]
    public void UnreadableAssetReasonIsLocalizedAndKeepsThePath()
    {
        WithInstallation(directory =>
        {
            File.WriteAllText(Path.Combine(directory, "brand.json"), With("\"logo\":\"missing.png\""));
            Refusal(directory).Should().Contain("missing.png").And.Contain("dossier d’installation")
                .And.NotContain("could not be read as a regular file");
        });
    }

    [Fact]
    public void LogoDimensionsReasonIsLocalizedAndKeepsTheLimits()
    {
        WithInstallation(directory =>
        {
            var png = new byte[24];
            new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }.CopyTo(png, 0);
            "IHDR"u8.CopyTo(png.AsSpan(12));
            File.WriteAllBytes(Path.Combine(directory, "logo.png"), png);
            File.WriteAllText(Path.Combine(directory, "brand.json"), With("\"logo\":\"logo.png\""));
            Refusal(directory).Should().Contain("logo").And.Contain("0x0").And.Contain("1024")
                .And.NotContain("each side must be from");
        });
    }

    [Fact]
    public void IncompletePngReasonIsLocalizedAndRemainsEnglishForPrograms()
    {
        var png = new byte[24];
        new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }.CopyTo(png, 0);
        "IHDR"u8.CopyTo(png.AsSpan(12));
        png[19] = png[23] = 1;
        var json = With("\"logo\":\"logo.png\"");
        Action parse = () => DistributionDescriptor.Parse(Encoding.UTF8.GetBytes(json), "brand.json", _ => png);
        parse.Should().Throw<DistributionDescriptorException>()
            .WithMessage("brand.json: 'logo' names 'logo.png', which is not a PNG image.");
        WithInstallation(directory =>
        {
            File.WriteAllText(Path.Combine(directory, "brand.json"), json);
            File.WriteAllBytes(Path.Combine(directory, "logo.png"), png);
            Refusal(directory).Should().Contain("« logo »").And.Contain("« logo.png »")
                .And.Contain("image PNG").And.NotContain("which is not a PNG image");
        });
    }

    [Theory]
    [InlineData("--user-data-dir", null, "répertoire")]
    [InlineData("--server-port", "bad-port", "port")]
    [InlineData("--session", null, "session")]
    [InlineData("--ansi-code-page", "bad-code-page", "page de codes")]
    public void FlagRefusalsUseFrenchAndKeepTheFlagAndProduct(string flag, string? value, string detail)
        => CheckFlagRefusal(flag, value, detail);

    public static bool OnWindows => OperatingSystem.IsWindows();

    [Fact(Skip = "Windows accepts the DPI-unaware flag", SkipWhen = nameof(OnWindows))]
    public void DpiRefusalUsesFrenchOnOtherPlatforms() => CheckFlagRefusal("--dpi-unaware", null, "Windows");

    private static void CheckFlagRefusal(string flag, string? value, string detail)
    {
        var properties = typeof(ServerOptions).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.SetMethod != null).ToDictionary(p => p, p => p.GetValue(null));
        var previous = LocalizationService.OsUICulture;
        var previousExitCode = Environment.ExitCode;
        try
        {
            foreach (var property in properties.Keys)
                property.SetValue(null, property.PropertyType.IsValueType ? Activator.CreateInstance(property.PropertyType) : null);
            LocalizationService.OsUICulture = CultureInfo.GetCultureInfo("fr-FR");
            ServerOptions.ParseArgs(value == null ? [flag] : [flag, value], Environment.CurrentDirectory);
            var output = "";
            HexIDE.Desktop.Program.RefuseInvalidArguments(text => output += text).Should().BeTrue();
            output.Should().Contain(flag).And.Contain(Distribution.DisplayName).And.Contain(detail)
                .And.Contain("n’a pas démarré");
            if (value != null) output.Should().Contain(value);
            if (flag == "--server-port") output.Should().Contain("1").And.Contain("65535");
            if (flag == "--ansi-code-page") output.Should().Contain("932");
            if (flag == "--dpi-unaware") output.Should().Contain("AVALONIA_GLOBAL_SCALE_FACTOR");
            Environment.ExitCode.Should().Be(2);
        }
        finally
        {
            foreach (var (property, valueBefore) in properties) property.SetValue(null, valueBefore);
            LocalizationService.OsUICulture = previous;
            Environment.ExitCode = previousExitCode;
        }
    }

    private static string Refusal(string directory)
    {
        var previous = LocalizationService.OsUICulture;
        var previousExitCode = Environment.ExitCode;
        try
        {
            LocalizationService.OsUICulture = CultureInfo.GetCultureInfo("fr-FR");
            var output = "";
            HexIDE.Desktop.Program.RunDistributionGate([], directory, directory, _ => { },
                render => output += render(new ConsoleTarget(false, int.MaxValue)), text => output += text,
                () => throw new InvalidOperationException("A broken installation must not start"))
                .Should().Be(2);
            return output;
        }
        finally
        {
            LocalizationService.OsUICulture = previous;
            Environment.ExitCode = previousExitCode;
        }
    }

    [Fact]
    public void BrokenInstallationHelpPrintsFrenchRefusalBeforeHelp()
    {
        var previous = LocalizationService.OsUICulture;
        var previousHelp = ServerOptions.HelpRequested;
        try
        {
            LocalizationService.OsUICulture = CultureInfo.GetCultureInfo("fr-FR");
            WithInstallation(directory =>
            {
                File.WriteAllText(Path.Combine(directory, "brand.json"), "[]");
                var output = new List<string>();
                HexIDE.Desktop.Program.RunDistributionGate(["--help"], directory, directory, _ => { },
                    render => output.Add(render(new ConsoleTarget(false, int.MaxValue))),
                    _ => throw new InvalidOperationException("Help must write the refusal to the console"),
                    () => throw new InvalidOperationException("Help must not start"))
                    .Should().Be(2);
                output.Should().HaveCount(2);
                output[0].Should().StartWith("L’identité de cette installation ne peut pas être utilisée")
                    .And.Contain(Path.Combine(directory, "brand.json"))
                    .And.Contain("objet JSON");
                output[1].Should().Be(ServerOptions.HelpText(new ConsoleTarget(false, int.MaxValue)));
            });
        }
        finally
        {
            LocalizationService.OsUICulture = previous;
            typeof(ServerOptions).GetProperty(nameof(ServerOptions.HelpRequested))!.SetValue(null, previousHelp);
        }
    }

    private static void WithInstallation(Action<string> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-startup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { test(directory); }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void NativeRefusalCaptionUsesTheOperatingSystemLanguage()
    {
        var source = File.ReadAllText(Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Desktop", "Program.cs"))
            .ReplaceLineEndings("\n");
        source.Should().Contain("localization.GetStringFrom(\"system\", \"Str.Startup.CannotStart\")");
        var localization = new LocalizationService();
        localization.GetStringFrom("fr", "Str.Startup.CannotStart").Should().Be("Démarrage impossible");
    }
}
