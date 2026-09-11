using System.Text;
using System.Text.RegularExpressions;

namespace HexIDE.Redaction;

/// <summary>
/// Rewrites the four places a captured conversation carries something about the machine it ran on.
/// </summary>
/// <remarks>
/// <b>The surface is enumerable, and that is a finding rather than a simplification.</b> VB6 forms and
/// modules ride an opaque scheme that carries only a component name, so the primary editor's traffic is
/// already free of filesystem paths. Real paths reach the wire through exactly three places — the
/// handshake's root URI, its workspace folders, and the URIs of carried files — and a fourth category is
/// not a path at all: the server launch configuration a user wrote by hand.
///
/// <para>
/// <b>Launch configuration is its own category because no content-agnostic rule would recognise it.</b>
/// Command, arguments, working directory, endpoint and pipe name are user-authored, and a command-line
/// argument is where a token or an internal hostname lives. So an argument is treated as opaque and
/// replaced whole, rather than parsed for things that look like secrets — a rule that only catches the
/// secrets somebody thought of.
/// </para>
///
/// <para>
/// <b>This governs egress, never display.</b> The live view belongs to the person who owns the files and
/// needs no protection from their own paths; redacting it would also break the raw-body affordance, which
/// is the only thing that proves what actually crossed the wire.
/// </para>
///
/// <para>
/// <b>What it deliberately leaves alone.</b> A <c>vb6:</c> URI keeps its component name. That name is the
/// one the developer typed, so it is not nothing — but it is also the only handle an author has on which
/// document a message concerned, and a capture in which every module is <c>hx-toad</c> cannot be read
/// against the project it came from. Recorded here as a known limit rather than an oversight, since it is
/// the obvious first thing to revisit if this is ever pointed at a codebase whose module names are the
/// sensitive part.
/// </para>
/// </remarks>
public sealed partial class ConversationRedactor
{
    /// <summary>
    /// A <c>file:</c> URI as it appears inside a JSON body.
    /// </summary>
    /// <remarks>
    /// Stops at the JSON string delimiter and steps over escape sequences, so a path carrying a
    /// <c>\uXXXX</c> escape is matched whole rather than truncated mid-escape. The matched text is never
    /// decoded: two differently-escaped spellings of one path are meant to produce two different
    /// pseudonyms, because that difference is precisely the kind of defect this preserves.
    /// </remarks>
    [GeneratedRegex(@"file:(?:\\.|[^""\\\s])*", RegexOptions.IgnoreCase)]
    private static partial Regex FileUriInJson();

    private readonly Pseudonymiser _names;

    /// <param name="names">
    /// The session's mapping. Shared across every surface on purpose: the same directory appearing in a
    /// root URI, a document URI and a working directory must come back as the same name, or a reader
    /// cannot tell that the three referred to one place.
    /// </param>
    /// <param name="pseudonymise">
    /// False turns every method below into an identity function, which is the opt-out for a developer who
    /// wants real values and accepts what that means. <b>Strictly non-default, and its state must travel
    /// with anything exported</b> — an export that does not say whether it was redacted is worse than one
    /// that never was, because the reader cannot tell which they are holding.
    /// </param>
    public ConversationRedactor(Pseudonymiser names, bool pseudonymise = true)
    {
        _names = names;
        IsPseudonymising = pseudonymise;
    }

    /// <summary>Whether anything here is actually being rewritten.</summary>
    public bool IsPseudonymising { get; }

    /// <summary>How many distinct values have been given names.</summary>
    public long NamedValues => _names.Assigned;

    /// <summary>
    /// Every <c>file:</c> URI inside a message body, rewritten in place.
    /// </summary>
    /// <remarks>
    /// Textual rather than structural, because the body may be one this client could not decode — which is
    /// exactly when an export is worth having — and because the capture's whole claim is that it holds the
    /// bytes that crossed the wire rather than a re-serialization of a decoded object.
    /// </remarks>
    public string Body(string json)
    {
        if (!IsPseudonymising || json.Length == 0) return json;

        return FileUriInJson().Replace(json, match => Uri(match.Value));
    }

    /// <summary>
    /// One URI. A <c>file:</c> URI is rewritten segment by segment; every other scheme is left alone.
    /// </summary>
    /// <remarks>
    /// <b>Per segment, not per URI, and the drive letter is kept verbatim.</b> Naming a whole URI with one
    /// pseudonym loses the structure a reader needs — that two documents sit in one directory, that a path
    /// is three deep rather than eight — and it loses the case question entirely. A drive letter is not
    /// user content, and its case is the single most expensive normalisation defect this project has
    /// recorded, so it survives untouched.
    ///
    /// <para>
    /// The extension survives too. Routing is by extension here, by specification, so <c>.cls</c> against
    /// <c>.frm</c> is a diagnosis rather than a detail — and an extension is not somebody's name.
    /// </para>
    /// </remarks>
    public string Uri(string uri)
    {
        if (!IsPseudonymising || uri.Length == 0) return uri;
        if (!uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return uri;

        var scheme = uri[.."file:".Length];
        var rest = uri["file:".Length..];

        var segments = rest.Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            var last = i == segments.Length - 1;
            segments[i] = Segment(segments[i], keepExtension: last);
        }

        return scheme + string.Join('/', segments);
    }

    /// <summary>A path on this machine, such as a server's working directory.</summary>
    /// <remarks>
    /// Both separators are handled and each is put back as it was found. A path written with backslashes
    /// coming back with forward ones would be a change to the very thing a reader is examining.
    /// </remarks>
    public string LocalPath(string path)
    {
        if (!IsPseudonymising || path.Length == 0) return path;

        var rebuilt = new StringBuilder(path.Length);
        var segment = new StringBuilder(32);

        foreach (var c in path)
        {
            if (c is '/' or '\\')
            {
                rebuilt.Append(Segment(segment.ToString(), keepExtension: false)).Append(c);
                segment.Clear();
                continue;
            }

            segment.Append(c);
        }

        return rebuilt.Append(Segment(segment.ToString(), keepExtension: true)).ToString();
    }

    /// <summary>
    /// A server's executable, whose directories are rewritten and whose file name is not.
    /// </summary>
    /// <remarks>
    /// <b>The file name is the one thing an author needs and the directories are the part that identifies
    /// a machine.</b> Knowing the server was <c>texlab</c> is most of what makes a capture readable;
    /// knowing which folder somebody keeps it in is not. This does mean an executable whose own name is
    /// sensitive is disclosed, which the opt-in preview exists to catch.
    /// </remarks>
    public string CommandPath(string command)
    {
        if (!IsPseudonymising || command.Length == 0) return command;

        var cut = command.LastIndexOfAny(['/', '\\']);
        return cut < 0 ? command : LocalPath(command[..cut]) + command[cut..];
    }

    /// <summary>
    /// A server's launch arguments: switches kept, everything else replaced whole.
    /// </summary>
    /// <remarks>
    /// A bare switch is protocol and says nothing about the machine — <c>--stdio</c> is worth keeping and
    /// costs nothing. Anything carrying a value is opaque, because the value could be a path, a port, a
    /// hostname or a token, and there is no way to tell which from the outside. Replacing it whole is the
    /// only honest answer; guessing which arguments are safe is how a token ships.
    /// </remarks>
    public IReadOnlyList<string> LaunchArguments(IReadOnlyList<string> arguments)
    {
        if (!IsPseudonymising || arguments.Count == 0) return arguments;

        var result = new string[arguments.Count];
        for (var i = 0; i < arguments.Count; i++)
        {
            result[i] = IsBareSwitch(arguments[i]) ? arguments[i] : _names.For(arguments[i]);
        }

        return result;
    }

    /// <summary>A WebSocket or HTTP endpoint: scheme and port kept, host and path rewritten.</summary>
    /// <remarks>
    /// The port is a protocol detail worth keeping. The host is not: an internal hostname is one of the
    /// two things the design singles out as living in user-authored server configuration.
    /// </remarks>
    public string Endpoint(string endpoint)
    {
        if (!IsPseudonymising || endpoint.Length == 0) return endpoint;
        if (!System.Uri.TryCreate(endpoint, UriKind.Absolute, out var parsed)) return _names.For(endpoint);

        // Loopback names nothing, and hiding it would make every local endpoint look remote — which is a
        // material fact about how a server was reached, not a detail.
        var host = parsed.IsLoopback ? parsed.Host : _names.For(parsed.Host);
        var port = parsed.Port < 0 ? string.Empty : $":{parsed.Port}";
        var path = parsed.AbsolutePath == "/" ? string.Empty : LocalPath(parsed.AbsolutePath);

        return $"{parsed.Scheme}://{host}{port}{path}";
    }

    /// <summary>A named pipe, replaced whole.</summary>
    /// <remarks>
    /// A pipe name has no structure worth keeping and is frequently derived from a project or a user name.
    /// </remarks>
    public string PipeName(string pipe) =>
        !IsPseudonymising || pipe.Length == 0 ? pipe : _names.For(pipe);

    private string Segment(string segment, bool keepExtension)
    {
        if (segment.Length == 0) return segment;
        if (IsDriveLetter(segment)) return segment;

        if (!keepExtension) return _names.For(segment);

        var dot = segment.LastIndexOf('.');

        // A leading dot is the whole name, not an extension: `.gitignore` has nothing to keep.
        return dot <= 0
            ? _names.For(segment)
            : _names.For(segment[..dot]) + segment[dot..];
    }

    /// <summary>A switch with no value attached, which is protocol rather than content.</summary>
    /// <remarks>
    /// Deliberately narrow. A leading separator is allowed because VB6 convention accepts <c>/flag</c>, but
    /// a second one means this is a path wearing a switch's clothes. Anything with an <c>=</c> or a
    /// <c>:</c> carries a value, and a value is opaque.
    /// </remarks>
    private static bool IsBareSwitch(string argument) =>
        argument.Length > 1
        && argument[0] is '-' or '/'
        && !argument.Contains('=')
        && !argument.Contains(':')
        && argument.IndexOf('/', 1) < 0
        && !argument.Contains('\\');

    /// <summary>
    /// A Windows drive designator, percent-encoded or not.
    /// </summary>
    /// <remarks>
    /// Kept verbatim wherever it appears, case included. The case of a drive letter is the single most
    /// expensive normalisation defect recorded in this project, and a redactor that folded the two
    /// spellings together would hide the next one.
    /// </remarks>
    private static bool IsDriveLetter(string segment) =>
        (segment.Length == 2 && char.IsAsciiLetter(segment[0]) && segment[1] == ':')
     || (segment.Length == 4 && char.IsAsciiLetter(segment[0]) && segment[1] == '%'
         && segment[2] == '3' && segment[3] is 'A' or 'a');
}
