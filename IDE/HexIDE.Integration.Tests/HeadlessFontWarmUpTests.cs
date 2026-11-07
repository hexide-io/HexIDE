using System;
using Avalonia.Media;

namespace HexIDE.Integration.Tests;

public class HeadlessFontWarmUpTests
{
    [Fact]
    public void Missing_platform_face_names_the_default_family_and_stage()
    {
        Action warmUp = () => HeadlessFontWarmUp.Run("Diagnostic Sans", () => null, (_, _) =>
            throw new InvalidOperationException("must not reach glyph creation"));

        warmUp.Should().Throw<InvalidOperationException>()
            .WithMessage("*Diagnostic Sans*Bold*platform typeface*GlyphTypeface.TryCreate: not called*");
    }

    [Fact]
    public void Null_glyph_names_the_default_family_and_null_result_without_retrying()
    {
        var platformTypeface = Substitute.For<IPlatformTypeface>();
        var attempts = 0;

        Action warmUp = () => HeadlessFontWarmUp.Run("Diagnostic Sans", () => platformTypeface, (_, _) =>
        {
            attempts++;
            return null;
        });

        warmUp.Should().Throw<InvalidOperationException>()
            .WithMessage("*Diagnostic Sans*Bold*GlyphTypeface.TryCreate: null*");
        attempts.Should().Be(1);
        platformTypeface.Received(1).Dispose();
    }

    [AvaloniaFact]
    public void Startup_probe_creates_the_real_default_bold_glyph()
    {
        var family = FontManager.Current.DefaultFontFamily.Name;
        var attempts = 0;

        HeadlessFontWarmUp.Run(family, () => HeadlessFontWarmUp.CreatePlatformTypeface(family), (face, simulations) =>
        {
            attempts++;
            face.FamilyName.Should().NotBeNullOrEmpty();
            return new GlyphTypeface(face, simulations);
        });

        attempts.Should().Be(1);
    }
}
