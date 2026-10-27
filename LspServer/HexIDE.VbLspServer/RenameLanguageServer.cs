// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors

using EmmyLua.LanguageServer.Framework.Protocol.JsonRpc;
using EmmyLua.LanguageServer.Framework.Server;

namespace HexIDE.VbLspServer;

// Framework 0.9.2 handlers can return only results; thrown errors become InternalError.
// Keep this seam until the framework lets handlers return a ResponseError (#1015).
internal sealed class RenameLanguageServer(Stream input, Stream output, DocumentStore store)
    : LanguageServer(input, output, new LanguageServerOptions())
{
    protected override bool BaseHandle(Message message)
    {
        if (message is not RequestMessage { Method: "textDocument/rename" } request)
            return base.BaseHandle(message);

        // BaseHandle runs on the read loop. Scheduling keeps rename behind prior didChange work.
        Scheduler.Schedule(_ =>
        {
            try
            {
                string? refusal = null;
                using var result = request.Params is { } p
                    ? LspRequestHandlers.Rename(store, p.RootElement, out refusal)
                    : LspJson.Null();
                Writer.WriteResponse(request.Id, refusal is null ? result : null,
                    refusal is null ? null : new ResponseError(-32803, refusal, null));
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Rename handler failed");
                Writer.WriteResponse(request.Id, null, new ResponseError(-32603, ex.Message, null));
            }
            return Task.CompletedTask;
        }, request);
        return true;
    }
}
