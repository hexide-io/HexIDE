namespace HexIDE.Language;

/// <summary>Asks the format first, then applies the IDE's rule when it leaves the decision to the IDE.</summary>
public static class ContentReplacementRules
{
    /// <param name="prefix">The current file prefix split from the document model's code.</param>
    public static ContentReplacement Replace(string current, string prefix, string incoming,
        IReadOnlyRegionReader reader, IReadOnlyRegionRules? rules)
    {
        var prefixLength = prefix.Length;
        if (rules?.Replace(current, prefixLength, incoming) is { } replacement)
            return replacement;

        var nextPrefix = incoming.StartsWith(prefix, StringComparison.Ordinal) ? prefixLength : 0;
        var regions = reader.Regions(current, prefixLength);
        var nextRegions = reader.Regions(incoming, nextPrefix);
        var hanging = reader.HangingRegions(current, prefixLength);
        var nextHanging = reader.HangingRegions(incoming, nextPrefix);
        var texts = regions.Select(r => current[r.Start..r.End]).ToArray();
        var nextTexts = nextRegions.Select(r => incoming[r.Start..r.End]).ToArray();
        if (texts.SequenceEqual(nextTexts)
            && hanging.Select(h => h.Key).SequenceEqual(nextHanging.Select(h => h.Key)))
            return new(incoming, null, false);

        var changed = new List<TextRegion>();
        var next = 0;
        for (var i = 0; i < regions.Count; i++)
        {
            var match = Array.IndexOf(nextTexts, texts[i], next);
            if (match < 0)
                changed.Add(regions[i]);
            else
            {
                next = match + 1;
                if (hanging.FirstOrDefault(h => h.Region == regions[i]) is { } attached
                    && nextHanging.FirstOrDefault(h => h.Region == nextRegions[match])?.Key != attached.Key)
                    changed.Add(regions[i]);
            }
        }
        var ordered = regions.Where(changed.Contains).ToArray();
        var unmatched = texts.ToList();
        var added = nextRegions.Where((_, i) => !unmatched.Remove(nextTexts[i])).ToArray();
        var phrase = ordered.Length == 0 ? null : rules?.DescribeRegions(current, prefixLength, ordered);
        if (phrase is null && added.Length > 0)
            phrase = rules?.DescribeRegions(incoming, nextPrefix, added);
        return new(null, $"The content would change {phrase ?? "read-only text"}, which only the IDE changes. Nothing was changed.", false);
    }
}
