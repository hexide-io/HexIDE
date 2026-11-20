using System.Security.Cryptography;

namespace HexIDE.TwinBasic.Tests;

/// <summary>
/// Containers the twinBASIC IDE itself saved, BETA 1044 (#5139). Fixtures/README.md says how each was made, and the
/// measurements they show are on #5140.
/// </summary>
public sealed class IdeSavedFixtureTests
{
    [Theory]
    [InlineData("tb1044-01-new-project.twinproj", "e5b92760d8cb4bdbca802834fbaed475ec34d14dbf70d7c279625a7e8309e7fa")]
    [InlineData("tb1044-02-reopened.twinproj", "650a2f4b99a3f94d7b7a8750822af9b61c3c1f169cb9934768c36c87c165d9f6")]
    [InlineData("tb1044-03-reopened-twice.twinproj", "343d4fe2cdd65fec9fc7ef7c3b049c1a6c02862eae32680109ffc0d500338f0a")]
    [InlineData("tb1044-04-one-character.twinproj", "bfb645049ede106e199a2791bdce8a7853d4dd1a6e4a6075b3311e39836c9286")]
    [InlineData("tb1044-05-module-added.twinproj", "96ceb9c7b03b814d9cb3ebb4e5603e5953bc0953828398e4de4b46e73e405573")]
    [InlineData("tb1044-06-module-renamed.twinproj", "39ca60aa42a8d32a036f4a21be776117a641e06d60bf9a670a78884c45d27259")]
    [InlineData("tb1044-07-folder-added.twinproj", "43f0b543bf4b1452ae157c6b6a5422ebf5e14b10805b7d7e769076cdc1126595")]
    [InlineData("tb1044-08-form-added.twinproj", "4e548be35de9b4e74b3aff0aa250e1d95550d72ce11adf7a5dbe1c0a84cfe56c")]
    [InlineData("tb1044-09-setting-applied.twinproj", "156f34d072321099e434ce4ca37b46c0200cc173d7e296f3b33024f7ce9caf42")]
    [InlineData("tb1044-10-setting-then-saved.twinproj", "455f7c4d812bfe5cc128441075f7421b292e9eccb4a826ba3d22e59db32a9f01")]
    public void Each_IDE_save_is_unchanged_and_reads_to_its_last_byte(string fixture, string hash)
    {
        var bytes = Bytes(fixture);
        Convert.ToHexStringLower(SHA256.HashData(bytes)).Should().Be(hash);
        var container = Read(bytes); // the reader refuses trailing bytes, so this reads every byte
        container.Version.Should().Be(1);
        container.Root.Name.Should().Be("NewProject");
    }

    [Fact]
    public void Reopening_reverses_every_folders_children_and_adds_two_to_every_files_revision()
    {
        // #5140: the IDE's save of an unchanged project after a close and reopen. Content is untouched.
        Compare(Read(Bytes("tb1044-01-new-project.twinproj")).Root, Read(Bytes("tb1044-02-reopened.twinproj")).Root);
        // And a second reopen reverses the order back.
        Read(Bytes("tb1044-03-reopened-twice.twinproj")).Root.Children.Select(c => c.Name)
            .Should().Equal(Read(Bytes("tb1044-01-new-project.twinproj")).Root.Children.Select(c => c.Name));
    }

    [Fact]
    public void An_added_module_is_written_first_in_its_folder_and_keeps_its_place_when_renamed()
    {
        Sources("tb1044-05-module-added.twinproj").Should().Equal("MyModule.twin", "Form1.tbform", "Form1.twin");
        Sources("tb1044-06-module-renamed.twinproj").Should().Equal("Helpers.twin", "Form1.tbform", "Form1.twin");
    }

    private static void Compare(TwinBasicDirectory first, TwinBasicDirectory reopened)
    {
        reopened.Children.Select(c => c.Name).Should().Equal(first.Children.Select(c => c.Name).Reverse());
        reopened.Revision.Should().Be(first.Revision);
        foreach (var before in first.Children)
        {
            var after = reopened.Children.Single(c => c.Name == before.Name);
            switch (before, after)
            {
                case (TwinBasicDirectory a, TwinBasicDirectory b):
                    Compare(a, b);
                    break;
                case (TwinBasicFile a, TwinBasicFile b):
                    b.Revision.Should().Be(a.Revision + 2, because: $"{a.Name}'s revision rises by 2");
                    b.Content.Should().Equal(a.Content);
                    break;
                default:
                    throw new InvalidOperationException($"{before.Name} changed kind");
            }
        }
    }

    private static IEnumerable<string> Sources(string fixture) =>
        Read(Bytes(fixture)).Root.Children.OfType<TwinBasicDirectory>().Single(d => d.Name == "Sources")
            .Children.Select(c => c.Name);

    private static byte[] Bytes(string fixture) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));

    private static TwinBasicContainer Read(byte[] bytes)
    {
        using var input = new MemoryStream(bytes, writable: false);
        return TwinBasicContainerReader.Read(input, cancellationToken: TestContext.Current.CancellationToken);
    }
}
