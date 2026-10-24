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
        CompilerProcess.ExecutableIn(@"C:\tb", CompilerArchitecture.Win64).Should().Be(Path.Combine(@"C:\tb", "bin", "twinBASIC_win64.exe"));
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
