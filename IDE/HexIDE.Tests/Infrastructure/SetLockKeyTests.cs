using HexIDE.Desktop.Server;
using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Runtime.BuiltinControls;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// set_lock_key sets a lock key's toggle as a running program reads it, so a StatusBar key panel's on state can be
/// live-checked: press_key can't change the toggle Windows keeps (#2296). Every reply lists all four keys.
/// </summary>
public sealed class SetLockKeyTests : IDisposable
{
    private readonly IdeContext context = new(Substitute.For<IProjectManager>(), Substitute.For<IDocumentDockService>(),
        Substitute.For<ILspClient>(), null!, Substitute.For<IProjectRunnerService>(),
        null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
    private readonly HexIdeTools tools;

    public SetLockKeyTests() => tools = new HexIdeTools(context);

    public void Dispose()
    {
        foreach (var key in Enum.GetValues<LockKey>())
            LockKeys.SetOverride(key, null);
        context.Dispose();
    }

    private static readonly LockKeySource Unset = OperatingSystem.IsWindows() ? LockKeySource.Keyboard : LockKeySource.Window;

    [Fact]
    public async Task Setting_a_key_reports_it_set_and_the_rest_as_the_keyboard_has_them()
    {
        var reply = await tools.SetLockKeyAsync("capslock", "ON", TestContext.Current.CancellationToken);

        reply.Success.Should().BeTrue();
        reply.Keys.Select(k => k.Key).Should().Equal(LockKey.CapsLock, LockKey.NumLock, LockKey.Insert, LockKey.ScrollLock);
        reply.Keys[0].Should().Be(new LockKeyRow(LockKey.CapsLock, true, LockKeySource.Set));
        foreach (var row in reply.Keys.Skip(1))
            row.Should().Be(new LockKeyRow(row.Key, LockKeys.Keyboard(row.Key), Unset));
        reply.Note.Should().StartWith("CapsLock now reads on to a running program");
        LockKeys.Override(LockKey.CapsLock).Should().BeTrue();
    }

    [Fact]
    public async Task Keyboard_returns_a_key_to_the_keyboard()
    {
        await tools.SetLockKeyAsync("NumLock", "Off", TestContext.Current.CancellationToken);

        var reply = await tools.SetLockKeyAsync("NumLock", "Keyboard", TestContext.Current.CancellationToken);

        reply.Success.Should().BeTrue();
        reply.Keys[1].Should().Be(new LockKeyRow(LockKey.NumLock, LockKeys.Keyboard(LockKey.NumLock), Unset));
        reply.Note.Should().Be("NumLock now follows the keyboard again.");
        LockKeys.Override(LockKey.NumLock).Should().BeNull();
    }

    [Fact]
    public async Task With_neither_argument_nothing_changes_and_all_four_are_listed()
    {
        await tools.SetLockKeyAsync("Insert", "On", TestContext.Current.CancellationToken);

        var reply = await tools.SetLockKeyAsync(ct: TestContext.Current.CancellationToken);

        reply.Success.Should().BeTrue();
        reply.Note.Should().StartWith("Nothing was changed.");
        reply.Keys.Should().HaveCount(4);
        reply.Keys[2].Should().Be(new LockKeyRow(LockKey.Insert, true, LockKeySource.Set));
    }

    [Theory]
    [InlineData("Shift", "On", "'Shift' is not a lock key")]
    [InlineData("1", "On", "'1' is not a lock key")]
    [InlineData("ScrollLock", "Maybe", "'Maybe' is not a state")]
    [InlineData(null, "On", "'state' was given without 'key'")]
    [InlineData("ScrollLock", null, "'key' was given without 'state'")]
    public async Task A_refusal_changes_nothing_and_names_the_vocabulary(string? key, string? state, string why)
    {
        var reply = await tools.SetLockKeyAsync(key, state, TestContext.Current.CancellationToken);

        reply.Success.Should().BeFalse();
        reply.Error.Should().Be($"Nothing was changed: {why}" + (key is null || state is null
            ? ". Pass both, or neither to read the keys; " : "; ")
            + "'key' is CapsLock, NumLock, Insert or ScrollLock, and 'state' is On, Off or Keyboard.");
        reply.Keys.Should().HaveCount(4).And.OnlyContain(k => k.Source == Unset);
    }
}
