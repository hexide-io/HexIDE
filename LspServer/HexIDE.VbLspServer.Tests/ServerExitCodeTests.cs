// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors

namespace HexIDE.VbLspServer.Tests;

public class ServerExitCodeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Completed_server_preserves_its_exit_code(int code)
    {
        (await global::Program.RunServerAsync(() => Task.FromResult(code))).Should().Be(code);
    }

    [Fact]
    public async Task Caught_crash_returns_code_2()
    {
        // Malformed framing is caught by the framework, not Program. Exercise Program's catch
        // directly without adding a crash request or switch to the shipped server (#3023).
        (await global::Program.RunServerAsync(() => Task.FromException<int>(
            new InvalidOperationException("server failed")))).Should().Be(2);
    }
}
