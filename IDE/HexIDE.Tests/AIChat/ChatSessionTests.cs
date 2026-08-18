using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using HexIDE.AIChat;

namespace HexIDE.Tests.AIChat;

public class ChatSessionTests
{
    // ── #1: history round-trip ────────────────────────────────────────────────

    [Fact]
    public void SaveThenLoad_RoundTripsFullLog()
    {
        var path = Path.GetTempFileName();
        try
        {
            var original = new ChatSession();
            original.Add(new ConversationMessage("user", "What's in Form1?"));
            original.Add(new ConversationMessage("assistant", "Let me check.",
                toolCalls: new List<ToolCall> { new("call_1", "get_active_file", "{}") }));
            original.Add(new ConversationMessage("tool", "{\"content\":\"Private Sub\"}",
                toolCallId: "call_1", toolName: "get_active_file"));
            original.Add(new ConversationMessage("assistant", "It has a Click handler."));
            original.SaveTo(path);

            var loaded = ChatSession.LoadFrom(path);

            loaded.Log.Should().HaveCount(4);

            loaded.Log[0].Role.Should().Be("user");
            loaded.Log[0].Content.Should().Be("What's in Form1?");

            loaded.Log[1].Role.Should().Be("assistant");
            loaded.Log[1].Content.Should().Be("Let me check.");
            loaded.Log[1].ToolCalls.Should().NotBeNull();
            loaded.Log[1].ToolCalls!.Should().ContainSingle();
            loaded.Log[1].ToolCalls![0].Id.Should().Be("call_1");
            loaded.Log[1].ToolCalls![0].Name.Should().Be("get_active_file");
            loaded.Log[1].ToolCalls![0].ArgumentsJson.Should().Be("{}");

            loaded.Log[2].Role.Should().Be("tool");
            loaded.Log[2].ToolCallId.Should().Be("call_1");
            loaded.Log[2].ToolName.Should().Be("get_active_file");

            loaded.Log[3].Content.Should().Be("It has a Click handler.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFrom_MissingFile_ReturnsEmptySession()
    {
        var session = ChatSession.LoadFrom(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Path.GetRandomFileName()));
        session.Log.Should().BeEmpty();
    }

    [Fact]
    public void CompactionSummary_SurvivesRoundTrip()
    {
        var path = Path.GetTempFileName();
        try
        {
            var original = new ChatSession();
            original.Add(new ConversationMessage("user", "hi"));
            original.SetCompactionSummary("Earlier we discussed Form1.");
            original.SaveTo(path);

            var loaded = ChatSession.LoadFrom(path);

            loaded.BuildApiMessages("SYS")[0]["content"]!.GetValue<string>()
                .Should().Contain("Earlier we discussed Form1.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── #3: tool-aware compaction boundary ─────────────────────────────────────

    [Fact]
    public void DrainForCompaction_KeptWindowNeverStartsWithToolMessage()
    {
        var session = new ChatSession();
        session.Add(new ConversationMessage("user", "u1"));
        session.Add(new ConversationMessage("assistant", "a1",
            toolCalls: new List<ToolCall> { new("c1", "list_files", "{}") }));
        session.Add(new ConversationMessage("tool", "result", toolCallId: "c1", toolName: "list_files"));
        session.Add(new ConversationMessage("assistant", "a2"));
        session.Add(new ConversationMessage("user", "u2"));
        session.Add(new ConversationMessage("assistant", "a3",
            toolCalls: new List<ToolCall> { new("c2", "get_diagnostics", "{}") }));
        session.Add(new ConversationMessage("tool", "result2", toolCallId: "c2", toolName: "get_diagnostics"));

        // keepTail = 5 → raw boundary = 7-5 = 2, which points at the first `tool` message.
        // The fix must advance the boundary forward so the kept window starts on a
        // non-tool message.
        var drained = session.DrainForCompaction(5);

        drained.Should().NotBeEmpty();

        var api = session.BuildApiMessages("SYS");
        // api[0] is the system message; the first conversation message must not be a tool.
        api[1]["role"]!.GetValue<string>().Should().NotBe("tool");
    }

    [Fact]
    public void BuildApiMessages_FoldsSummaryIntoSystem_NoSyntheticTurns()
    {
        var session = new ChatSession();
        session.Add(new ConversationMessage("user", "u1"));
        session.SetCompactionSummary("prior context");

        var api = session.BuildApiMessages("SYS");

        api[0]["role"]!.GetValue<string>().Should().Be("system");
        api[0]["content"]!.GetValue<string>().Should().Contain("SYS").And.Contain("prior context");
        // The message right after system should be the real first window message,
        // not an injected synthetic user/assistant pair.
        api[1]["role"]!.GetValue<string>().Should().Be("user");
        api[1]["content"]!.GetValue<string>().Should().Be("u1");
    }

    [Fact]
    public void ChatSettings_DefaultsModelIdToClaudeOpus5()
    {
        var settings = new ChatSettings();
        settings.ModelId.Should().Be("claude-opus-5");
    }
}
