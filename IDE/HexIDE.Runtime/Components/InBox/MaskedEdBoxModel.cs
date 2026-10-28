using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Components.InBox;

/// <summary>The code-assigned text of MSMask.MaskEdBox (#1098), measured in the MaskedEdBox oracle.</summary>
internal sealed class MaskedEdBoxModel
{
    private sealed record Place(char Kind, char Literal, int Case);
    private readonly List<Place> places = [];
    private string mask = "";
    private string text = "";
    private char prompt = '_';
    private int maxLength = 64;
    private int clipMode;
    private int start;
    private int length;

    internal event Action? Changed;
    internal event Action<string, int>? ValidationError;
    internal void TypeCharacter(char character)
    {
        if (character > 127) throw new NotImplementedException("Non-ASCII MaskEdBox input is unmeasured (#1098)");
        if (AutoTab) throw new NotImplementedException("MaskEdBox AutoTab input is not implemented (#1098)");
        if (mask.Length == 0)
        {
            var next = text.Remove(start, length);
            if (next.Length >= maxLength) return;
            text = next.Insert(start, character.ToString());
            start++;
            length = 0;
            Changed?.Invoke();
            return;
        }
        var candidate = text.ToCharArray();
        if (length > 0 && !Pull(candidate, start, length)) return;
        var at = start;
        if (at < places.Count && places[at].Kind == '\0' && character == places[at].Literal)
        {
            start = Math.Min(at + 1 + (at == 0 ? 0 : 1), places.Count);
            length = 0;
            text = new string(candidate);
            Changed?.Invoke();
            return;
        }
        while (at < places.Count && places[at].Kind == '\0') at++;
        if (at == places.Count) { ValidationError?.Invoke(new string(candidate), at); return; }
        char converted;
        try
        {
            if (character == prompt && !AllowPrompt) throw Invalid();
            converted = ConvertCharacter(places[at], character, typing: true);
        }
        catch (VBRunTimeException)
        {
            candidate[at] = character;
            ValidationError?.Invoke(new string(candidate), at);
            return;
        }
        var carry = converted;
        var room = false;
        for (var i = at; i < places.Count; i++)
        {
            if (places[i].Kind == '\0') continue;
            var old = candidate[i];
            candidate[i] = carry;
            if (old == prompt) { room = true; break; }
            carry = old;
        }
        if (!room) { ValidationError?.Invoke(new string(candidate), places.Count); return; }
        text = new string(candidate);
        start = at + 1;
        while (start < places.Count && places[start].Kind == '\0') start++;
        length = 0;
        Changed?.Invoke();
    }

    internal void Remove(bool backspace)
    {
        var at = start;
        if (length == 0 && backspace)
        {
            do { at--; } while (at >= 0 && mask.Length != 0 && places[at].Kind == '\0');
            if (at < 0) return;
        }
        if (at >= text.Length) return;
        if (mask.Length == 0) text = text.Remove(at, length == 0 ? 1 : length);
        else
        {
            var candidate = text.ToCharArray();
            if (!Pull(candidate, at, length == 0 ? 1 : length)) return;
            text = new string(candidate);
        }
        start = at;
        length = 0;
        Changed?.Invoke();
    }

    private bool Pull(char[] candidate, int at, int count)
    {
        // Deleting on a literal is refused with the raw shifted prefix, as measured.
        if (places[at].Kind == '\0')
        {
            var invalid = candidate.Take(at).Concat(candidate.Skip(at + count).Take(1))
                .Concat(Enumerable.Repeat(prompt, places.Count)).Take(places.Count).ToArray();
            ValidationError?.Invoke(new string(invalid), at);
            return false;
        }
        var remaining = new Queue<char>(candidate.Where((c, i) => i >= at + count && places[i].Kind != '\0' && c != prompt));
        for (var i = at; i < places.Count; i++)
        {
            if (places[i].Kind == '\0') continue;
            var c = remaining.Count == 0 ? prompt : remaining.Dequeue();
            try { candidate[i] = ConvertCharacter(places[i], c, typing: true); }
            catch (VBRunTimeException)
            {
                var invalid = new string(candidate.Take(i).Concat(new[] { c })
                    .Concat(Enumerable.Repeat(prompt, places.Count)).Take(places.Count).ToArray());
                ValidationError?.Invoke(invalid, i);
                return false;
            }
        }
        return true;
    }

    internal bool PromptInclude { get; set; } = true;
    internal bool AllowPrompt { get; set; }
    internal bool AutoTab { get; set; }

    internal string Mask
    {
        get => mask;
        set
        {
            if (value.Length == 0)
                throw new NotImplementedException("Clearing a MaskEdBox mask is unmeasured (#1098)");
            // The count and intermediate readings when replacing nonempty Text need the full property log.
            if (text.Length != 0)
                throw new NotImplementedException("Replacing a MaskEdBox mask with nonempty Text is not implemented (#1098)");
            var parsed = new List<Place>();
            var casing = 0;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c is '>' or '<') { casing = c == '>' ? 1 : -1; continue; }
                if (c == '\\')
                {
                    if (++i == value.Length)
                        throw new NotImplementedException("A trailing MaskEdBox mask escape is unmeasured (#1098)");
                    parsed.Add(new Place('\0', value[i], 0));
                }
                else if ("#9?Aa&C".Contains(c)) parsed.Add(new Place(c, '\0', casing));
                else if ("() -.:/,".Contains(c)) parsed.Add(new Place('\0', c, 0));
                else throw new NotImplementedException("This MaskEdBox mask character is unmeasured (#1098)");
            }
            places.Clear();
            places.AddRange(parsed);
            mask = value;
            text = new string(places.Select(p => p.Kind == '\0' ? p.Literal : prompt).ToArray());
            maxLength = text.Length;
            start = length = 0;
            Changed?.Invoke();
        }
    }

    internal string PromptChar
    {
        get => prompt.ToString();
        set
        {
            if (value.Length != 1) throw Invalid();
            if (value[0] != prompt)
                throw new NotImplementedException("Changing a MaskEdBox prompt character is not implemented (#1098)");
            Changed?.Invoke();
        }
    }

    internal int MaxLength
    {
        get => maxLength;
        set
        {
            if (value == 0) throw Invalid();
            if (value < 0) throw new NotImplementedException("Negative MaskEdBox MaxLength is unmeasured (#1098)");
            maxLength = value;
        }
    }

    internal int ClipMode
    {
        get => clipMode;
        set
        {
            if (value == 2) throw Invalid();
            if (value is not (0 or 1)) throw new NotImplementedException("This MaskEdBox ClipMode is unmeasured (#1098)");
            clipMode = value;
        }
    }

    internal string Text
    {
        get => PromptInclude || mask.Length == 0 ? text : ClipText;
        set
        {
            try
            {
                maxLength = Math.Max(maxLength, value.Length);
                if (mask.Length == 0) { text = value; start = length = 0; return; }
                var next = places.Select(p => p.Kind == '\0' ? p.Literal : prompt).ToArray();
                var at = 0;
                foreach (var c in value)
                {
                    if (!PromptInclude)
                    {
                        if (places.Any(p => p.Kind == '\0' && p.Literal == c)) continue;
                        while (at < places.Count && places[at].Kind == '\0') at++;
                    }
                    if (at >= places.Count) throw Invalid();
                    var place = places[at];
                    if (place.Kind == '\0')
                    {
                        if (c != place.Literal) throw Invalid();
                    }
                    else next[at] = ConvertCharacter(place, c);
                    at++;
                }
                if (PromptInclude && at != places.Count) throw Invalid();
                text = new string(next);
                start = length = 0;
            }
            finally { Changed?.Invoke(); }
        }
    }

    internal string DisplayText => text;

    internal string ClipText => mask.Length == 0 ? text : new string(text.Where((c, i) => places[i].Kind != '\0' && c != prompt).ToArray());
    internal string FormattedText => mask.Length == 0 ? text : new string(text.Select((c, i) => places[i].Kind != '\0' && c == prompt ? ' ' : c).ToArray());

    internal int SelStart
    {
        get => start;
        set { if (value < 0) throw Invalid(); start = Math.Min(value, text.Length); length = 0; }
    }
    internal int SelLength
    {
        get => length;
        set { if (value < 0) throw Invalid(); length = Math.Min(value, text.Length - start); }
    }
    internal string SelText => clipMode == 0 || mask.Length == 0 ? text.Substring(start, length)
        : new string(text.Where((c, i) => i >= start && i < start + length && places[i].Kind != '\0' && c != prompt).ToArray());

    internal void SetSelectedText(string value)
    {
        // The selection-write table measured ##-##; other masks and prompts need their own probes.
        if (mask != "##-##" || start == 2 || value.Any(c => c > 127 || c == prompt))
            throw new NotImplementedException("This MaskEdBox SelText assignment is unmeasured (#1098)");
        var candidate = text.ToCharArray();
        if (length > 0 && !Pull(candidate, start, length)) return;
        var at = start;
        foreach (var character in value)
        {
            if (character == '-') continue;
            while (at < places.Count && places[at].Kind == '\0') at++;
            if (at == places.Count) break;
            char converted;
            try { converted = ConvertCharacter(places[at], character, typing: true); }
            catch (VBRunTimeException) { continue; }
            var carry = converted;
            for (var i = at; i < places.Count; i++)
            {
                if (places[i].Kind == '\0') continue;
                var old = candidate[i];
                candidate[i] = carry;
                if (old == prompt) break;
                carry = old;
            }
            at++;
        }
        text = new string(candidate);
        start = length = 0;
        Changed?.Invoke();
    }

    internal string CopySelection(bool cut)
    {
        if (mask != "##-##" || length == 0 || cut && start == 2)
            throw new NotImplementedException("This MaskEdBox clipboard selection is unmeasured (#1098)");
        var selected = SelText;
        if (cut) Remove(false);
        return selected;
    }

    internal void Paste(string value)
    {
        if (AutoTab || value.Any(c => c > 127 || c == prompt)
            || mask.Length != 0 && mask != "##-##" || start == 2 && mask.Length != 0)
            throw new NotImplementedException("This MaskEdBox paste is unmeasured (#1098)");
        if (mask.Length == 0)
        {
            if (length != 0 || text.Length + value.Length > maxLength)
                throw new NotImplementedException("This unmasked MaskEdBox paste is unmeasured (#1098)");
            text = text.Insert(start, value);
            start += value.Length;
            Changed?.Invoke();
            return;
        }
        var candidate = text.ToCharArray();
        if (length > 0 && !Pull(candidate, start, length)) return;
        var at = start;
        var accepted = false;
        var overflow = false;
        string? invalid = null;
        foreach (var character in value)
        {
            if (character == '-') continue;
            while (at < places.Count && places[at].Kind == '\0') at++;
            if (at == places.Count) { overflow = true; break; }
            char converted;
            try { converted = ConvertCharacter(places[at], character, typing: true); }
            catch (VBRunTimeException)
            {
                var refused = (char[])candidate.Clone();
                refused[at] = character;
                invalid = new string(refused);
                continue;
            }
            accepted = true;
            var carry = converted;
            var room = false;
            for (var i = at; i < places.Count; i++)
            {
                if (places[i].Kind == '\0') continue;
                var old = candidate[i];
                candidate[i] = carry;
                if (old == prompt) { room = true; break; }
                carry = old;
            }
            overflow |= !room;
            at++;
        }
        while (at < places.Count && places[at].Kind == '\0') at++;
        text = new string(candidate);
        start = at;
        length = 0;
        Changed?.Invoke();
        // Paste reports a single validation error, including the last refused character when none fitted.
        if (overflow) ValidationError?.Invoke(text, places.Count);
        else if (!accepted && invalid is not null) ValidationError?.Invoke(invalid, at);
    }

    private char ConvertCharacter(Place place, char c, bool typing = false)
    {
        if (c == prompt) return c;
        // The probes used ASCII input. Unicode classes and locale-sensitive separators still need measurement.
        if (c > 127) throw new NotImplementedException("Non-ASCII MaskEdBox input is unmeasured (#1098)");
        if (!typing && place.Kind == '9' && c is '+' or '-')
            throw new NotImplementedException("Signs assigned to a MaskEdBox 9 place are unmeasured (#1098)");
        var digit = c is >= '0' and <= '9';
        var letter = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
        var accepted = place.Kind switch
        {
            '#' => digit,
            '9' => digit || c == ' ' || typing && c == '+',
            '?' => letter,
            'A' => letter || digit,
            'a' => letter || digit || c == ' ',
            '&' or 'C' => true,
            _ => false,
        };
        if (!accepted) throw Invalid();
        return place.Case switch { 1 => char.ToUpperInvariant(c), -1 => char.ToLowerInvariant(c), _ => c };
    }

    private static VBRunTimeException Invalid() => new(VBStandardError.InvalidPropertyValue);
}
