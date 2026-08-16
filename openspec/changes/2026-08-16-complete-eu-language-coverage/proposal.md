# Complete the EU-24 language coverage

> Converted from the `language-packs-europe` design record (2026-08-16). This is **in-flight work, not
> history** — none of the languages below ships yet, which is why it is a change rather than a spec.

## Why

The shipped set already covers most of Europe's larger languages, but the gaps are not where you would
guess. Romanian is the single biggest missing European language and has a large development community.
Hungarian is a linguistic isolate, so no other pack gets a Hungarian speaker even part of the way.

There is also a milestone worth reaching deliberately rather than by accident. Eleven official EU languages
are missing; adding them completes all twenty-four, which is a claim that can be made in one sentence and
checked by anyone. Partial European coverage is not a claim at all.

## What Changes

- Add the highest-impact European gaps: Romanian, Hungarian, Bulgarian, Croatian, Slovak.
- Add the remainder of the official EU languages: Slovenian, Lithuanian, Latvian, Estonian, Irish, Maltese.
- Adopt "every official EU language ships" as a standing requirement, so the milestone cannot be silently
  lost when a pack is dropped or a key set is reorganised.

Three selection rules decide what belongs and are worth recording, because each rejects an easy mistake:

- **Country-level primary languages only.** Regional variants of a language already shipped are regions,
  not packs — they resolve through their neutral and need no file.
- **Ship a country's language even when a close relative already ships.** Slovak ships despite Czech;
  Croatian ships despite Serbian. Mutual intelligibility is not a reason to tell a country its language is
  a variation of its neighbour's.
- **Europe first**, because that is where the audience is, with EU-official status as the tie-breaker.

## Impact

- Adds a requirement to `language-packs`.
- Each language is one pack file and one manifest entry — no code changes, and the existing coverage checks
  apply unchanged.
- Two of these introduce scripts already handled (Bulgarian is Cyrillic, so access-key markers are omitted
  by the existing script rule); none introduces a new direction.
- Non-EU European languages — Serbian, Albanian, Macedonian, Bosnian, Icelandic, Catalan, Belarusian,
  Luxembourgish — are deliberately not in this change. They are a reasonable next wave and would dilute a
  milestone that is currently one sentence.
