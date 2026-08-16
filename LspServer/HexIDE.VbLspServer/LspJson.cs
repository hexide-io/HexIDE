// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// Utf8JsonWriter helpers for building LSP response payloads. Reflection-free (AOT-safe): the framework
// serializes a returned JsonDocument verbatim, and the Nodes DOM is NOT safe under reflection-disabled.

using System.Buffers;
using System.Text.Json;

namespace HexIDE.VbLspServer;

internal static class LspJson
{
    /// <summary>Builds a <see cref="JsonDocument"/> from a Utf8JsonWriter callback.</summary>
    public static JsonDocument Doc(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
            write(writer);
        return JsonDocument.Parse(buffer.WrittenMemory);
    }

    /// <summary>Explicit JSON <c>null</c> → serialized as LSP <c>result: null</c> (NOT an omitted result).</summary>
    public static JsonDocument Null() => JsonDocument.Parse("null");

    public static JsonDocument EmptyArray() => Doc(static w => { w.WriteStartArray(); w.WriteEndArray(); });

    public static void WritePositionValue(Utf8JsonWriter w, LspPosition p)
    {
        w.WriteStartObject();
        w.WriteNumber("line", p.Line);
        w.WriteNumber("character", p.Character);
        w.WriteEndObject();
    }

    public static void WriteRangeValue(Utf8JsonWriter w, LspRange r)
    {
        w.WriteStartObject();
        w.WritePropertyName("start"); WritePositionValue(w, r.Start);
        w.WritePropertyName("end");   WritePositionValue(w, r.End);
        w.WriteEndObject();
    }

    public static void WriteRange(Utf8JsonWriter w, string propertyName, LspRange r)
    {
        w.WritePropertyName(propertyName);
        WriteRangeValue(w, r);
    }
}
