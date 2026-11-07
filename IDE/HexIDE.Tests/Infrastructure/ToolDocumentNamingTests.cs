using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// Every automation tool that takes a document's name finds it through the one lookup, and its reply names what
/// it found (hexide-io/HexIDE#273, the hexide-mcp-server delta).
/// </summary>
/// <remarks>
/// <para>
/// <c>NamedDocumentsTests</c> covers the rule. These cover whether the tools use it, which nothing else can reach:
/// the tool bodies compile into an executable no test project references, so they are read here as text, as
/// <see cref="ToolSource"/> reads them. Before these, a <c>move_control</c> that went back to taking the first
/// open designer whose form had the caller's name, or a <c>Naming</c> that returned its reply untouched, passed
/// every test.
/// </para>
/// <para>
/// A tool's document-name parameter is whatever it passes first to <c>Find</c>, <c>FindDesigner</c> or
/// <c>ResolveDocument</c>, the tool-side doors to <c>NamedDocuments</c>. A body is read as code, with the text
/// of its string literals blanked, so the guards see an interpolated <c>{formName}</c> and not the prose around it.
/// </para>
/// </remarks>
public class ToolDocumentNamingTests
{
    private static readonly Regex Lookup = new(@"(?<![\w.])(?:Find|FindDesigner|ResolveDocument)\s*\(\s*(?<name>\w+)\s*,");
    private static readonly Regex NamingCall = new(@"(?<![\w.])Naming\s*\(");

    /// <summary>The tools that look a document up, each with the parameter it looks up and where it first does.</summary>
    private static IReadOnlyList<(ToolSource.Tool Tool, string Name, int At)> Resolving() =>
        ToolSource.Tools
            .Select(t => (Tool: t, Match: Lookup.Match(t.Body)))
            .Where(t => t.Match.Success)
            .Select(t => (t.Tool, t.Match.Groups["name"].Value, t.Match.Index))
            .ToList();

    [Fact]
    public void Every_tool_that_takes_a_documents_name_finds_it_through_the_lookup()
    {
        // A tool that takes `project` names a document in some project, so it takes the document's name too, and
        // has to find it the one way. add_file is the exception: its name is the name of a document it creates,
        // which no lookup can find, and its `project` is where to add it.
        var documentTools = ToolSource.Tools
            .Where(t => t.Parameters.Any(p => p.Name == "project") && t.Name != "add_file")
            .Select(t => t.Name)
            .ToList();
        documentTools.Should().HaveCountGreaterThanOrEqualTo(14,
            "fourteen tools took a document's name when this was written, so a smaller count is the parser's miss");

        documentTools.Should().BeSubsetOf(Resolving().Select(r => r.Tool.Name),
            "a tool that matches a name its own way finds a different document from every other tool: set_control_property "
            + "looked in the startup project only, and move_control and add_control took the first open designer of "
            + "that name, until #273");
    }

    [Fact]
    public void A_tool_that_names_a_form_or_module_takes_project()
    {
        ToolSource.Tools
            .Where(t => t.Parameters.Any(p => p.Name is "formName" or "module")
                        && t.Parameters.All(p => p.Name != "project"))
            .Select(t => t.Name)
            .Should().BeEmpty("a name is unique only within a project, so with a group open a bare one can be ambiguous");
    }

    [Fact]
    public void A_name_is_used_only_to_find_the_document()
    {
        // Once the lookup has found the document, the document is the answer: what the reply names, what a
        // designer or editor is matched against. The caller's spelling compared with anything else is a second
        // lookup with different rules, and quoted in a reply it is the echo #467 was about.
        var resolving = Resolving();
        resolving.Should().HaveCountGreaterThanOrEqualTo(14, "fourteen tools look a document up");

        // An optional tab name is checked for null to choose title addressing; that is not a second lookup.
        resolving
            .Select(r => (Tool: r.Tool.Name, Parameter: r.Name,
                Uses: Regex.Matches(Regex.Replace(r.Tool.Body, $@"\b{Regex.Escape(r.Name)} is null", ""), $@"(?<![\w.]){Regex.Escape(r.Name)}\b").Count,
                Lookups: Lookup.Matches(r.Tool.Body).Count(m => m.Groups["name"].Value == r.Name)))
            .Where(r => r.Uses != r.Lookups)
            .Select(r => $"{r.Tool} uses `{r.Parameter}` {r.Uses - r.Lookups} time(s) besides looking it up")
            .Should().BeEmpty("a tool that compares the caller's name with an open designer's or editor's finds whichever "
                              + "came first when a group holds two documents of that name");
    }

    [Fact]
    public void An_open_designer_is_found_by_the_form_and_never_by_its_name()
    {
        // The form the lookup returned is the key: two Form1s in a group are two forms, and only a comparison by
        // reference tells their designers apart. move_control matched FormDefinition?.Name until #273.
        Regex.Matches(ToolSource.CodeOnly(ToolSource.WithoutComments(ToolSource.Source)), @"FormDefinition\s*\??\.\s*Name\b")
            .Should().BeEmpty("a designer matched by its form's name may be the other project's");
    }

    [Fact]
    public void Every_reply_about_a_document_a_tool_found_names_it()
    {
        // After the lookup, a MutateResult reaches the caller through Naming, and any other reply record carries
        // its project itself. The one exemption is a refusal that passes on the lookup's own Error, which is the
        // case where nothing was found to name.
        var unnamed = new List<string>();
        foreach (var (tool, _, at) in Resolving())
        {
            var named = NamingCall.Matches(tool.Body)
                .Select(m => (Open: m.Index + m.Length - 1, Close: ToolSource.Closing(tool.Body, m.Index + m.Length - 1)))
                .ToList();
            var reply = new Regex($@"\bnew\s+(?<type>MutateResult|{Regex.Escape(tool.ReplyType)})\s*\(");
            foreach (Match construction in reply.Matches(tool.Body))
            {
                if (construction.Index < at)
                    continue;   // a refusal before anything was looked up has nothing to name

                var open = construction.Index + construction.Length - 1;
                var arguments = tool.Body[(open + 1)..Math.Max(open + 1, ToolSource.Closing(tool.Body, open))];
                var throughNaming = named.Any(n => n.Open < construction.Index && construction.Index < n.Close);
                var lookupRefusal = Regex.IsMatch(arguments, @"\.\s*Error\b");
                var namesItself = construction.Groups["type"].Value != "MutateResult"
                                  && Regex.IsMatch(arguments, @"\.\s*Project\s*\.\s*Name\b|\.\s*Owner\s*\.\s*Name\b|\bProject\s*:");

                if (!throughNaming && !lookupRefusal && !namesItself)
                    unnamed.Add($"{tool.Name}: new {construction.Groups["type"].Value}({Regex.Replace(arguments, @"\s+", " ").Trim()})");
            }
        }

        unnamed.Should().BeEmpty("a reply about a document the call found names it as the IDE spells it, which is the "
                                 + "only way a caller can see which of two documents of one name a call reached");
    }

    [Fact]
    public void Naming_puts_the_project_and_the_document_on_the_reply()
    {
        // Pinned as text because it cannot be called: nothing references the executable it compiles into. Every
        // MutateResult a tool returns about a document goes through one of these two, so a Naming that returns its
        // reply untouched drops 'project' and 'document' from all of them at once, and nothing else would notice.
        // Change these expectations only together with what Naming does.
        ToolSource.MethodBodies("Naming").Select(b => Regex.Replace(b, @"\s+", " ").Trim())
            .Should().BeEquivalentTo(
                "document is null ? reply : reply with { Project = document.Project.Name, Document = document.Name }",
                "found.Name is null ? reply : reply with { Project = found.Project, Document = found.Name }");
    }

    [Theory]
    [InlineData("ResolveDocument")]
    [InlineData("Find")]
    [InlineData("FindDesigner")]
    public void The_lookup_a_tool_calls_is_the_rule_over_every_loaded_project(string door)
    {
        ToolSource.MethodBodies(door).Should().ContainSingle()
            .Which.Should().MatchRegex(@"^NamedDocuments\.\w+\(\s*ctx\.ProjectManager\.LoadedProjects\s*,",
                "a tool-side door that searched the startup project alone, or anything but NamedDocuments, would make "
                + "every tool through it find a different document from the rest (#575)");
    }

    [Fact]
    public void Lines_are_counted_in_the_text_get_file_content_returns()
    {
        // set_breakpoints' range check and set_file_content's reported range read the same text through
        // DocumentContent.TextOf. A tool reading the code window's text its own way could disagree with them
        // about where a document ends, and no test compares the paths.
        Regex.Matches(ToolSource.CodeOnly(ToolSource.WithoutComments(ToolSource.Source)), @"\bCodeWindowText\b")
            .Select(m => m.Value)
            .Should().BeEmpty("the counted text is DocumentContent.TextOf's, read one way");
    }

    [Theory]
    [InlineData("activate_document_tab")]
    [InlineData("close_document_tab")]
    public void Tab_tools_accept_document_identity_without_requiring_a_title(string name)
    {
        var tool = ToolSource.Tools.Single(t => t.Name == name);
        tool.Parameters.Should().Contain(p => p.Name == "name" && p.Optional);
        tool.Parameters.Should().Contain(p => p.Name == "project" && p.Optional);
        tool.Parameters.Should().Contain(p => p.Name == "title" && p.Optional);
        tool.Body.Should().Contain("ResolveDocument(");
    }

    [Theory]
    [InlineData("DocumentTabInfo")]
    [InlineData("UndoStateResult")]
    [InlineData("LocalsResult")]
    [InlineData("WatchesResult")]
    public void Replies_about_implicit_documents_carry_the_project(string record)
    {
        ToolSource.Records[record].Should().Contain(f => f.Name == "project");
        if (record is "DocumentTabInfo" or "UndoStateResult")
            ToolSource.Records[record].Should().Contain(f => f.Name == "document");
    }

    [Fact]
    public void A_pause_note_identifies_the_running_project()
    {
        ToolSource.MethodBodies("PausedAt").Single().Should().Contain("ctx.ProjectRunnerService.RunningProject");
    }

    [Theory]
    [InlineData("invoke_designer_undo")]
    [InlineData("invoke_designer_redo")]
    [InlineData("invoke_format_command")]
    public void Active_designer_mutations_name_the_document(string name)
    {
        ToolSource.Tools.Single(t => t.Name == name).Body.Should().Contain("Naming(")
            .And.Contain("designer.OpenDocument");
    }

    [Fact]
    public void Open_editors_reports_structured_editor_identities()
    {
        ToolSource.Records["OpenEditorsResult"].Should().Contain(f => f.Name == "editors");
        ToolSource.Tools.Single(t => t.Name == "get_open_editors").Body.Should().Contain("TabInfo(");
    }

    [Theory]
    [InlineData("GetLocalsAsync")]
    [InlineData("BuildWatchesResult")]
    public void Frame_replies_read_the_running_project(string method)
    {
        ToolSource.MethodBodies(method).Single().Should().Contain("ctx.ProjectRunnerService.RunningProject?.Name");
    }
}
