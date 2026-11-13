using System.IO;
using System.Text.Json;
using Avalonia;
using HexIDE.Addins;
using HexIDE.AIChat.Views;
using HexIDE.Localization;

namespace HexIDE.Tests.AIChat;

public class ChatNoticeLocalizationTests
{
    [AvaloniaTheory]
    [MemberData(nameof(Packs))]
    public void CredentialRefusalNamesEnvironmentVariableBeforeFile(string id)
    {
        var value = new LocalizationService().GetStringFrom(id, "Str.AIChat.ApiKeyRequired");
        value.Should().Contain("HEXIDE_AI_API_KEY").And.Contain("apiKey").And.Contain("chat-settings.json");
        value.IndexOf("HEXIDE_AI_API_KEY", StringComparison.Ordinal).Should()
            .BeLessThan(value.IndexOf("chat-settings.json", StringComparison.Ordinal));
    }

    public static IEnumerable<object[]> Packs() => LanguageManifest.Packs.Select(p => new object[] { p.Id });

    [AvaloniaTheory]
    [InlineData("Str.AIChat.PrivacyNotice")]
    [InlineData("Str.AIChat.PrivacyKeyTip")]
    public void NoticeKeysResolveInEnglish(string key)
    {
        new LocalizationService().GetStringFrom("en", key).Should().NotBe(key).And.NotBeNullOrWhiteSpace();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoticeUsesLocalizedResourcesAndOnlyTipsForAFileKey(bool fileKey)
    {
        var names = new[] { "HEXIDE_AI_API_KEY", "ANTHROPIC_API_KEY", "OPENAI_API_KEY" };
        var previous = names.Select(Environment.GetEnvironmentVariable).ToArray();
        var directory = Path.Join(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var name in names) Environment.SetEnvironmentVariable(name, null);
            File.WriteAllText(Path.Join(directory, "chat-settings.json"), JsonSerializer.Serialize(new
            {
                baseUrl = "https://privacy.example/v1", apiKey = fileKey ? "file-key" : ""
            }));
            Application.Current!.Resources["Str.AIChat.PrivacyNotice"] = "Notice {0}";
            Application.Current.Resources["Str.AIChat.PrivacyKeyTip"] = "Localized tip";
            var chat = new ChatViewModel(Substitute.For<IHexIdeHost>(), directory);

            chat.MaybeShowEgressNotice();

            chat.Messages.Single().RawContent.Should().Be("Notice privacy.example" + (fileKey ? " Localized tip" : ""));
        }
        finally
        {
            for (var i = 0; i < names.Length; i++) Environment.SetEnvironmentVariable(names[i], previous[i]);
            Directory.Delete(directory, true);
        }
    }
}
