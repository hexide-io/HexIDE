using HexIDE.Language;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.IDE;

/// <summary>Today's VB6 answers before moving their callers behind the format and reader (#3731).</summary>
public class CodePrefixEquivalenceTests
{
    public static TheoryData<string> Kinds => new()
    {
        "form", "new form", "class", "split bas", "unsplit bas", "ctl", "pag", "all header"
    };

    private static DocumentIdentity Document(string kind)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        const string attributes = "Attribute VB_Name = \"Item\"\r\n";
        const string body = "Option Explicit\r\n";
        if (kind is "form" or "new form")
        {
            var form = new FormDefinition(project, FormComponentClass.Instance, "Item");
            if (kind == "form") form.RecordDesignerText("VERSION 5.00\r\nBegin VB.Form Item\r\nEnd\r\n");
            form.UpdateCode(attributes + body);
            return DocumentIdentity.For(form);
        }
        var moduleKind = kind switch
        {
            "class" => ModuleKind.ClassModule,
            "ctl" => ModuleKind.UserControl,
            "pag" => ModuleKind.PropertyPage,
            _ => ModuleKind.StandardModule,
        };
        var module = new ModuleDefinition(project, "Item", moduleKind);
        if (kind is "ctl" or "pag")
        {
            var part = new FormDefinition(project, FormComponentClass.Instance, "Item");
            part.RecordDesignerText($"VERSION 5.00\r\nBegin VB.{(kind == "ctl" ? "UserControl" : "PropertyPage")} Item\r\nEnd\r\n");
            module.UpdateFormPart(part);
            module.UpdateCode(attributes + body);
        }
        else
        {
            var header = kind == "class" ? "VERSION 1.0 CLASS\r\nBEGIN\r\nEND\r\n" + attributes : attributes;
            module.RecordOriginalHeader(kind is "unsplit bas" or "all header" ? "" : header);
            module.UpdateCode(kind == "all header" ? attributes.TrimEnd('\r', '\n')
                : kind == "unsplit bas" ? header + body : body);
        }
        return DocumentIdentity.For(module);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void The_leading_region_has_the_existing_header_count_and_end(string kind)
    {
        var document = Document(kind);
        var prefix = FormCodeText.Prefix(document);
        var format = new Vb6ProjectFileFormat();
        (document.Module is { } module ? format.CodePrefix(module) : format.CodePrefix(document.Form!))
            .Should().Be(prefix);
        if (document.Module is { } named)
            format.PrefixCarriesName(named).Should().Be(ModuleFileFormat.HandlesHeader(named.Kind));
        var text = prefix + (document.Module?.Code ?? document.Form!.Code);
        var leading = new Vb6ReadOnlyRegionReader().Regions(text, prefix.Length)
            .Where(region => region.Start == 0 && region.Anchor is null).Cast<TextRegion?>().SingleOrDefault();
        var end = leading?.End ?? 0;
        var lines = text[..end].Count(c => c == '\n');
        if (end > 0 && text[end - 1] != '\n') lines++;

        var window = DocumentContent.TextOf(document, null, format, new Vb6ReadOnlyRegionReader());
        window.HeaderLineCount.Should().Be(lines);
        (window.LeadingRegion?.End ?? 0).Should().Be(end);
        end.Should().Be(ReadOnlyRegions.HeaderEnd(text, prefix.Length));
        lines.Should().Be(ReadOnlyRegions.HeaderLineCount(text, prefix.Length));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void The_omitted_attribute_note_keeps_todays_answer(string kind)
    {
        var document = Document(kind);
        DocumentContent.LeftOutLeadingRegion(document, "Option Explicit\r\n",
            format: new Vb6ProjectFileFormat(),
            reader: new Vb6ReadOnlyRegionReader()).HasValue.Should().Be(kind is "form" or "new form" or "ctl" or "pag");
        DocumentContent.LeftOutLeadingRegion(document, FormCodeText.Prefix(document) + (document.Module?.Code ?? document.Form!.Code),
            format: new Vb6ProjectFileFormat(),
            reader: new Vb6ReadOnlyRegionReader()).HasValue.Should().BeFalse();
    }
}
