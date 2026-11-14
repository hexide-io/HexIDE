using System.Collections;
using System.Reflection;
using HexIDE.Addins;
using HexIDE.Localization;

namespace HexIDE.Tests.Addins;

public sealed class AddinTitleTests
{
    private static readonly AddinOrigin AIChat = new("com.hexide.aichat", "AI Chat", true);

    [AvaloniaTheory]
    [InlineData("en", "AI Chat", "AI _Chat")]
    [InlineData("de", "KI-Chat", "KI-_Chat")]
    [InlineData("fr", "Chat IA", "_Chat IA")]
    [InlineData("ja", "AI チャット", "AI チャット(_C)")]
    public void OwnedKeys_FollowThePackAndPseudo(string language, string expected, string expectedMenu)
    {
        var localization = new LocalizationService();
        localization.Apply(language);
        var tools = new AddinToolWindowService(localization);
        var menus = new AddinMenuService(localization);
        var commands = new AddinCommandService(localization);
        using var tool = tools.ForOwner(AIChat).Register("chat", AddinTitle.FromKey("Str.AIChat.Title"), null, () => new object());
        using var menu = menus.AddOwned(AIChat, "Tools", AddinTitle.FromKey("Str.AIChat.Menu"), null, () => { }, null);
        using var command = commands.RegisterOwned(AIChat, "chat", AddinTitle.FromKey("Str.AIChat.Title"), () => { }, null, null);
        var renderedMenu = MainView.BuildMenuItem(menus.ToolsItems.Single());
        void Check(string text, string menuText)
        {
            tools.Tools.Single().Title.Should().Be(text);
            menus.ToolsItems.Single().Header.Should().Be(menuText);
            renderedMenu.Header.Should().Be(menuText);
            commands.Commands.Single().Label.Should().Be(text);
        }
        Check(expected, expectedMenu);
        var menuChanges = 0;
        var commandChanges = 0;
        menus.ToolsItems.Single().PropertyChanged += (_, e) => { if (e.PropertyName == "Header") menuChanges++; };
        commands.Commands.Single().PropertyChanged += (_, e) => { if (e.PropertyName == "Label") commandChanges++; };
        localization.Apply("pseudo");
        tools.Tools.Single().Title.Should().StartWith("⟦").And.EndWith("⟧");
        menus.ToolsItems.Single().Header.Should().StartWith("⟦").And.EndWith("⟧");
        renderedMenu.Header.Should().BeOfType<string>().Which.Should().StartWith("⟦").And.EndWith("⟧");
        commands.Commands.Single().Label.Should().StartWith("⟦").And.EndWith("⟧");
        localization.Apply("de");
        Check("KI-Chat", "KI-_Chat");
        menuChanges.Should().Be(2);
        commandChanges.Should().Be(2);
    }

    [AvaloniaTheory]
    [InlineData("fr-CA", "fr-CA", "Régional")]
    [InlineData("fr-CA", "fr", "Neutre")]
    [InlineData("de", "en", "English")]
    [InlineData("de", "fr", "Fallback")]
    public void Translations_ResolveTheBrandChainAndRefresh(string language, string supplied, string expected)
    {
        var localization = new LocalizationService();
        localization.Apply(language);
        var title = new AddinTitle("Fallback", new Dictionary<string, string>
        {
            [supplied] = supplied switch { "fr-CA" => "Régional", "fr" => "Neutre", _ => "English" },
        });
        var tools = new AddinToolWindowService(localization);
        var menus = new AddinMenuService(localization);
        var commands = new AddinCommandService(localization);
        using var tool = tools.Register("one", title, null, () => new object());
        using var menu = menus.Add("AddIns/Identifier", title, null, () => { });
        using var command = commands.Register("one", title, () => { }, null, null);
        tools.Tools.Single().Title.Should().Be(expected);
        menus.AddInsItems.Single().Header.Should().Be("Identifier");
        menus.AddInsItems.Single().SubItems.Single().Header.Should().Be(expected);
        commands.Commands.Single().Label.Should().Be(expected);
        localization.Apply("ja");
        var fallback = supplied == "en" ? "English" : "Fallback";
        tools.Tools.Single().Title.Should().Be(fallback);
        menus.AddInsItems.Single().SubItems.Single().Header.Should().Be(fallback);
        commands.Commands.Single().Label.Should().Be(fallback);
    }

    [AvaloniaTheory]
    [InlineData("unknown")]
    [InlineData("thirdparty")]
    [InlineData("wrongid")]
    [InlineData("security")]
    [InlineData("consent")]
    [InlineData("malformed")]
    [InlineData("empty")]
    public void InvalidTitles_RefuseAllSurfacesBeforeRegistration(string kind)
    {
        var title = kind switch
        {
            "unknown" => AddinTitle.FromKey("Str.AIChat.Typo"),
            "security" => AddinTitle.FromKey("Str.Dialog.About.SigningKeys.FirstPartyKey"),
            "consent" => AddinTitle.FromKey("Str.Dialog.AddinConsent.Allow"),
            "malformed" => new AddinTitle("Fallback", new Dictionary<string, string> { ["not a culture!"] = "Text" }),
            "empty" => new AddinTitle("Fallback", new Dictionary<string, string> { ["fr"] = "" }),
            _ => AddinTitle.FromKey("Str.AIChat.Title"),
        };
        var owner = kind == "thirdparty" ? AIChat with { IsFirstParty = false }
            : kind == "wrongid" ? AIChat with { Id = "com.example.other" } : AIChat;
        var tools = new AddinToolWindowService();
        var menus = new AddinMenuService();
        var commands = new AddinCommandService();
        var called = false;
        Action tool = () => tools.ForOwner(owner).Register("one", title, null, () => { called = true; return new object(); });
        Action menu = () => menus.AddOwned(owner, "AddIns/Identifier", title, null, () => called = true, null);
        Action command = () => commands.RegisterOwned(owner, "one", title, () => called = true, null, null);
        tool.Should().Throw<ArgumentException>().Which.Message.Should().NotBeNullOrWhiteSpace();
        menu.Should().Throw<ArgumentException>();
        command.Should().Throw<ArgumentException>();
        tools.Tools.Should().BeEmpty();
        menus.AddInsItems.Should().BeEmpty();
        commands.Commands.Should().BeEmpty();
        called.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Switches_NeverReadTheAddinsDictionaryOrRunItsDelegates()
    {
        var localization = new LocalizationService();
        localization.Apply("en");
        var dictionary = new CountingTranslations();
        var title = new AddinTitle("Fallback", dictionary);
        var tools = new AddinToolWindowService(localization);
        var menus = new AddinMenuService(localization);
        var commands = new AddinCommandService(localization);
        var factories = 0;
        var callbacks = 0;
        using var tool = tools.Register("one", title, null, () => { factories++; return new object(); });
        using var menu = menus.Add("Tools", title, null, () => callbacks++, () => { callbacks++; return true; });
        using var command = commands.Register("one", title, () => callbacks++, null, () => { callbacks++; return true; });
        var accesses = dictionary.Accesses;
        accesses.Should().BeGreaterThan(0);
        var renderedMenu = MainView.BuildMenuItem(menus.ToolsItems.Single());
        callbacks = 0; // A menu can query CanExecute when its command is first attached.
        dictionary.ValuesByLanguage["fr"] = "Changed by add-in";
        foreach (var language in new[] { "fr-CA", "de", "en", "fr" }) localization.Apply(language);
        dictionary.Accesses.Should().Be(accesses);
        tools.Tools.Single().Title.Should().Be("Français");
        menus.ToolsItems.Single().Header.Should().Be("Français");
        renderedMenu.Header.Should().Be("Français");
        commands.Commands.Single().Label.Should().Be("Français");
        factories.Should().Be(1);
        callbacks.Should().Be(0);
    }

    [Fact]
    public void TitleApi_ExposesOnlyData_AndKeepsStringOverloads()
    {
        typeof(AddinTitle).IsSealed.Should().BeTrue();
        typeof(AddinTitle).GetEvents().Should().BeEmpty();
        var allowed = new[] { typeof(string), typeof(IReadOnlyDictionary<string, string>) };
        foreach (var property in typeof(AddinTitle).GetProperties()) allowed.Should().Contain(property.PropertyType);
        foreach (var constructor in typeof(AddinTitle).GetConstructors())
            foreach (var parameter in constructor.GetParameters()) allowed.Should().Contain(parameter.ParameterType);
        foreach (var method in typeof(AddinTitle).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            method.IsVirtual.Should().BeFalse();
            new[] { typeof(string), typeof(IReadOnlyDictionary<string, string>), typeof(AddinTitle), typeof(void) }
                .Should().Contain(method.ReturnType);
            foreach (var parameter in method.GetParameters()) allowed.Should().Contain(parameter.ParameterType);
        }
        foreach (var type in new[] { typeof(IToolWindowContributor), typeof(IMenuContributor), typeof(IHexIdeHost) })
        {
            var method = type == typeof(IToolWindowContributor) ? "Register" : type == typeof(IMenuContributor) ? "Add" : "RegisterCommand";
            var overloads = type.GetMethods().Where(m => m.Name == method).ToArray();
            overloads.Should().HaveCount(2);
            overloads.Select(m => m.GetParameters()[1].ParameterType).Should().BeEquivalentTo([typeof(string), typeof(AddinTitle)]);
            var legacy = overloads.Single(m => m.GetParameters()[1].ParameterType == typeof(string)).GetParameters();
            var added = overloads.Single(m => m.GetParameters()[1].ParameterType == typeof(AddinTitle)).GetParameters();
            added.Length.Should().Be(legacy.Length);
            for (var i = 0; i < added.Length; i++)
                if (i != 1) added[i].ParameterType.Should().Be(legacy[i].ParameterType);
        }
    }

    public static IEnumerable<object[]> UnsafeTextCases()
    {
        var values = new[] { "", " ", " leading", "trailing ", new string('a', 129),
            "a\tb", "a\u202eb", "a\u2066b", "a\u200bb", "a\ufeffb", "a\u2028b", "a\u2029b",
            "_Literal\u202e\t", "a\u202ab", "a\u202bb", "a\u202cb", "a\u202db",
            "a\ud800b", "a\udc00b", "a\u2067b", "a\u2068b", "a\u2069b", "a\0b", "a\U000e0001b" };
        foreach (var text in values)
            foreach (var form in new[] { "string", "title", "translation" })
                foreach (var surface in new[] { "tool", "menu", "command" })
                    yield return [text, form, surface];
    }

    [AvaloniaTheory]
    [MemberData(nameof(UnsafeTextCases))]
    public void UnsafeText_RefusesAllSurfacesBeforeRegistration(string text, string form, string surface)
    {
        var tools = new AddinToolWindowService();
        var menus = new AddinMenuService();
        var commands = new AddinCommandService();
        IHexIdeHost host = new HexIdeHost(menus, tools, null!, null!, null!, null!, null!, commands,
            null!, null!, null!, null!, null!, null!, null!);
        var called = false;
        var title = form == "translation"
            ? new AddinTitle("Fallback", new Dictionary<string, string> { ["fr"] = text })
            : new AddinTitle(text);
        Action tool = () => { if (form == "string") tools.Register("one", text, null, () => { called = true; return new object(); });
            else tools.Register("one", title, null, () => { called = true; return new object(); }); };
        Action menu = () => { if (form == "string") menus.Add("AddIns/Identifier", text, null, () => called = true);
            else menus.Add("AddIns/Identifier", title, null, () => called = true); };
        Action command = () => { if (form == "string") host.RegisterCommand("one", text, () => called = true);
            else host.RegisterCommand("one", title, () => called = true); };
        var register = surface == "tool" ? tool : surface == "menu" ? menu : command;
        register.Should().Throw<ArgumentException>().Which.Message.Should().NotBeNullOrWhiteSpace();
        tools.Tools.Should().BeEmpty();
        menus.AddInsItems.Should().BeEmpty();
        commands.Commands.Should().BeEmpty();
        called.Should().BeFalse();
        using var retry = tools.Register("one", "Valid", null, () => new object());
        tools.Tools.Should().ContainSingle();
    }

    [AvaloniaTheory]
    [InlineData("", "empty or only whitespace")]
    [InlineData(" ", "empty or only whitespace")]
    [InlineData(" leading", "leading or trailing whitespace")]
    [InlineData("trailing ", "leading or trailing whitespace")]
    [InlineData("limit", "length 129 exceeds the limit of 128")]
    [InlineData("a\u202eb", "refused character U+202E at UTF-16 index 1")]
    [InlineData("😀\U000e0001", "refused character U+E0001 at UTF-16 index 2")]
    [InlineData("high-surrogate", "invalid UTF-16 at index 1")]
    [InlineData("low-surrogate", "invalid UTF-16 at index 1")]
    public void Refusal_IdentifiesTheProblemInEnglishEvenInGerman(string text, string problem)
    {
        if (text == "limit") text = new string('a', 129);
        if (text == "high-surrogate") text = "a\ud800b";
        if (text == "low-surrogate") text = "a\udc00b";
        var localization = new LocalizationService();
        localization.Apply("de");
        var tools = new AddinToolWindowService(localization);
        Action literal = () => tools.Register("one", text, null, () => new object());
        Action translation = () => tools.Register("one", new AddinTitle("Fallback",
            new Dictionary<string, string> { ["fr"] = text }), null, () => new object());
        literal.Should().Throw<ArgumentException>().Which.Message.Should()
            .StartWith($"Invalid add-in title: {problem}.");
        translation.Should().Throw<ArgumentException>().Which.Message.Should()
            .StartWith($"Invalid translation for 'fr': {problem}.");
        tools.Tools.Should().BeEmpty();
    }

    [Fact]
    public void Refusal_HasNoLocalizationKeyInAnyPack()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "IDE", "HexIDE", "Localization", "Packs")))
            directory = directory.Parent;
        directory.Should().NotBeNull();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(directory!.FullName, "IDE", "HexIDE", "Localization", "Packs"), "*.json"))
            File.ReadAllText(file).Should().NotContain("Str.Addins.InvalidTitle", file);
    }

    [AvaloniaTheory]
    [InlineData("Arabic العربية")]
    [InlineData("עברית")]
    [InlineData("می\u200cروم")]
    [InlineData("👩\u200d💻")]
    [InlineData("_Access key")]
    [InlineData("limit")]
    [InlineData("surrogate-limit")]
    public void SafeText_RetainsTextAndDisposesNormally(string text)
    {
        if (text == "limit") text = new string('a', 128);
        if (text == "surrogate-limit") text = string.Concat(Enumerable.Repeat("😀", 64));
        var localization = new LocalizationService();
        var tools = new AddinToolWindowService(localization);
        var menus = new AddinMenuService(localization);
        var commands = new AddinCommandService(localization);
        var title = new AddinTitle(text, new Dictionary<string, string> { ["fr"] = text });
        using var tool = tools.Register("one", text, null, () => new object());
        using var menu = menus.Add("Tools", text, null, () => { });
        using var command = commands.Register("one", text, () => { }, null, null);
        using var translatedTool = tools.Register("translated", title, null, () => new object());
        using var translatedMenu = menus.Add("Tools", title, null, () => { });
        using var translatedCommand = commands.Register("translated", title, () => { }, null, null);
        localization.Apply("fr");
        tools.Tools.Should().OnlyContain(t => t.Title == text);
        menus.ToolsItems.Should().OnlyContain(m => m.Header == text);
        commands.Commands.Should().OnlyContain(c => c.Label == text);
        tool.Dispose(); menu.Dispose(); command.Dispose();
        translatedTool.Dispose(); translatedMenu.Dispose(); translatedCommand.Dispose();
        localization.Apply("de");
        tools.Tools.Should().BeEmpty(); menus.ToolsItems.Should().BeEmpty(); commands.Commands.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData(true, "com.hexide.aichat", AddinStatus.Loaded, true)]
    [InlineData(false, "com.hexide.aichat", AddinStatus.Loaded, false)]
    [InlineData(false, "com.hexide.aichat", AddinStatus.DeveloperUnsigned, true)]
    [InlineData(false, "com.example.other", AddinStatus.DeveloperUnsigned, false)]
    [InlineData(true, "com.example.other", AddinStatus.Loaded, false)]
    public void RegistryFacade_UsesVerifiedOwnerRatherThanClaimedId(bool firstParty, string id, AddinStatus status, bool accepted)
    {
        var localization = new LocalizationService();
        localization.Apply("de");
        var tools = new AddinToolWindowService(localization);
        var menus = new AddinMenuService(localization);
        var host = Substitute.For<IHexIdeHost>();
        host.ToolWindows.Returns(tools);
        host.Menus.Returns(menus);
        var root = Path.Combine(Path.GetTempPath(), "hexide-title-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var registry = new AddinRegistry(Substitute.For<IPackageVerifier>(), Substitute.For<HexIDE.IDE.ISettingsService>(),
                Substitute.For<HexIDE.IDE.IDeveloperModeService>(), Substitute.For<HexIDE.IDE.IWindowManager>(), localization,
                new ConsentStore(Path.Combine(root, "consent.json")), new RevocationStore(null, Path.Combine(root, "revocations")),
                Path.Combine(root, "state.json"), root);
            var link = new TrustLink("Test", "test", "test");
            var info = new AddinInfo(root, "Signed name", "", "1.0", "", Id: id, Chain: new TrustChain
            {
                IsFirstParty = firstParty, BuildVersion = "1.0", BuildHash = "hash",
                Publisher = link, Intermediate = link, Root = link,
            });
            var context = new AddinContext(info);
            var addin = Substitute.For<IAddin>();
            var handles = new List<IDisposable>();
            addin.When(a => a.Initialize(Arg.Any<IHexIdeHost>())).Do(call =>
            {
                var owned = call.Arg<IHexIdeHost>();
                handles.Add(owned.ToolWindows.Register("chat", AddinTitle.FromKey("Str.AIChat.Title"), null, () => new object()));
                handles.Add(owned.Menus.Add("Tools", AddinTitle.FromKey("Str.AIChat.Menu"), null, () => { }));
            });
            registry.InitializeAddin(context, addin, status, host);
            context.Status.Should().Be(accepted ? status : AddinStatus.Error);
            info.Title.Should().Be("Signed name");
            if (accepted)
            {
                tools.Tools.Single().Title.Should().Be("KI-Chat");
                menus.ToolsItems.Single().Header.Should().Be("KI-_Chat");
            }
            else
            {
                tools.Tools.Should().BeEmpty();
                menus.ToolsItems.Should().BeEmpty();
            }
            foreach (var handle in handles) handle.Dispose();
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void KeyAreas_HaveExactlyTheReviewedOwnersAndNeverOverlap()
    {
        var field = typeof(RegisteredAddinTitle).GetField("KeyOwners", BindingFlags.Static | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        var owners = (IReadOnlyDictionary<string, string>)field!.GetValue(null)!;
        owners.Should().BeEquivalentTo(new Dictionary<string, string> { ["Str.AIChat."] = "com.hexide.aichat" });
        foreach (var (area, owner) in owners)
        {
            owner.Should().NotBeNullOrWhiteSpace();
            owners.Keys.Where(other => other != area).Should().NotContain(other => other.StartsWith(area, StringComparison.Ordinal));
        }
    }

    private sealed class CountingTranslations : IReadOnlyDictionary<string, string>
    {
        public Dictionary<string, string> ValuesByLanguage { get; } = new() { ["fr"] = "Français" };
        public int Accesses { get; private set; }
        public int Count { get { Accesses++; return ValuesByLanguage.Count; } }
        public IEnumerable<string> Keys { get { Accesses++; return ValuesByLanguage.Keys; } }
        public IEnumerable<string> Values { get { Accesses++; return ValuesByLanguage.Values; } }
        public string this[string key] { get { Accesses++; return ValuesByLanguage[key]; } }
        public bool ContainsKey(string key) { Accesses++; return ValuesByLanguage.ContainsKey(key); }
        public bool TryGetValue(string key, out string value) { Accesses++; return ValuesByLanguage.TryGetValue(key, out value!); }
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() { Accesses++; return ValuesByLanguage.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { Accesses++; return GetEnumerator(); }
    }
}
