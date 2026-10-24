// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors

using EmmyLua.LanguageServer.Framework.Server;
using StreamJsonRpc;

namespace HexIDE.VbLspServer.Tests;

internal static class ServerHarnessTeardown
{
    public static async ValueTask StopAsync(LanguageServer server, Task loop, JsonRpc rpc, TimeSpan timeout)
    {
        try
        {
            // Framework 0.9.2 Run() sets ExitTokenSource synchronously before returning. Its loop
            // clears that token on completion, including EOF. Keep the client connected until Exit()
            // has cancelled the loop: disposing rpc first lets EOF race Exit() (#355).
            server.Exit();
            await loop.WaitAsync(timeout);
        }
        finally
        {
            rpc.Dispose();
        }
    }
}
