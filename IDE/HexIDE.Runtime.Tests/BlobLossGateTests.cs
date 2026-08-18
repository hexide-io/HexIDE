using System.Collections.Generic;
using System.Linq;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Guards the binary half of the refusal gate (#84 phase 1).
///
/// A .frm points at its companion binary by offset — <c>Picture = "Form1.frx":0000</c>. If the property
/// carrying that reference is one HexIDE does not model, the reference is dropped on save while the .frx
/// itself survives untouched, because a separate guard refuses to rewrite a companion it would shrink. The
/// result is a save that looks successful and silently removes a control's picture, with the bytes still
/// sitting on disk unreferenced.
///
/// There are two ways it goes missing and only one announces itself, which is why the gate needs both a
/// flag and a count:
///   - an UNMODELLED CONTROL carrying an .frx reference — flagged during the walk
///   - a MODELLED control carrying a property that is named but whose CLR type is not byte[] — dropped
///     with no diagnostic at all. ODBC Log In's ComboBox List is exactly this.
/// </summary>
public class BlobLossGateTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public List<string> Errors { get; } = new();
        public void LogError(string error) => Errors.Add(error);
    }

    private static FormDefinition Load(string source, IReadOnlyDictionary<int, byte[]>? blobs = null) =>
        new FormDeserializer().Deserialize(
            new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink(), blobs)!;

    /// <summary>One blob at offset 0, so a form referencing ":0000" can capture it.</summary>
    private static Dictionary<int, byte[]> OneBlob() => new() { [0] = new byte[] { 1, 2, 3, 4 } };

    private const string ModelledPictureForm =
        "VERSION 5.00\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Begin VB.PictureBox Picture1 \r\n" +
        "      Picture         =   \"Form1.frx\":0000\r\n" +
        "   End\r\n" +
        "End\r\nAttribute VB_Name = \"Form1\"\r\n";

    private const string UnmodelledOwnerForm =
        "VERSION 5.00\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Begin MSComctlLib.ImageList ImageList1 \r\n" +
        "      Picture         =   \"Form1.frx\":0000\r\n" +
        "   End\r\n" +
        "End\r\nAttribute VB_Name = \"Form1\"\r\n";

    private const string UnmodelledPropertyForm =
        "VERSION 5.00\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Begin VB.Label Label1 \r\n" +
        "      Picture         =   \"Form1.frx\":0000\r\n" +
        "   End\r\n" +
        "End\r\nAttribute VB_Name = \"Form1\"\r\n";

    [Fact]
    public void AModelledPropertyCarryingABlob_DoesNotHoldTheFormReadOnly()
    {
        var form = Load(ModelledPictureForm, OneBlob());

        // PictureBox.Picture IS modelled, so the blob reaches the model and the save can re-emit it.
        form.UnfaithfulSaveCauses.Should().NotHaveFlag(UnfaithfulSaveCause.UnreproducibleBinaryContent);
        form.CanSaveFaithfully.Should().BeTrue();
    }

    [Fact]
    public void AnUnmodelledControlCarryingABlob_HoldsTheFormReadOnly()
    {
        var form = Load(UnmodelledOwnerForm, OneBlob());

        // The whole control is preserved as raw text, so its blob is never collected.
        form.UnfaithfulSaveCauses.Should().HaveFlag(UnfaithfulSaveCause.UnreproducibleBinaryContent);
        form.CanSaveFaithfully.Should().BeFalse();
    }

    [Fact]
    public void AModelledControlWithAnUnmodelledBlobProperty_HoldsTheFormReadOnly()
    {
        var form = Load(UnmodelledPropertyForm, OneBlob());

        // Label is modelled; Label.Picture is not. Nothing flags this — the count is what catches it, and
        // this is the case the old gate was blind to.
        form.UnfaithfulSaveCauses.Should().HaveFlag(UnfaithfulSaveCause.UnreproducibleBinaryContent);
    }

    [Fact]
    public void AFormWithNoCompanionAtAll_IsUnaffected()
    {
        var form = Load(
            "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   Begin VB.TextBox Text1 \r\n   End\r\nEnd\r\n" +
            "Attribute VB_Name = \"Form1\"\r\n");

        form.CanSaveFaithfully.Should().BeTrue();
    }

    [Fact]
    public void CausesAccumulate_RatherThanOverwritingEachOther()
    {
        const string both =
            "VERSION 5.00\r\n" +
            "Begin VB.Form Form1 \r\n" +
            "   Begin VB.Frame Frame1 \r\n" +
            "      Begin VB.Label Label1 \r\n" +
            "         Picture         =   \"Form1.frx\":0000\r\n" +
            "      End\r\n   End\r\n" +
            "End\r\nAttribute VB_Name = \"Form1\"\r\n";

        var form = Load(both, OneBlob());

        // Splash Screen and three others are unreproducible in both ways at once. If one cause overwrote
        // the other, fixing containers would appear to free a form that is still losing a picture.
        form.UnfaithfulSaveCauses.Should().Be(
            UnfaithfulSaveCause.NestedContainers | UnfaithfulSaveCause.UnreproducibleBinaryContent);
    }
}
