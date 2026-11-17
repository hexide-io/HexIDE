using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HexIDE.Addins;

/// <summary>
/// The namespaces a verified publisher record issues (#4258 part D, #4024's package format): an add-in's language-server
/// message prefix must be <c>&lt;ns&gt;/</c> and its command prefix <c>&lt;ns&gt;.</c> for one of its publisher's namespaces.
/// </summary>
/// <remarks>
/// <para>
/// The list comes only from <c>publisher.json</c> after its signature is checked (<see cref="PackageVerification"/>), never
/// from <c>addin.json</c> or anything else the publisher writes alone. A record issued before the field existed has none
/// (null), and today's rule applies to it, with a notice.
/// </para>
/// <para>
/// An entry that isn't a name followed by <c>.</c>, or that is <c>hexide.</c> or under it, grants nothing and is dropped:
/// <c>hexide/</c> and <c>hexide.</c> are core's whatever a record says. A record whose entries are all dropped issues no
/// namespace at all, so it grants no prefix, rather than falling back to the rule for records without the field.
/// </para>
/// </remarks>
public static partial class PublisherNamespaces
{
    /// <summary>The namespace core keeps for itself: <c>hexide/</c> for messages and <c>hexide.</c> for commands.</summary>
    public const string Reserved = "hexide.";

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_-]*(\.[A-Za-z][A-Za-z0-9_-]*)*\.$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();

    /// <summary>The usable namespaces of a verified record, or null when the record carries no list.</summary>
    public static string[]? From(string[]? issued) =>
        issued?.Where(IsUsable).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>A name followed by <c>.</c>, outside core's reserved namespace.</summary>
    public static bool IsUsable(string? ns) =>
        ns is { Length: > 1 } && Shape().IsMatch(ns) && !IsReserved(ns);

    /// <summary>Core's own: <c>hexide.</c> or a namespace under it, compared without regard to case.</summary>
    public static bool IsReserved(string ns) => ns.StartsWith(Reserved, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a message prefix (<c>rdcore/</c>, <c>rdcore/sub/</c>) is under one of the namespaces.</summary>
    public static bool AllowsMessagePrefix(IReadOnlyList<string> namespaces, string prefix) =>
        namespaces.Any(ns => prefix.StartsWith(ns[..^1] + "/", StringComparison.Ordinal));

    /// <summary>Whether a command prefix (<c>rdcore.</c>, <c>rdcore.sub.</c>) is under one of the namespaces.</summary>
    public static bool AllowsCommandPrefix(IReadOnlyList<string> namespaces, string prefix) =>
        namespaces.Any(ns => prefix.StartsWith(ns, StringComparison.Ordinal));
}
