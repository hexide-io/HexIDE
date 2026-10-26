using System.Text;

namespace HexIDE.TwinBasic.Tests;

public sealed class CompilerProcessTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private const string Guid = "{D27FF8F5-412D-4972-8062-AF65AC36B022}";

    private static MemoryStream Record(uint port, string guid, params byte[] after) =>
        new([.. BitConverter.GetBytes(port), .. Encoding.ASCII.GetBytes(guid), .. after]);

    [Fact]
    public async Task The_endpoint_record_is_a_little_endian_port_then_a_braced_guid()
    {
        // The bytes BETA 1024 wrote, port 49226 = 0xC04A.
        var (port, guid) = await CompilerProcess.ReadEndpointAsync(Record(0xC04A, Guid), Token);

        port.Should().Be(49226);
        guid.Should().Be(Guid);
    }

    [Fact]
    public async Task Output_after_the_record_is_not_read_as_part_of_it()
    {
        var output = Record(1024, Guid, 0x41, 0x42);

        await CompilerProcess.ReadEndpointAsync(output, Token);

        output.Position.Should().Be(CompilerProcess.EndpointBytes);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(65536u)]
    public async Task A_port_outside_the_range_is_refused(uint port)
    {
        var read = () => CompilerProcess.ReadEndpointAsync(Record(port, Guid), Token);

        await read.Should().ThrowExactlyAsync<InvalidDataException>().WithMessage("*invalid port*");
    }

    [Theory]
    [InlineData("D27FF8F5-412D-4972-8062-AF65AC36B022  ")]
    [InlineData("{D27FF8F5-412D-4972-8062-AF65AC36B02Z}")]
    public async Task A_guid_that_is_not_braced_hex_is_refused_without_repeating_it(string guid)
    {
        var read = () => CompilerProcess.ReadEndpointAsync(Record(1024, guid), Token);

        (await read.Should().ThrowExactlyAsync<InvalidDataException>()).Which.Message.Should().NotContain("D27FF8F5");
    }

    [Fact]
    public async Task A_record_cut_short_is_the_end_of_the_stream()
    {
        var read = () => CompilerProcess.ReadEndpointAsync(new MemoryStream([0x4A, 0xC0, 0, 0, 0x7B]), Token);

        await read.Should().ThrowExactlyAsync<EndOfStreamException>();
    }

    [Fact]
    public void The_executable_is_under_the_install_folders_bin()
    {
        CompilerProcess.ExecutableIn(@"C:\tb").Should().Be(Path.Combine(@"C:\tb", "bin", "twinBASIC_win32.exe"));
    }

    // The six an install holds (BETA 1024): there is no 64-bit _LAA, since a 64-bit process needs no large-address flag.
    [Theory]
    [InlineData(CompilerArchitecture.Win32, true, false, "twinBASIC_win32.exe")]
    [InlineData(CompilerArchitecture.Win32, false, false, "twinBASIC_win32_noDEP.exe")]
    [InlineData(CompilerArchitecture.Win32, true, true, "twinBASIC_win32_LAA.exe")]
    [InlineData(CompilerArchitecture.Win32, false, true, "twinBASIC_win32_noDEP_LAA.exe")]
    [InlineData(CompilerArchitecture.Win64, true, false, "twinBASIC_win64.exe")]
    [InlineData(CompilerArchitecture.Win64, false, true, "twinBASIC_win64_noDEP.exe")]
    public void The_stub_follows_the_projects_architecture_DEP_and_LAA(CompilerArchitecture architecture, bool dep, bool laa, string name)
    {
        new CompilerStub(architecture, dep, laa).FileName.Should().Be(name);
    }

    [Fact]
    public void The_root_endpoint_is_the_port_and_key_on_loopback()
    {
        // The key goes in exactly as the record held it, braces and case kept (BETA 1024 refused nothing else it was given).
        CompilerProcess.RootEndpointFor(49226, Guid).Should().Be(new Uri($"ws://127.0.0.1:49226/{Guid}/root"));
    }

    private static TwinBasicContainer ProjectWithSettings(string? settings) => new(1, new TwinBasicDirectory("", 0, 0, 0,
        settings is null ? [] : [new TwinBasicFile("Settings", 0, 0, 4, [.. Encoding.UTF8.GetBytes(settings)], [])]));

    [Theory]
    [InlineData("{\"project.depAware\": true}", "twinBASIC_win32.exe")]
    [InlineData("{\"project.depAware\": false}", "twinBASIC_win32_noDEP.exe")]
    [InlineData("{\"project.name\": \"x\"}", "twinBASIC_win32_noDEP.exe")]
    [InlineData("{\"project.depAware\": true, \"project.largeAddressAware\": true}", "twinBASIC_win32_LAA.exe")]
    [InlineData("{\"project.depAware\": \"true\"}", "twinBASIC_win32_noDEP.exe")]
    public void A_projects_stub_follows_its_settings(string settings, string name)
    {
        CompilerStub.ForProject(ProjectWithSettings(settings)).FileName.Should().Be(name);
    }

    [Fact]
    public void A_project_with_no_settings_needs_the_stub_without_DEP()
    {
        CompilerStub.ForProject(ProjectWithSettings(null)).FileName.Should().Be("twinBASIC_win32_noDEP.exe");
    }

    [Fact]
    public void The_package_list_is_the_installs_package_folder_names_in_ordinal_order()
    {
        var install = Directory.CreateTempSubdirectory("hexide-tb-install-");
        try
        {
            // No two names differ only in case: Windows and macOS's default APFS would make them one folder.
            // Ordinal order puts the capital first, where a culture-aware sort would not.
            foreach (var name in new[] { "c", "B", "a" }) Directory.CreateDirectory(Path.Combine(install.FullName, "packages", name));
            File.WriteAllText(Path.Combine(install.FullName, "packages", "not-a-folder"), "");

            CompilerProcess.PackageNamesIn(install.FullName).Should().Equal("B", "a", "c");
        }
        finally { install.Delete(recursive: true); }
    }
}

public sealed class CompilerLanguagePackTests
{
    [Fact]
    public void Entries_end_at_a_semicolon_before_whitespace_and_comments_are_ignored()
    {
        var pack = CompilerLanguagePack.Read(
            "/* DO NOT EDIT; really */\r\nFriendlyName:\t\t\"English (British English)\";\r\nA: \"x;y\"; B:\"z\";\tC: \"w\";\n");

        pack.Should().Equal(new Dictionary<string, string>
        {
            ["FriendlyName"] = "English (British English)", ["A"] = "x;y", ["B"] = "z", ["C"] = "w",
        });
    }

    [Fact]
    public void Adjacent_quoted_runs_join_and_a_doubled_quote_is_one_quote()
    {
        CompilerLanguagePack.Read("A: \"say \"\"hi\"\"\" \"there\";")["A"].Should().Be("say \"hi\"there");
    }

    [Fact]
    public void A_carriage_return_between_runs_is_kept_as_the_IDE_keeps_it()
    {
        CompilerLanguagePack.Read("A: \"one\"\r\n   \"two\";\n")["A"].Should().Be("one\rtwo");
    }

    [Fact]
    public void The_pack_becomes_a_JSON_object_of_its_entries()
    {
        var data = CompilerLanguagePack.ToLanguageData(new Dictionary<string, string> { ["A"] = "é" });

        data.Should().NotBeNull();
    }
}
