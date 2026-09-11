using System.Security.Cryptography;

namespace HexIDE.Redaction;

/// <summary>
/// Gives each distinct value a stable, harmless name for the life of one session.
/// </summary>
/// <remarks>
/// <b>Pseudonymisation, not removal, and the difference is the whole point.</b> Replacing every path with
/// one placeholder destroys the exact defect class this project has already paid for: a client sending one
/// drive-letter case and a server echoing another was invisible for as long as it existed, and it becomes
/// invisible again the moment both strings turn into the same token. A redactor that makes traces safe and
/// useless gets switched off, which protects nobody.
///
/// <para>
/// <b>So two values that differ only in case share a word and differ in capitalisation.</b> The mapping is
/// keyed on the case-folded value, and the original's case shape is projected onto the name — so
/// <c>Projects</c> and <c>projects</c> come back as <c>Hx-Toad</c> and <c>hx-toad</c>: plainly the same
/// path, plainly not the same string. That is the signal a normalisation bug is made of, preserved through
/// redaction rather than laundered out of it.
/// </para>
///
/// <para>
/// <b>Where that projection cannot be faithful, it says so instead of colliding.</b> Three shapes are
/// representable — all lower, all upper, leading capital — and a value whose casing is none of those
/// cannot be told apart from its neighbour by the name alone. Rather than let two different originals
/// render identically, the second gets a suffix. An ugly name is a much smaller problem than two distinct
/// strings that a reader believes are one.
/// </para>
///
/// <para>
/// <b>Nothing survives the session.</b> The permutation is drawn from the system's cryptographic generator
/// at construction and is never written anywhere, so the mapping is deterministic while the IDE is running
/// and irrecoverable afterwards. This matters because the weakness of any tokenisation scheme is
/// correlation across captures: two traces from the same machine naming the same path the same way is how
/// a pseudonym stops being one. Session scope is what bounds that, and it is bounded by construction here
/// rather than by policy.
/// </para>
/// </remarks>
public sealed class Pseudonymiser
{
    private readonly int[] _permutation;
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _indices = new(StringComparer.Ordinal);
    private readonly HashSet<string> _issued = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private long _assigned;

    /// <summary>A fresh mapping, unrelated to every other one this machine has ever produced.</summary>
    public Pseudonymiser()
    {
        _permutation = Shuffled(index => RandomNumberGenerator.GetInt32(index + 1));
    }

    /// <summary>A mapping whose permutation is reproducible. For tests, and only for tests.</summary>
    /// <remarks>
    /// <b>The public constructor does not go anywhere near <see cref="Random"/>.</b> A pseudonym's job
    /// includes hiding which pool word a value drew, and a shuffle seeded from a 32-bit value narrows the
    /// space of permutations to something enumerable. Tests need determinism; a session does not.
    /// </remarks>
    internal Pseudonymiser(Random deterministic)
    {
        _permutation = Shuffled(index => deterministic.Next(index + 1));
    }

    /// <summary>How many distinct values have been named.</summary>
    /// <remarks>
    /// Read by the disclosure summary, which has to size what is about to leave in terms a person can
    /// weigh. A count of distinct paths is part of that; a count of messages is not.
    /// </remarks>
    public long Assigned
    {
        get { lock (_gate) return _assigned; }
    }

    /// <summary>The name for a value, the same one every time within this session.</summary>
    public string For(string value)
    {
        if (value.Length == 0) return value;

        lock (_gate)
        {
            if (_names.TryGetValue(value, out var known)) return known;

            var folded = value.ToLowerInvariant();
            if (!_indices.TryGetValue(folded, out var index))
            {
                index = _assigned++;
                _indices[folded] = index;
            }

            var name = ProjectCase(value, PseudonymPool.NameOf(index, _permutation));

            // Two originals folding to the same key whose casing the three representable shapes cannot
            // distinguish. Suffix rather than collide: a reader who sees one name must never be looking at
            // two different strings.
            if (!_issued.Add(name))
            {
                var attempt = 2;
                string candidate;
                do { candidate = $"{name}~{attempt++}"; } while (!_issued.Add(candidate));
                name = candidate;
            }

            _names[value] = name;
            return name;
        }
    }

    /// <summary>
    /// The original's case shape, applied to the name's words.
    /// </summary>
    /// <remarks>
    /// The prefix stays lowercase whatever happens, so a pseudonym is recognisable as one at a glance and
    /// greppable with a single case-sensitive pattern.
    /// </remarks>
    private static string ProjectCase(string original, string name)
    {
        var words = name[PseudonymPool.Prefix.Length..];

        var letters = 0;
        var upper = 0;
        foreach (var c in original)
        {
            if (!char.IsLetter(c)) continue;
            letters++;
            if (char.IsUpper(c)) upper++;
        }

        if (letters == 0 || upper == 0) return name;

        if (upper == letters && letters > 1) return PseudonymPool.Prefix + words.ToUpperInvariant();

        return char.IsUpper(FirstLetter(original))
            ? PseudonymPool.Prefix + char.ToUpperInvariant(words[0]) + words[1..]
            : name;
    }

    private static char FirstLetter(string value)
    {
        foreach (var c in value)
        {
            if (char.IsLetter(c)) return c;
        }

        return '\0';
    }

    /// <summary>A full Fisher-Yates shuffle of the pool's indices.</summary>
    /// <remarks>
    /// <b>A full shuffle rather than an arithmetic scramble.</b> A multiply-and-add over the index — the
    /// obvious cheap alternative — is a permutation, but a linear one: two consecutive values land a fixed
    /// distance apart in the pool, so anyone holding the pool file and two names can recover the stride and
    /// then read off every other name in the session. Fisher-Yates has no such structure to find.
    /// </remarks>
    private static int[] Shuffled(Func<int, int> nextInclusive)
    {
        var order = new int[PseudonymPool.Count];
        for (var i = 0; i < order.Length; i++) order[i] = i;

        for (var i = order.Length - 1; i > 0; i--)
        {
            var j = nextInclusive(i);
            (order[i], order[j]) = (order[j], order[i]);
        }

        return order;
    }
}
