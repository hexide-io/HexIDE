using System.Collections.Generic;
using System.Linq;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A companion record HexIDE does not model is carried through a save, and every citation of it is rewritten to
/// wherever it lands (#60).
/// </summary>
/// <remarks>
/// Two kinds of property cite a record HexIDE cannot read: one HexIDE does not model on a control it does, and any
/// property of a control it does not model at all, kept as a preserved block. Both used to drop the citation and
/// hold the form read-only. Now the record goes back out with the modelled ones, in the order the <c>.frm</c>
/// cites them, which is how VB6 lays a companion out. The citation is rewritten whole, file name included, so a
/// modelled record that changes size moves the ones after it and their citations follow.
/// </remarks>
public class CarriedCompanionRecordTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public List<string> Errors { get; } = new();
        public void LogError(string error) => Errors.Add(error);
    }

    private static readonly byte[] PictureRecord = [4, 0, 0, 0, 1, 1, 1, 1];
    private static readonly byte[] BadgeRecord = [4, 0, 0, 0, 2, 2, 2, 2];
    private static readonly byte[] ListImageRecord = [4, 0, 0, 0, 3, 3, 3, 3];

    /// <summary>The three records, laid out in the order the form below cites them.</summary>
    private static readonly byte[] Companion = [.. BadgeRecord, .. PictureRecord, .. ListImageRecord];

    // A PictureBox's Picture is modelled; its Badge is not, and sorts before it, so the order of the text and the
    // order the writer reaches the two in differ. An unregistered ImageList class (#1083 models the real one) stays a preserved
    // block, and its picture sits two BeginProperty levels down, as Web Browser.frm's do.
    private const string Form =
        "VERSION 5.00\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Caption         =   \"Form1\"\r\n" +
        "   Begin VB.PictureBox Picture1 \r\n" +
        "      Badge           =   \"Form1.frx\":0000\r\n" +
        "      Picture         =   \"Form1.frx\":0008\r\n" +
        "   End\r\n" +
        "   Begin NoSuchLib.ImageList ImageList1 \r\n" +
        "      BeginProperty Images {2C247F25-8591-11D1-B16A-00C0F0283628} \r\n" +
        "         NumListImages   =   1\r\n" +
        "         BeginProperty ListImage1 {2C247F27-8591-11D1-B16A-00C0F0283628} \r\n" +
        "            Picture         =   \"Form1.frx\":0010\r\n" +
        "            Key             =   \"\"\r\n" +
        "         EndProperty\r\n" +
        "      EndProperty\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n";

    private static FormDefinition Load(string source, byte[] companion) =>
        new FormDeserializer().Deserialize(
            new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink(), FrxDeserializer.Read(companion, source))!;

    /// <summary>
    /// Each citation in <paramref name="text"/>, in document order, with the property it is on and the bytes it
    /// addresses in <paramref name="companion"/>.
    /// </summary>
    private static List<(string Property, byte[] Record)> Citations(string text, byte[] companion)
    {
        var records = FrxDeserializer.Read(companion, text);
        var cited = new List<(string, byte[])>();
        foreach (var line in text.Split("\r\n"))
        {
            if (!FrxDeserializer.IsFrxReference(line)) continue;
            var offset = FrxDeserializer.CitedOffsets(line).Single();
            cited.Add((line[..line.IndexOf('=')].Trim(), records.TryGetValue(offset, out var r) ? r : []));
        }
        return cited;
    }

    [Fact]
    public void A_form_citing_records_HexIDE_does_not_model_can_be_saved()
    {
        var form = Load(Form, Companion);

        form.CanSaveFaithfully.Should().BeTrue("every citation resolved, so every record can go back out");
        form.UnfaithfulSaveCauses.Should().NotHaveFlag(UnfaithfulSaveCause.UnreproducibleBinaryContent);
    }

    [Fact]
    public void An_untouched_form_writes_its_companion_back_byte_for_byte()
    {
        var (text, produced) = new FormSerializer().Serialize(Load(Form, Companion), "Form1.frm");

        produced.Should().Equal(Companion, "nothing changed, so every record lands where it was");
        Citations(text, produced!).Should().SatisfyRespectively(
            c => { c.Property.Should().Be("Badge"); c.Record.Should().Equal(BadgeRecord); },
            c => { c.Property.Should().Be("Picture"); c.Record.Should().Equal(PictureRecord); },
            c => { c.Property.Should().Be("Picture"); c.Record.Should().Equal(ListImageRecord); });
    }

    [Fact]
    public void A_preserved_controls_citation_is_kept_in_its_block()
    {
        var (text, _) = new FormSerializer().Serialize(Load(Form, Companion), "Form1.frm");

        text.Should().Contain(
            "         BeginProperty ListImage1 {2C247F27-8591-11D1-B16A-00C0F0283628} \r\n" +
            "            Picture         =   \"Form1.frx\":0010\r\n" +
            "            Key             =   \"\"\r\n",
            "the line goes back where it was read, and only its citation is rewritten");
    }

    [Fact]
    public void A_modelled_record_that_grows_moves_the_carried_ones_and_their_citations_follow()
    {
        var form = Load(Form, Companion);
        byte[] bigger = [8, 0, 0, 0, 9, 9, 9, 9, 9, 9, 9, 9];
        var picture = form.Components.Single(c => c.BaseClass is PictureBoxComponentClass);
        picture.SetProperty(VBProperties.PictureProperty, bigger);

        var (text, produced) = new FormSerializer().Serialize(form, "Form1.frm");

        // Kept at its old offset, the ImageList's record would overlap the grown picture; a verbatim citation would
        // then address the picture's last four bytes.
        produced.Should().Equal([.. BadgeRecord, .. bigger, .. ListImageRecord]);
        Citations(text, produced!).Should().SatisfyRespectively(
            c => c.Record.Should().Equal(BadgeRecord),
            c => c.Record.Should().Equal(bigger),
            c => c.Record.Should().Equal(ListImageRecord));
    }

    [Fact]
    public void Saved_under_another_name_every_citation_names_the_new_companion()
    {
        var (text, _) = new FormSerializer().Serialize(Load(Form, Companion), "Main.frm");

        text.Split("\r\n").Where(FrxDeserializer.IsFrxReference).Should().HaveCount(3)
            .And.OnlyContain(line => line.Contains("\"Main.frx\":"),
                "a carried line that kept the old name would cite a file the save did not write");
    }

    [Fact]
    public void A_record_cited_by_a_modelled_and_a_carried_property_is_written_once()
    {
        var shared = Form.Replace("Picture         =   \"Form1.frx\":0008", "Picture         =   \"Form1.frx\":0000")
            .Replace("\"Form1.frx\":0010", "\"Form1.frx\":0008");
        byte[] companion = [.. PictureRecord, .. ListImageRecord];

        var (text, produced) = new FormSerializer().Serialize(Load(shared, companion), "Form1.frm");

        produced.Should().Equal(companion, "one record cited twice is one record");
        FrxDeserializer.CitedOffsets(text).Should().Equal(0, 0, 8);
    }

    [Fact]
    public void A_NUL_carried_in_from_a_damaged_file_is_refused_rather_than_read_as_a_citation()
    {
        // The placeholders are NUL-delimited, and a carried line keeps whatever the file held. Read as a mark, a
        // stray NUL would fail deep inside the save with an exception that says nothing about why.
        var damaged = Form.Replace(
            "      Badge           =   \"Form1.frx\":0000\r\n",
            "      Badge           =   \"Form1.frx\":0000\r\n      Note            =   \"a" + (char)0 + "b\"\r\n");

        var save = () => new FormSerializer().Serialize(Load(damaged, Companion), "Form1.frm");

        save.Should().Throw<InvalidOperationException>().WithMessage("*designer text holds a NUL character*");
    }

    [Fact]
    public void A_carried_citation_the_companion_cannot_honour_still_holds_the_form_read_only()
    {
        // The companion stops before the ImageList's record, so that one citation cannot be honoured.
        var form = Load(Form, [.. BadgeRecord, .. PictureRecord]);

        form.CanSaveFaithfully.Should().BeFalse("saving would drop the ImageList's picture");
        form.UnfaithfulSaveCauses.Should().HaveFlag(UnfaithfulSaveCause.UnreproducibleBinaryContent);
    }
}
