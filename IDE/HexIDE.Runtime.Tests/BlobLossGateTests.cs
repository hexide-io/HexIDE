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
/// There are three ways it goes missing and only one announces itself, which is why the gate needs both a
/// flag and a count:
///   - an UNMODELLED CONTROL carrying an .frx reference — flagged during the walk
///   - a MODELLED control carrying a property that is named but whose CLR type is not byte[] — dropped
///     with no diagnostic at all. ODBC Log In's ComboBox List is exactly this.
///   - the COMPANION ITSELF absent or truncated, so a citation on an entirely ordinary property cannot be
///     honoured. Nothing about the form is unusual; the file beside it is what is missing (#146).
///
/// The count is taken against what the .frm CITES, not against what the companion yielded. Counting the
/// companion cannot see that third case — with no companion there is nothing to count, and with a truncated
/// one both sides fall to the same lower number and agree. The citations live in the .frm, so they are
/// there to be counted whatever became of the file beside it.
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
        // A ListBox is not a container, so the Label nested inside it would be re-parented onto the form on
        // save — and it carries a blob-backed property the writer cannot re-emit. Two independent causes on
        // one form.
        //
        // The fixture used to be a Frame, which was the point when a Frame held a form read-only. Frames no
        // longer do, so keeping it would have made this assert only the binary half while still claiming to
        // prove the two accumulate.
        const string both =
            "VERSION 5.00\r\n" +
            "Begin VB.Form Form1 \r\n" +
            "   Begin VB.ListBox List1 \r\n" +
            "      Begin VB.Label Label1 \r\n" +
            "         Picture         =   \"Form1.frx\":0000\r\n" +
            "      End\r\n   End\r\n" +
            "End\r\nAttribute VB_Name = \"Form1\"\r\n";

        var form = Load(both, OneBlob());

        // If one cause overwrote the other, fixing containers would appear to free a form that is still
        // losing a picture.
        form.UnfaithfulSaveCauses.Should().Be(
            UnfaithfulSaveCause.NestedContainers | UnfaithfulSaveCause.UnreproducibleBinaryContent);
    }

    // ── A companion that is not there (#146) ─────────────────────────────────────────────────────────
    //
    // The gate used to count what the COMPANION yielded — `frxBlobs?.Count ?? 0` — so with no companion the
    // test became `capturedBlobs.Count < 0`, always false. A .frm separated from its .frx was never
    // flagged, and an ordinary Ctrl+S at its ORIGINAL path rewrote it with every citation dropped. The
    // damaged file then reopened as faithful, because the citations were the only thing that would have
    // flagged it.
    //
    // Counting what the SOURCE cites closes it: the citations live in the .frm, so the check no longer
    // depends on the file that may be the one missing.
    //
    // `AFormWithNoCompanionAtAll_IsUnaffected` above is the other side of this and still holds — a form
    // that cites NOTHING is fine without a companion. These cover a form that cites one it does not have.

    [Fact]
    public void AModelledPictureWhoseCompanionIsMissing_HoldsTheFormReadOnly()
    {
        // The near-universal shape: Picture/Icon on a control HexIDE models perfectly well. Nothing is
        // wrong with the form — the companion is simply absent, and the citation cannot be honoured.
        var form = Load(ModelledPictureForm);

        form.CanSaveFaithfully.Should().BeFalse(
            "the form cites a blob it could not load, so saving would drop the citation");
        form.UnfaithfulSaveCauses.Should().HaveFlag(UnfaithfulSaveCause.UnreproducibleBinaryContent);
    }

    [Fact]
    public void AnUnmodelledOwnerWhoseCompanionIsMissing_HoldsTheFormReadOnly()
    {
        var form = Load(UnmodelledOwnerForm);

        form.CanSaveFaithfully.Should().BeFalse();
    }

    [Fact]
    public void AnUnmodelledPropertyWhoseCompanionIsMissing_HoldsTheFormReadOnly()
    {
        var form = Load(UnmodelledPropertyForm);

        form.CanSaveFaithfully.Should().BeFalse();
    }

    [Fact]
    public void ATruncatedCompanion_HoldsTheFormReadOnly()
    {
        // The case nobody had noticed. A cited offset past the end of the companion is filtered out of the
        // blob dictionary AND fails to extract, so under the old count BOTH sides fell to the same lower
        // number and the two agreed. Counting citations makes the shortfall visible.
        var twoCitations =
            "VERSION 5.00\r\n" +
            "Begin VB.Form Form1 \r\n" +
            "   Begin VB.PictureBox Picture1 \r\n" +
            "      Picture         =   \"Form1.frx\":0000\r\n" +
            "   End\r\n" +
            "   Begin VB.PictureBox Picture2 \r\n" +
            "      Picture         =   \"Form1.frx\":00FF\r\n" +   // past the end of the 4-byte companion
            "   End\r\n" +
            "End\r\nAttribute VB_Name = \"Form1\"\r\n";

        var form = Load(twoCitations, FrxDeserializer.Read(new byte[] { 1, 2, 3, 4 }, twoCitations));

        form.CanSaveFaithfully.Should().BeFalse("one of the two cited blobs is not in the companion");
    }

    [Fact]
    public void AFormWhoseCitationsAreAllHonoured_StaysSavable()
    {
        // The guard against over-reaching: counting citations must not flag a form that is perfectly fine.
        // Two citations, two blobs, both captured.
        var twoCitations =
            "VERSION 5.00\r\n" +
            "Begin VB.Form Form1 \r\n" +
            "   Begin VB.PictureBox Picture1 \r\n" +
            "      Picture         =   \"Form1.frx\":0000\r\n" +
            "   End\r\n" +
            "   Begin VB.PictureBox Picture2 \r\n" +
            "      Picture         =   \"Form1.frx\":0004\r\n" +
            "   End\r\n" +
            "End\r\nAttribute VB_Name = \"Form1\"\r\n";

        var form = Load(twoCitations, FrxDeserializer.Read(new byte[] { 1, 2, 3, 4, 9, 9, 9, 9 }, twoCitations));

        form.CanSaveFaithfully.Should().BeTrue();
    }

    [Fact]
    public void TwoPropertiesCitingOneOffset_CountAsOneBlob()
    {
        // Distinct(): two controls sharing an image cite the same offset, which is one blob, not two.
        // Counting citations naively would flag this perfectly reproducible form.
        var sharedOffset =
            "VERSION 5.00\r\n" +
            "Begin VB.Form Form1 \r\n" +
            "   Begin VB.PictureBox Picture1 \r\n" +
            "      Picture         =   \"Form1.frx\":0000\r\n" +
            "   End\r\n" +
            "   Begin VB.PictureBox Picture2 \r\n" +
            "      Picture         =   \"Form1.frx\":0000\r\n" +
            "   End\r\n" +
            "End\r\nAttribute VB_Name = \"Form1\"\r\n";

        var form = Load(sharedOffset, FrxDeserializer.Read(new byte[] { 1, 2, 3, 4 }, sharedOffset));

        form.CanSaveFaithfully.Should().BeTrue("one offset cited twice is one blob");
    }

    [Fact]
    public void OneRecordCitedTwice_IsWrittenOnce()
    {
        // FrxDeserializer hands the SAME array instance to every property citing an offset, so a form where
        // two controls share an image arrives as one object seen twice. The writer collected per property,
        // so it wrote the bytes twice — and because FrxSerializer keys its offset map by reference, the
        // second write overwrote the first's entry and BOTH citations came out pointing at the second copy,
        // leaving the first uncited. The file grew and stopped matching what VB6 wrote.
        var shared =
            "VERSION 5.00\r\n" +
            "Begin VB.Form Form1 \r\n" +
            "   Begin VB.PictureBox Picture1 \r\n" +
            "      Picture         =   \"Form1.frx\":0000\r\n" +
            "   End\r\n" +
            "   Begin VB.PictureBox Picture2 \r\n" +
            "      Picture         =   \"Form1.frx\":0000\r\n" +
            "   End\r\n" +
            "End\r\nAttribute VB_Name = \"Form1\"\r\n";
        var companion = new byte[] { 4, 0, 0, 0, 7, 7, 7, 7 };

        var form = Load(shared, FrxDeserializer.Read(companion, shared));
        var (text, produced) = new FormSerializer().Serialize(form, "Form1.frm");

        produced.Should().Equal(companion, "one record cited twice is one record");
        FrxDeserializer.CitedOffsets(text).Distinct().Should().ContainSingle()
            .Which.Should().Be(0, "both properties cite the one record, at the offset it actually occupies");
    }
}
