using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using HexIDE.Addins;
using HexIDE.AIChat;
using HexIDE.AIChat.Views;

namespace HexIDE.Tests.AIChat;

/// <summary>
/// The first message to an endpoint off this machine waits for Send on the privacy notice, once per notice version
/// and host, and nothing leaves before it (hexide-io/HexIDE#4230, decided on the Team Desk, 2026-11-16).
/// </summary>
public sealed class ChatEgressConfirmationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly RecordingHandler _handler = new();
    private readonly IHexIdeHost _host = Substitute.For<IHexIdeHost>();

    public ChatEgressConfirmationTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        _handler.Dispose();
        Directory.Delete(_directory, true);
    }

    private string ConfirmedPath => Path.Combine(_directory, ChatViewModel.EgressConfirmedFileName);

    private const string OtherService = "https://api.openai.com/v1";

    private ChatViewModel Chat(string? endpoint = null, string text = "private message")
    {
        WriteSettings(endpoint);
        return new ChatViewModel(_host, _directory, new ChatClient(new HttpClient(_handler))) { InputText = text };
    }

    private void WriteSettings(string? endpoint)
    {
        var settings = new ChatSettings { ApiKey = "file-key", MaxTurnsBeforeCompaction = 100 };
        if (endpoint is not null) settings.BaseUrl = endpoint;
        File.WriteAllText(Path.Combine(_directory, "chat-settings.json"), JsonSerializer.Serialize(settings));
    }

    private static ChatMessageViewModel[] Confirmations(ChatViewModel chat) =>
        chat.Messages.Where(m => m.IsEgressConfirmation).ToArray();

    [Fact]
    public async Task The_first_send_to_an_outside_endpoint_asks_and_sends_nothing()
    {
        var chat = Chat();

        await chat.SendAsync();

        _handler.Requests.Should().Be(0);
        _host.Editor.DidNotReceive().GetActiveDocument();
        var confirmation = Confirmations(chat).Should().ContainSingle().Subject;
        confirmation.IsAwaitingDecision.Should().BeTrue();
        confirmation.IsBubble.Should().BeFalse();
        confirmation.RawContent.Should().Contain("Str.AIChat.EgressConfirm");
        chat.InputText.Should().Be("private message");
        chat.SessionLog.Should().BeEmpty();
        File.Exists(ConfirmedPath).Should().BeFalse();
    }

    [Fact]
    public async Task Send_on_the_notice_sends_the_message_and_records_the_choice()
    {
        var chat = Chat();
        await chat.SendAsync();

        await chat.ConfirmEgressAsync(Confirmations(chat).Single());

        _handler.Requests.Should().BeGreaterThan(0);
        chat.InputText.Should().BeEmpty();
        chat.SessionLog.Should().Contain(m => m.Role == "user" && m.Content == "private message");
        File.ReadAllText(ConfirmedPath).Should().Be(ChatViewModel.EgressNoticeVersion + "\napi.anthropic.com");
        Confirmations(chat).Single().ApprovalOutcome.Should().BeEmpty("the message shows itself once sent");
    }

    [Fact]
    public async Task After_Send_the_next_message_goes_without_asking_and_so_does_a_new_session()
    {
        var chat = Chat();
        await chat.SendAsync();
        await chat.ConfirmEgressAsync(Confirmations(chat).Single());
        var sent = _handler.Requests;

        chat.InputText = "second";
        await chat.SendAsync();
        _handler.Requests.Should().BeGreaterThan(sent);
        Confirmations(chat).Should().ContainSingle();

        var next = Chat(text: "in a new session");
        await next.SendAsync();
        Confirmations(next).Should().BeEmpty();
    }

    [Fact]
    public async Task Cancel_sends_nothing_and_leaves_the_message_in_the_box()
    {
        var chat = Chat();
        await chat.SendAsync();

        chat.DeclineEgress(Confirmations(chat).Single());

        _handler.Requests.Should().Be(0);
        chat.InputText.Should().Be("private message");
        Confirmations(chat).Single().IsAwaitingDecision.Should().BeFalse();
        Confirmations(chat).Single().ApprovalOutcome.Should().Be("Str.AIChat.EgressNotSent");
        File.Exists(ConfirmedPath).Should().BeFalse();

        await chat.SendAsync();
        Confirmations(chat).Should().HaveCount(2, "a send after Cancel asks again");
        _handler.Requests.Should().Be(0);
    }

    [Fact]
    public async Task Sending_again_while_asked_adds_no_second_notice()
    {
        var chat = Chat();
        await chat.SendAsync();
        await chat.SendAsync();

        Confirmations(chat).Should().ContainSingle();
        _handler.Requests.Should().Be(0);
    }

    [Fact]
    public async Task A_new_chat_clears_the_question_so_the_next_send_asks_again()
    {
        var chat = Chat();
        await chat.SendAsync();
        chat.NewChat();
        chat.InputText = "after a new chat";

        await chat.SendAsync();

        Confirmations(chat).Should().ContainSingle();
        _handler.Requests.Should().Be(0);
    }

    [Theory]
    [InlineData("project-tool-reads-older\napi.anthropic.com")]
    [InlineData("shown")]
    [InlineData(ChatViewModel.EgressNoticeVersion)]
    public async Task A_choice_recorded_for_another_notice_version_or_no_host_asks_again(string recorded)
    {
        File.WriteAllText(ConfirmedPath, recorded);
        var chat = Chat();

        await chat.SendAsync();

        Confirmations(chat).Should().ContainSingle();
        _handler.Requests.Should().Be(0);
    }

    [Fact]
    public async Task A_notice_only_shown_is_not_a_choice_to_send()
    {
        // .ai-egress-ack records that the notice was shown, as a local endpoint does without asking.
        File.WriteAllText(Path.Combine(_directory, ".ai-egress-ack"), ChatViewModel.EgressNoticeVersion);
        var chat = Chat();

        await chat.SendAsync();

        Confirmations(chat).Should().ContainSingle();
        _handler.Requests.Should().Be(0);
    }

    [Fact]
    public async Task A_choice_for_one_service_does_not_carry_to_another_in_a_new_session()
    {
        var first = Chat();
        await first.SendAsync();
        await first.ConfirmEgressAsync(Confirmations(first).Single());
        var sent = _handler.Requests;

        var next = Chat(OtherService, "for the other service");
        await next.SendAsync();

        Confirmations(next).Should().ContainSingle();
        _handler.Requests.Should().Be(sent, "nothing goes to the other service before Send");
        next.InputText.Should().Be("for the other service");
    }

    [Fact]
    public async Task A_choice_for_one_service_does_not_carry_to_another_when_the_settings_change()
    {
        var chat = Chat();
        await chat.SendAsync();
        await chat.ConfirmEgressAsync(Confirmations(chat).Single());
        var sent = _handler.Requests;

        WriteSettings(OtherService);
        chat.ReloadSettings();
        chat.InputText = "for the other service";
        await chat.SendAsync();

        Confirmations(chat).Should().HaveCount(2);
        _handler.Requests.Should().Be(sent);

        await chat.ConfirmEgressAsync(Confirmations(chat).Last());
        _handler.Requests.Should().BeGreaterThan(sent);
        File.ReadAllText(ConfirmedPath).Should().Be(ChatViewModel.EgressNoticeVersion + "\napi.openai.com");
    }

    [Fact]
    public async Task A_question_open_when_the_service_changes_is_replaced_and_cannot_confirm_the_new_one()
    {
        var chat = Chat();
        await chat.SendAsync();
        var asked = Confirmations(chat).Single();

        WriteSettings(OtherService);
        chat.ReloadSettings();
        await chat.SendAsync();

        asked.IsAwaitingDecision.Should().BeFalse("its Send would confirm a host it no longer names");
        asked.ApprovalOutcome.Should().Be("Str.AIChat.EgressNotSent");
        Confirmations(chat).Should().HaveCount(2);
        Confirmations(chat).Last().IsAwaitingDecision.Should().BeTrue();
        await chat.ConfirmEgressAsync(asked);
        _handler.Requests.Should().Be(0);
        File.Exists(ConfirmedPath).Should().BeFalse();
    }

    [Fact]
    public async Task Send_on_a_question_confirms_the_host_it_named_even_if_the_settings_changed_since()
    {
        var chat = Chat();
        await chat.SendAsync();
        var asked = Confirmations(chat).Single();

        WriteSettings(OtherService);
        chat.ReloadSettings();
        await chat.ConfirmEgressAsync(asked);

        File.ReadAllText(ConfirmedPath).Should().Be(ChatViewModel.EgressNoticeVersion + "\napi.anthropic.com");
        _handler.Requests.Should().Be(0, "the other service was never named, so its send asks first");
        Confirmations(chat).Should().HaveCount(2);
    }

    [Theory]
    [InlineData("http://localhost:11434/v1")]
    [InlineData("http://127.0.0.1:1234/v1")]
    [InlineData("http://[::1]:1234/v1")]
    public async Task A_local_endpoint_does_not_ask(string endpoint)
    {
        var chat = Chat(endpoint);

        await chat.SendAsync();

        Confirmations(chat).Should().BeEmpty();
        _handler.Requests.Should().BeGreaterThan(0);
        File.Exists(ConfirmedPath).Should().BeFalse("a local endpoint records no choice to send elsewhere");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri!.Host == "api.anthropic.com"
                    ? "data: {\"type\":\"message_stop\"}\n\n" : "data: [DONE]\n\n")
            });
        }
    }
}
