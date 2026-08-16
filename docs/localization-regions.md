# Localization regions

Canonical catalog of the language/region packs HexIDE recognizes, and their status. This is the
human-readable reference; the code source of truth for what actually ships is
`IDE/HexIDE/Localization/LanguageManifest.Packs` (which drives the **Options → Environment → Language**
dropdown). Keep the two in sync.

See the [localization system](../openspec/specs/localization/spec.md) spec for the engine/region scheme,
and the [language packs](../openspec/specs/language-packs/spec.md) spec for the per-language content this
catalog tracks.

## How packs and regions work

- **Pack ids are BCP-47** — a neutral language (`en`, `fr`, `zh-Hans`) or `language-REGION` (`en-GB`,
  `fr-FR`). One JSON file per pack at `IDE/HexIDE/Localization/Packs/{id}.json`.
- **`en` is the canonical pack** — the single key-complete source of truth and the universal fallback
  (US-spelled neutral English; renamed from `en-US` in P17). Every other pack lists only the keys it overrides.
- **The picker is two cascading combos (P17):** a **Language** combo (one entry per translation) drives a
  dependent **Region** combo (a `(Standard)` entry = the bare neutral, plus that language's regions).
  "System default" and the dev-only pseudo entries disable the Region combo.
- **3-tier fallback** — *region → neutral language → canonical*, the neutral layer **optional**:
  - `resolve(fr-CA)` = `en` ← `fr` (neutral, if present) ← `fr-CA`
  - `resolve(fr)` = `en` ← `fr`
  - `resolve(en-GB)` = `en` ← `en-GB`

  A key absent from a pack inherits its parent chain, so a control can never render blank. The neutral
  layer barely matters for English variants but is essential for real languages: every `fr-*` shares one
  French translation in the neutral **`fr`** pack, and regional packs hold only their (often zero) tweaks.
- **Most region packs are fileless (P17).** A region with no overrides ships **no JSON file** — it resolves
  through its neutral, and the Region combo is populated from a data-driven `LanguageManifest.Regions` map,
  not from files. Only packs with real content exist on disk: the 28 neutral translations and `en-GB`. (The
  91 previously-empty regional placeholder packs were deleted.) The catalog still lists every region below;
  the **Status** column flags which ship a file vs. inherit fileless (`empty → {neutral}`).
- **Region = strings/terminology only.** Packs change spelling and wording (`Colour`/`Color`,
  `Customise`/`Customize`, `Centre`/`Center`) — **not** number, date, or currency formatting. HexIDE keeps
  its invariant-culture lock (for stable VB6 numeric parsing); locale-aware *runtime* formatting is a
  separate, out-of-scope axis.
- **OS detection (`"system"`):** (1) exact neutral-language match of `CultureInfo.CurrentUICulture.Name`
  (e.g. `fr` → `fr`); else (2) an **exact region** match against the regions map (e.g. `en-GB` → `en-GB`,
  so a British machine gets Colour/Customise; `zh-CN` → `zh-CN`); else (3) a **neutral-language** match on
  the two-letter code (e.g. `fr-CD` → `fr`); else (4) canonical `en`.

## Status legend

| Status | Meaning |
|--------|---------|
| **Implemented** | A `{id}.json` pack ships and is listed in `LanguageManifest`. |
| **Planned** | Intended next; no pack file yet. |
| **Recognized** | A valid region we acknowledge as a future candidate; no pack and not yet planned. |
| **Not started** | A BCP-47 neutral culture .NET can OS-detect, with no pack yet (see the backlog below). |

## Catalog

> **Home** = the ISO 3166-1 country .NET resolves as a language’s default region —
> `CultureInfo.CreateSpecificCulture(code)` → `RegionInfo` (`TwoLetterISORegionName` + `EnglishName`),
> i.e. Unicode CLDR’s *likely region*. `—` means .NET yields no single ISO country (constructed/stateless
> languages resolve to UN region `001`, or have no specific culture). It is **mechanically derived, not an
> editorial or political judgement** — and so e.g. `en-GB`’s home is .NET’s `GB (United Kingdom)`, `ar`’s
> is `SA (Saudi Arabia)`, `pt`’s is `BR (Brazil)`. Regenerate with the same call if the platform CLDR updates.

| Code | English name | Native name | Home | Direction | Status |
|------|--------------|-------------|------|-----------|--------|
| `en` | English (neutral) | English | US (United States) | LTR | **Implemented** (canonical) |
| `en-US` | English (United States) | English (United States) | US (United States) | LTR | **Implemented** (empty → `en`) |
| `en-GB` | English (United Kingdom) | English (United Kingdom) | GB (United Kingdom) | LTR | **Implemented** (full variant) |
| `en-CA` | English (Canada) | English (Canada) | CA (Canada) | LTR | **Implemented** (empty → `en`) |
| `en-AU` | English (Australia) | English (Australia) | AU (Australia) | LTR | **Implemented** (empty → `en`) |
| `en-IN` | English (India) | English (India) | IN (India) | LTR | **Implemented** (empty → `en`) |
| `en-NG` | English (Nigeria) | English (Nigeria) | NG (Nigeria) | LTR | **Implemented** (empty → `en`) |
| `en-PH` | English (Philippines) | English (Philippines) | PH (Philippines) | LTR | **Implemented** (empty → `en`) |
| `en-ZA` | English (South Africa) | English (South Africa) | ZA (South Africa) | LTR | **Implemented** (empty → `en`) |
| `en-IE` | English (Ireland) | English (Ireland) | IE (Ireland) | LTR | **Implemented** (empty → `en`) |
| `en-NZ` | English (New Zealand) | English (New Zealand) | NZ (New Zealand) | LTR | **Implemented** (empty → `en`) |
| `en-SG` | English (Singapore) | English (Singapore) | SG (Singapore) | LTR | **Implemented** (empty → `en`) |
| `fr` | French (neutral) | Français | FR (France) | LTR | **Implemented** (full translation) |
| `fr-FR` | French (France) | Français (France) | FR (France) | LTR | **Implemented** (empty → `fr`) |
| `fr-CA` | French (Canada) | Français (Canada) | CA (Canada) | LTR | **Implemented** (empty → `fr`) |
| `fr-BE` | French (Belgium) | Français (Belgique) | BE (Belgium) | LTR | **Implemented** (empty → `fr`) |
| `fr-CH` | French (Switzerland) | Français (Suisse) | CH (Switzerland) | LTR | **Implemented** (empty → `fr`) |
| `fr-LU` | French (Luxembourg) | Français (Luxembourg) | LU (Luxembourg) | LTR | **Implemented** (empty → `fr`) |
| `fr-MC` | French (Monaco) | Français (Monaco) | MC (Monaco) | LTR | **Implemented** (empty → `fr`) |
| `fr-CD` | French (Congo [DRC]) | français (Congo [République démocratique du]) | CD (Congo (DRC)) | LTR | **Implemented** (empty → `fr`) |
| `fr-CI` | French (Côte d’Ivoire) | français (Côte d’Ivoire) | CI (Côte d’Ivoire) | LTR | **Implemented** (empty → `fr`) |
| `fr-SN` | French (Senegal) | français (Sénégal) | SN (Senegal) | LTR | **Implemented** (empty → `fr`) |
| `fr-CM` | French (Cameroon) | français (Cameroun) | CM (Cameroon) | LTR | **Implemented** (empty → `fr`) |
| `fr-MA` | French (Morocco) | français (Maroc) | MA (Morocco) | LTR | **Implemented** (empty → `fr`) |
| `fr-DZ` | French (Algeria) | français (Algérie) | DZ (Algeria) | LTR | **Implemented** (empty → `fr`) |
| `fr-HT` | French (Haiti) | français (Haïti) | HT (Haiti) | LTR | **Implemented** (empty → `fr`) |
| `ar` | Arabic (neutral) | العربية | SA (Saudi Arabia) | RTL | **Implemented** (full translation) |
| `ar-SA` | Arabic (Saudi Arabia) | العربية (السعودية) | SA (Saudi Arabia) | RTL | **Implemented** (empty → `ar`) |
| `ar-EG` | Arabic (Egypt) | العربية (مصر) | EG (Egypt) | RTL | **Implemented** (empty → `ar`) |
| `ar-AE` | Arabic (U.A.E.) | العربية (الإمارات) | AE (United Arab Emirates) | RTL | **Implemented** (empty → `ar`) |
| `ar-MA` | Arabic (Morocco) | العربية (المغرب) | MA (Morocco) | RTL | **Implemented** (empty → `ar`) |
| `ar-DZ` | Arabic (Algeria) | العربية (الجزائر) | DZ (Algeria) | RTL | **Implemented** (empty → `ar`) |
| `ar-IQ` | Arabic (Iraq) | العربية (العراق) | IQ (Iraq) | RTL | **Implemented** (empty → `ar`) |
| `ar-KW` | Arabic (Kuwait) | العربية (الكويت) | KW (Kuwait) | RTL | **Implemented** (empty → `ar`) |
| `ar-QA` | Arabic (Qatar) | العربية (قطر) | QA (Qatar) | RTL | **Implemented** (empty → `ar`) |
| `ar-BH` | Arabic (Bahrain) | العربية (البحرين) | BH (Bahrain) | RTL | **Implemented** (empty → `ar`) |
| `ar-OM` | Arabic (Oman) | العربية (عُمان) | OM (Oman) | RTL | **Implemented** (empty → `ar`) |
| `ar-JO` | Arabic (Jordan) | العربية (الأردن) | JO (Jordan) | RTL | **Implemented** (empty → `ar`) |
| `ar-LB` | Arabic (Lebanon) | العربية (لبنان) | LB (Lebanon) | RTL | **Implemented** (empty → `ar`) |
| `ar-YE` | Arabic (Yemen) | العربية (اليمن) | YE (Yemen) | RTL | **Implemented** (empty → `ar`) |
| `ar-SY` | Arabic (Syria) | العربية (سوريا) | SY (Syria) | RTL | **Implemented** (empty → `ar`) |
| `ar-TN` | Arabic (Tunisia) | العربية (تونس) | TN (Tunisia) | RTL | **Implemented** (empty → `ar`) |
| `ar-LY` | Arabic (Libya) | العربية (ليبيا) | LY (Libya) | RTL | **Implemented** (empty → `ar`) |
| `ar-SD` | Arabic (Sudan) | العربية (السودان) | SD (Sudan) | RTL | **Implemented** (empty → `ar`) |
| `ar-PS` | Arabic (Palestinian Authority) | العربية (السلطة الفلسطينية) | PS (Palestinian Authority) | RTL | **Implemented** (empty → `ar`) |
| `zh-Hans` | Chinese (Simplified, neutral) | 简体中文 | CN (China) | LTR | **Implemented** (full translation) |
| `zh-CN` | Chinese (China) | 中文 (中国) | CN (China) | LTR | **Implemented** (empty → `zh-Hans`) |
| `zh-SG` | Chinese (Singapore) | 中文 (新加坡) | SG (Singapore) | LTR | **Implemented** (empty → `zh-Hans`) |
| `zh-Hant` | Chinese (Traditional, neutral) | 繁體中文 | HK (Hong Kong SAR) | LTR | **Implemented** (full translation) |
| `zh-TW` | Chinese (Taiwan) | 中文 (台灣) | TW (Taiwan) | LTR | **Implemented** (empty → `zh-Hant`) |
| `zh-HK` | Chinese (Hong Kong) | 中文 (香港) | HK (Hong Kong SAR) | LTR | **Implemented** (empty → `zh-Hant`) |
| `zh-MO` | Chinese (Macau) | 中文 (澳門) | MO (Macao SAR) | LTR | **Implemented** (empty → `zh-Hant`) |
| `de` | German (neutral) | Deutsch | DE (Germany) | LTR | **Implemented** (full translation) |
| `de-DE` | German (Germany) | Deutsch (Deutschland) | DE (Germany) | LTR | **Implemented** (empty → `de`) |
| `de-AT` | German (Austria) | Deutsch (Österreich) | AT (Austria) | LTR | **Implemented** (empty → `de`) |
| `de-CH` | German (Switzerland) | Deutsch (Schweiz) | CH (Switzerland) | LTR | **Implemented** (empty → `de`) |
| `de-LU` | German (Luxembourg) | Deutsch (Luxemburg) | LU (Luxembourg) | LTR | **Implemented** (empty → `de`) |
| `de-LI` | German (Liechtenstein) | Deutsch (Liechtenstein) | LI (Liechtenstein) | LTR | **Implemented** (empty → `de`) |
| `de-BE` | German (Belgium) | Deutsch (Belgien) | BE (Belgium) | LTR | **Implemented** (empty → `de`) |
| `es` | Spanish (neutral) | Español | ES (Spain) | LTR | **Implemented** (full translation) |
| `es-ES` | Spanish (Spain) | Español (España) | ES (Spain) | LTR | **Implemented** (empty → `es`) |
| `es-MX` | Spanish (Mexico) | Español (México) | MX (Mexico) | LTR | **Implemented** (empty → `es`) |
| `es-AR` | Spanish (Argentina) | Español (Argentina) | AR (Argentina) | LTR | **Implemented** (empty → `es`) |
| `es-CO` | Spanish (Colombia) | Español (Colombia) | CO (Colombia) | LTR | **Implemented** (empty → `es`) |
| `es-CL` | Spanish (Chile) | Español (Chile) | CL (Chile) | LTR | **Implemented** (empty → `es`) |
| `es-US` | Spanish (United States) | español (Estados Unidos) | US (United States) | LTR | **Implemented** (empty → `es`) |
| `es-PE` | Spanish (Peru) | español (Perú) | PE (Peru) | LTR | **Implemented** (empty → `es`) |
| `es-VE` | Spanish (Venezuela) | español (Venezuela) | VE (Venezuela) | LTR | **Implemented** (empty → `es`) |
| `es-EC` | Spanish (Ecuador) | español (Ecuador) | EC (Ecuador) | LTR | **Implemented** (empty → `es`) |
| `es-GT` | Spanish (Guatemala) | español (Guatemala) | GT (Guatemala) | LTR | **Implemented** (empty → `es`) |
| `es-DO` | Spanish (Dominican Republic) | español (República Dominicana) | DO (Dominican Republic) | LTR | **Implemented** (empty → `es`) |
| `es-CR` | Spanish (Costa Rica) | español (Costa Rica) | CR (Costa Rica) | LTR | **Implemented** (empty → `es`) |
| `es-PR` | Spanish (Puerto Rico) | español (Puerto Rico) | PR (Puerto Rico) | LTR | **Implemented** (empty → `es`) |
| `es-BO` | Spanish (Bolivia) | español (Bolivia) | BO (Bolivia) | LTR | **Implemented** (empty → `es`) |
| `es-CU` | Spanish (Cuba) | español (Cuba) | CU (Cuba) | LTR | **Implemented** (empty → `es`) |
| `es-GQ` | Spanish (Equatorial Guinea) | español (Guinea Ecuatorial) | GQ (Equatorial Guinea) | LTR | **Implemented** (empty → `es`) |
| `es-HN` | Spanish (Honduras) | español (Honduras) | HN (Honduras) | LTR | **Implemented** (empty → `es`) |
| `es-NI` | Spanish (Nicaragua) | español (Nicaragua) | NI (Nicaragua) | LTR | **Implemented** (empty → `es`) |
| `es-PA` | Spanish (Panama) | español (Panamá) | PA (Panama) | LTR | **Implemented** (empty → `es`) |
| `es-PY` | Spanish (Paraguay) | español (Paraguay) | PY (Paraguay) | LTR | **Implemented** (empty → `es`) |
| `es-SV` | Spanish (El Salvador) | español (El Salvador) | SV (El Salvador) | LTR | **Implemented** (empty → `es`) |
| `es-UY` | Spanish (Uruguay) | español (Uruguay) | UY (Uruguay) | LTR | **Implemented** (empty → `es`) |
| `ja` | Japanese (neutral) | 日本語 | JP (Japan) | LTR | **Implemented** (full translation) |
| `ja-JP` | Japanese (Japan) | 日本語 (日本) | JP (Japan) | LTR | **Implemented** (empty → `ja`) |
| `he` | Hebrew (neutral) | עברית | IL (Israel) | RTL | **Implemented** (full translation) |
| `he-IL` | Hebrew (Israel) | עברית (ישראל) | IL (Israel) | RTL | **Implemented** (empty → `he`) |
| `cs` | Czech (neutral) | Čeština | CZ (Czechia) | LTR | **Implemented** (full translation) |
| `cs-CZ` | Czech (Czechia) | Čeština (Česko) | CZ (Czechia) | LTR | **Implemented** (empty → `cs`) |
| `it` | Italian (neutral) | Italiano | IT (Italy) | LTR | **Implemented** (full translation) |
| `it-IT` | Italian (Italy) | Italiano (Italia) | IT (Italy) | LTR | **Implemented** (empty → `it`) |
| `it-CH` | Italian (Switzerland) | Italiano (Svizzera) | CH (Switzerland) | LTR | **Implemented** (empty → `it`) |
| `it-SM` | Italian (San Marino) | italiano (San Marino) | SM (San Marino) | LTR | **Implemented** (empty → `it`) |
| `ko` | Korean (neutral) | 한국어 | KR (Korea) | LTR | **Implemented** (full translation) |
| `ko-KR` | Korean (Korea) | 한국어 (대한민국) | KR (Korea) | LTR | **Implemented** (empty → `ko`) |
| `ko-KP` | Korean (North Korea) | 한국어 (조선민주주의인민공화국) | KP (North Korea) | LTR | **Implemented** (empty → `ko`) |
| `pl` | Polish (neutral) | Polski | PL (Poland) | LTR | **Implemented** (full translation) |
| `pl-PL` | Polish (Poland) | Polski (Polska) | PL (Poland) | LTR | **Implemented** (empty → `pl`) |
| `pt` | Portuguese (neutral) | Português | BR (Brazil) | LTR | **Implemented** (full translation) |
| `pt-BR` | Portuguese (Brazil) | Português (Brasil) | BR (Brazil) | LTR | **Implemented** (empty → `pt`) |
| `pt-PT` | Portuguese (Portugal) | Português (Portugal) | PT (Portugal) | LTR | **Implemented** (empty → `pt`) |
| `pt-AO` | Portuguese (Angola) | português (Angola) | AO (Angola) | LTR | **Implemented** (empty → `pt`) |
| `pt-MZ` | Portuguese (Mozambique) | português (Moçambique) | MZ (Mozambique) | LTR | **Implemented** (empty → `pt`) |
| `pt-CV` | Portuguese (Cabo Verde) | português (Cabo Verde) | CV (Cabo Verde) | LTR | **Implemented** (empty → `pt`) |
| `pt-GW` | Portuguese (Guinea-Bissau) | português (Guiné-Bissau) | GW (Guinea-Bissau) | LTR | **Implemented** (empty → `pt`) |
| `pt-ST` | Portuguese (São Tomé & Príncipe) | português (São Tomé e Príncipe) | ST (São Tomé & Príncipe) | LTR | **Implemented** (empty → `pt`) |
| `pt-TL` | Portuguese (Timor-Leste) | português (Timor-Leste) | TL (Timor-Leste) | LTR | **Implemented** (empty → `pt`) |
| `pt-GQ` | Portuguese (Equatorial Guinea) | português (Guiné Equatorial) | GQ (Equatorial Guinea) | LTR | **Implemented** (empty → `pt`) |
| `ru` | Russian (neutral) | Русский | RU (Russia) | LTR | **Implemented** (full translation) |
| `ru-RU` | Russian (Russia) | Русский (Россия) | RU (Russia) | LTR | **Implemented** (empty → `ru`) |
| `ru-UA` | Russian (Ukraine) | русский (Украина) | UA (Ukraine) | LTR | **Implemented** (empty → `ru`) |
| `ru-KZ` | Russian (Kazakhstan) | русский (Казахстан) | KZ (Kazakhstan) | LTR | **Implemented** (empty → `ru`) |
| `ru-BY` | Russian (Belarus) | русский (Беларусь) | BY (Belarus) | LTR | **Implemented** (empty → `ru`) |
| `ru-KG` | Russian (Kyrgyzstan) | русский (Киргизия) | KG (Kyrgyzstan) | LTR | **Implemented** (empty → `ru`) |
| `tr` | Turkish (neutral) | Türkçe | TR (Türkiye) | LTR | **Implemented** (full translation) |
| `tr-TR` | Turkish (Türkiye) | Türkçe (Türkiye) | TR (Türkiye) | LTR | **Implemented** (empty → `tr`) |
| `tr-CY` | Turkish (Cyprus) | Türkçe (Kıbrıs) | CY (Cyprus) | LTR | **Implemented** (empty → `tr`) |
| `uk` | Ukrainian (neutral) | Українська | UA (Ukraine) | LTR | **Implemented** (full translation) |
| `uk-UA` | Ukrainian (Ukraine) | Українська (Україна) | UA (Ukraine) | LTR | **Implemented** (empty → `uk`) |
| `hi` | Hindi (neutral) | हिन्दी | IN (India) | LTR | **Implemented** (full translation) |
| `hi-IN` | Hindi (India) | हिन्दी (भारत) | IN (India) | LTR | **Implemented** (empty → `hi`) |
| `ur` | Urdu (neutral) | اردو | PK (Pakistan) | RTL | **Implemented** (full translation) |
| `ur-PK` | Urdu (Pakistan) | اردو (پاکستان) | PK (Pakistan) | RTL | **Implemented** (empty → `ur`) |
| `ur-IN` | Urdu (India) | اردو (بھارت) | IN (India) | RTL | **Implemented** (empty → `ur`) |
| `id` | Indonesian (neutral) | Indonesia | ID (Indonesia) | LTR | **Implemented** (full translation) |
| `id-ID` | Indonesian (Indonesia) | Indonesia (Indonesia) | ID (Indonesia) | LTR | **Implemented** (empty → `id`) |
| `vi` | Vietnamese (neutral) | Tiếng Việt | VN (Vietnam) | LTR | **Implemented** (full translation) |
| `vi-VN` | Vietnamese (Vietnam) | Tiếng Việt (Việt Nam) | VN (Vietnam) | LTR | **Implemented** (empty → `vi`) |
| `fa` | Persian (neutral) | فارسی | IR (Iran) | RTL | **Implemented** (full translation) |
| `fa-IR` | Persian (Iran) | فارسی (ایران) | IR (Iran) | RTL | **Implemented** (empty → `fa`) |
| `fa-AF` | Persian (Afghanistan) | فارسی (افغانستان) | AF (Afghanistan) | RTL | **Implemented** (empty → `fa`) |
| `nl` | Dutch (neutral) | Nederlands | NL (Netherlands) | LTR | **Implemented** (full translation) |
| `nl-NL` | Dutch (Netherlands) | Nederlands (Nederland) | NL (Netherlands) | LTR | **Implemented** (empty → `nl`) |
| `nl-BE` | Dutch (Belgium) | Nederlands (België) | BE (Belgium) | LTR | **Implemented** (empty → `nl`) |
| `nl-SR` | Dutch (Suriname) | Nederlands (Suriname) | SR (Suriname) | LTR | **Implemented** (empty → `nl`) |
| `sv` | Swedish (neutral) | svenska | SE (Sweden) | LTR | **Implemented** (full translation) |
| `sv-SE` | Swedish (Sweden) | svenska (Sverige) | SE (Sweden) | LTR | **Implemented** (empty → `sv`) |
| `sv-FI` | Swedish (Finland) | svenska (Finland) | FI (Finland) | LTR | **Implemented** (empty → `sv`) |
| `el` | Greek (neutral) | Ελληνικά | GR (Greece) | LTR | **Implemented** (full translation) |
| `el-GR` | Greek (Greece) | Ελληνικά (Ελλάδα) | GR (Greece) | LTR | **Implemented** (empty → `el`) |
| `el-CY` | Greek (Cyprus) | Ελληνικά (Κύπρος) | CY (Cyprus) | LTR | **Implemented** (empty → `el`) |
| `nb` | Norwegian Bokmål (neutral) | norsk bokmål | NO (Norway) | LTR | **Implemented** (full translation) |
| `nb-NO` | Norwegian Bokmål (Norway) | norsk bokmål (Norge) | NO (Norway) | LTR | **Implemented** (empty → `nb`) |
| `da` | Danish (neutral) | dansk | DK (Denmark) | LTR | **Implemented** (full translation) |
| `da-DK` | Danish (Denmark) | dansk (Danmark) | DK (Denmark) | LTR | **Implemented** (empty → `da`) |
| `fi` | Finnish (neutral) | suomi | FI (Finland) | LTR | **Implemented** (full translation) |
| `fi-FI` | Finnish (Finland) | suomi (Suomi) | FI (Finland) | LTR | **Implemented** (empty → `fi`) |

> **Chinese** splits by *script*, not just region: `zh-CN`/`zh-SG` (Simplified) inherit the neutral
> **`zh-Hans`**, and `zh-TW`/`zh-HK`/`zh-MO` (Traditional) inherit **`zh-Hant`**. The two neutrals are
> separate full translations. This needs script-aware resolution (the fallback chain must map a region to
> its *script* neutral, not the bare `zh`); added in P18.

> The synthetic dev locales **Pseudo (LTR)** (`pseudo`) and **Pseudo (RTL)** (`pseudo-rtl`) are generated
> at runtime from `en-US` and are not regions; they are coverage/RTL-layout oracles, not catalog entries.
> Two tiny synthetic test packs (`zz`, `zz-ZZ`) prove 3-tier precedence and are not listed here.

## Adding a language or region pack

Each pack file is `IDE/HexIDE/Localization/Packs/{id}.json` with `name`, optional `direction` (`"rtl"` for
RTL), and a `strings` map. List every pack in `LanguageManifest.Packs` and mirror it in the table above.

- **A new language** — create the neutral pack `{lang}.json` (e.g. `de.json`) carrying the bulk of the
  translation (only the keys you translate; the rest fall back to `en-US`). Then add regional packs
  `{lang}-{REGION}.json` for the variants you support.
- **A region pack** lists **only** the keys that differ from its neutral parent (or, for a language with no
  neutral pack, from `en-US`). If a region has no differences, ship it **empty** (`"strings": {}`) — it
  still appears in the dropdown (signalling the region is supported) and is a placeholder for later tweaks.
- After adding files + manifest lines + table rows (Status = **Implemented**), the packs are selectable in
  Options → Language and matched by OS detection (exact culture, else neutral-language, else `en-US`).

## Not started (BCP-47 backlog)

The 255 neutral cultures below are every `CultureInfo.GetCultures(CultureTypes.NeutralCultures)` entry
under **.NET 10 (ICU)** — i.e. the language identifiers HexIDE's `"system"` detection could match — **minus**
the 17 languages already shipped (above). They have **no pack yet**. This is the realistic universe for
future coverage (not the full ~8,000-tag IANA registry). Script variants (e.g. `sr-Cyrl`/`sr-Latn`) and
the RTL flag are listed because both affect pack design. Auto-derived; regenerate when the catalog grows.

**Grouped by AI confidence** — a self-estimate of how reliable an LLM first-pass translation would be,
roughly the language's training-data / resource level. **High** = credible first pass; **Medium** =
plausible but error-prone; **Low** = unreliable, prefer native/community authoring. Native review is
recommended regardless.

### High confidence (29)

Credible LLM first-pass — well-resourced languages; the natural next candidates after Batch 2.

| Code | English name | Native name | Home | Direction | Status |
|------|--------------|-------------|------|-----------|--------|
| `af` | Afrikaans | Afrikaans | ZA (South Africa) | LTR | **Not started** |
| `sq` | Albanian | shqip | AL (Albania) | LTR | **Not started** |
| `bn` | Bangla | বাংলা | BD (Bangladesh) | LTR | **Not started** |
| `eu` | Basque | euskara | ES (Spain) | LTR | **Not started** |
| `be` | Belarusian | беларуская | BY (Belarus) | LTR | **Not started** |
| `bg` | Bulgarian | български | BG (Bulgaria) | LTR | **Not started** |
| `ca` | Catalan | català | ES (Spain) | LTR | **Not started** |
| `hr` | Croatian | hrvatski | HR (Croatia) | LTR | **Not started** |
| `et` | Estonian | eesti | EE (Estonia) | LTR | **Not started** |
| `fil` | Filipino | Filipino | PH (Philippines) | LTR | **Not started** |
| `gl` | Galician | galego | ES (Spain) | LTR | **Not started** |
| `hi-Latn` | Hindi (Latin) | Hindi (Latin) | — | LTR | **Not started** |
| `hu` | Hungarian | magyar | HU (Hungary) | LTR | **Not started** |
| `is` | Icelandic | íslenska | IS (Iceland) | LTR | **Not started** |
| `sw` | Kiswahili | Kiswahili | KE (Kenya) | LTR | **Not started** |
| `lv` | Latvian | latviešu | LV (Latvia) | LTR | **Not started** |
| `lt` | Lithuanian | lietuvių | LT (Lithuania) | LTR | **Not started** |
| `mk` | Macedonian | македонски | MK (North Macedonia) | LTR | **Not started** |
| `ms` | Malay | Melayu | MY (Malaysia) | LTR | **Not started** |
| `no` | Norwegian | norsk | NO (Norway) | LTR | **Not started** |
| `ro` | Romanian | română | RO (Romania) | LTR | **Not started** |
| `sr` | Serbian | српски | RS (Serbia) | LTR | **Not started** |
| `sr-Cyrl` | Serbian (Cyrillic) | српски (ћирилица) | RS (Serbia) | LTR | **Not started** |
| `sr-Latn` | Serbian (Latin) | srpski (latinica) | RS (Serbia) | LTR | **Not started** |
| `sk` | Slovak | slovenčina | SK (Slovakia) | LTR | **Not started** |
| `sl` | Slovenian | slovenščina | SI (Slovenia) | LTR | **Not started** |
| `ta` | Tamil | தமிழ் | IN (India) | LTR | **Not started** |
| `te` | Telugu | తెలుగు | IN (India) | LTR | **Not started** |
| `th` | Thai | ไทย | TH (Thailand) | LTR | **Not started** |

### Medium confidence (60)

Plausible but error-prone — an LLM pass needs careful native review before shipping.

| Code | English name | Native name | Home | Direction | Status |
|------|--------------|-------------|------|-----------|--------|
| `am` | Amharic | አማርኛ | ET (Ethiopia) | LTR | **Not started** |
| `hy` | Armenian | հայերեն | AM (Armenia) | LTR | **Not started** |
| `as` | Assamese | অসমীয়া | IN (India) | LTR | **Not started** |
| `az` | Azerbaijani | azərbaycan | AZ (Azerbaijan) | LTR | **Not started** |
| `az-Cyrl` | Azerbaijani (Cyrillic) | азәрбајҹан (Кирил) | AZ (Azerbaijan) | LTR | **Not started** |
| `az-Latn` | Azerbaijani (Latin) | azərbaycan (latın) | AZ (Azerbaijan) | LTR | **Not started** |
| `bs` | Bosnian | bosanski | BA (Bosnia & Herzegovina) | LTR | **Not started** |
| `bs-Cyrl` | Bosnian (Cyrillic) | босански (ћирилица) | BA (Bosnia & Herzegovina) | LTR | **Not started** |
| `bs-Latn` | Bosnian (Latin) | bosanski (latinica) | BA (Bosnia & Herzegovina) | LTR | **Not started** |
| `br` | Breton | brezhoneg | FR (France) | LTR | **Not started** |
| `my` | Burmese | မြန်မာ | MM (Myanmar) | LTR | **Not started** |
| `ceb` | Cebuano | Cebuano | — | LTR | **Not started** |
| `ckb` | Central Kurdish | کوردیی ناوەندی | — | RTL | **Not started** |
| `eo` | Esperanto | esperanto | — | LTR | **Not started** |
| `ka` | Georgian | ქართული | GE (Georgia) | LTR | **Not started** |
| `gu` | Gujarati | ગુજરાતી | IN (India) | LTR | **Not started** |
| `ha` | Hausa | Hausa | NG (Nigeria) | LTR | **Not started** |
| `ig` | Igbo | Igbo | NG (Nigeria) | LTR | **Not started** |
| `ga` | Irish | Gaeilge | IE (Ireland) | LTR | **Not started** |
| `jv` | Javanese | Jawa | ID (Indonesia) | LTR | **Not started** |
| `jv-Java` | Javanese (Javanese) | Javanese (Javanese) | ID (Indonesia) | LTR | **Not started** |
| `kn` | Kannada | ಕನ್ನಡ | IN (India) | LTR | **Not started** |
| `kk` | Kazakh | қазақ тілі | KZ (Kazakhstan) | LTR | **Not started** |
| `km` | Khmer | ខ្មែរ | KH (Cambodia) | LTR | **Not started** |
| `ky` | Kyrgyz | кыргызча | KG (Kyrgyzstan) | LTR | **Not started** |
| `lo` | Lao | ລາວ | LA (Laos) | LTR | **Not started** |
| `lb` | Luxembourgish | Lëtzebuergesch | LU (Luxembourg) | LTR | **Not started** |
| `ml` | Malayalam | മലയാളം | IN (India) | LTR | **Not started** |
| `mt` | Maltese | Malti | MT (Malta) | LTR | **Not started** |
| `mr` | Marathi | मराठी | IN (India) | LTR | **Not started** |
| `mn` | Mongolian | монгол | MN (Mongolia) | LTR | **Not started** |
| `mn-Mong` | Mongolian (Mongolian) | Mongolian (Mongolian) | CN (China) | LTR | **Not started** |
| `ne` | Nepali | नेपाली | NP (Nepal) | LTR | **Not started** |
| `or` | Odia | ଓଡ଼ିଆ | IN (India) | LTR | **Not started** |
| `ps` | Pashto | پښتو | AF (Afghanistan) | RTL | **Not started** |
| `pa` | Punjabi | ਪੰਜਾਬੀ | IN (India) | LTR | **Not started** |
| `pa-Arab` | Punjabi (Arabic) | پنجابی (عربی) | PK (Pakistan) | RTL | **Not started** |
| `pa-Guru` | Punjabi (Gurmukhi) | ਪੰਜਾਬੀ (ਗੁਰਮੁਖੀ) | — | LTR | **Not started** |
| `gd` | Scottish Gaelic | Gàidhlig | GB (United Kingdom) | LTR | **Not started** |
| `sd` | Sindhi | سنڌي | PK (Pakistan) | RTL | **Not started** |
| `sd-Arab` | Sindhi (Arabic) | سنڌي (عربي) | PK (Pakistan) | RTL | **Not started** |
| `sd-Deva` | Sindhi (Devanagari) | सिन्धी (देवनागिरी) | IN (India) | LTR | **Not started** |
| `si` | Sinhala | සිංහල | LK (Sri Lanka) | LTR | **Not started** |
| `so` | Somali | Soomaali | SO (Somalia) | LTR | **Not started** |
| `su` | Sundanese | Basa Sunda | — | LTR | **Not started** |
| `su-Latn` | Sundanese (Latin) | Basa Sunda (Latin) | — | LTR | **Not started** |
| `tg` | Tajik | тоҷикӣ | TJ (Tajikistan) | LTR | **Not started** |
| `tt` | Tatar | татар | RU (Russia) | LTR | **Not started** |
| `tk` | Turkmen | türkmen dili | TM (Turkmenistan) | LTR | **Not started** |
| `ug` | Uyghur | ئۇيغۇرچە | CN (China) | RTL | **Not started** |
| `uz` | Uzbek | o‘zbek | UZ (Uzbekistan) | LTR | **Not started** |
| `uz-Arab` | Uzbek (Arabic) | اوزبیک (عربی) | AF (Afghanistan) | RTL | **Not started** |
| `uz-Cyrl` | Uzbek (Cyrillic) | ўзбекча (Кирил) | UZ (Uzbekistan) | LTR | **Not started** |
| `uz-Latn` | Uzbek (Latin) | o‘zbek (lotin) | UZ (Uzbekistan) | LTR | **Not started** |
| `cy` | Welsh | Cymraeg | GB (United Kingdom) | LTR | **Not started** |
| `fy` | Western Frisian | Frysk | NL (Netherlands) | LTR | **Not started** |
| `yi` | Yiddish | ייִדיש | — | RTL | **Not started** |
| `yo` | Yoruba | Èdè Yorùbá | NG (Nigeria) | LTR | **Not started** |
| `xh` | isiXhosa | IsiXhosa | ZA (South Africa) | LTR | **Not started** |
| `zu` | isiZulu | isiZulu | ZA (South Africa) | LTR | **Not started** |

### Low confidence (166)

Unreliable from an LLM alone — prefer native/community authoring via the [translation-editor](../openspec/specs/translation-editor/spec.md) path.

| Code | English name | Native name | Home | Direction | Status |
|------|--------------|-------------|------|-----------|--------|
| `aa` | Afar | Afar | ET (Ethiopia) | LTR | **Not started** |
| `agq` | Aghem | Aghem | CM (Cameroon) | LTR | **Not started** |
| `ak` | Akan | Akan | GH (Ghana) | LTR | **Not started** |
| `ast` | Asturian | asturianu | ES (Spain) | LTR | **Not started** |
| `asa` | Asu | Kipare | TZ (Tanzania) | LTR | **Not started** |
| `ksf` | Bafia | rikpa | CM (Cameroon) | LTR | **Not started** |
| `bm` | Bamanankan | bamanakan | ML (Mali) | LTR | **Not started** |
| `bas` | Basaa | Ɓàsàa | CM (Cameroon) | LTR | **Not started** |
| `ba` | Bashkir | башҡорт теле | RU (Russia) | LTR | **Not started** |
| `bem` | Bemba | Ichibemba | ZM (Zambia) | LTR | **Not started** |
| `bez` | Bena | Hibena | TZ (Tanzania) | LTR | **Not started** |
| `bho` | Bhojpuri | भोजपुरी | — | LTR | **Not started** |
| `byn` | Blin | Blin | ER (Eritrea) | LTR | **Not started** |
| `brx` | Bodo | बर’ | IN (India) | LTR | **Not started** |
| `tzm` | Central Atlas Tamazight | Tamaziɣt n laṭlaṣ | DZ (Algeria) | LTR | **Not started** |
| `tzm-Arab` | Central Atlas Tamazight (Arabic) | Central Atlas Tamazight (Arabic) | MA (Morocco) | LTR | **Not started** |
| `tzm-Tfng` | Central Atlas Tamazight (Tifinagh) | Central Atlas Tamazight (Tifinagh) | MA (Morocco) | LTR | **Not started** |
| `ccp` | Chakma | 𑄌𑄋𑄴𑄟𑄳𑄦 | — | LTR | **Not started** |
| `ce` | Chechen | нохчийн | RU (Russia) | LTR | **Not started** |
| `chr` | Cherokee | ᏣᎳᎩ | US (United States) | LTR | **Not started** |
| `cgg` | Chiga | Rukiga | UG (Uganda) | LTR | **Not started** |
| `cu` | Church Slavic | Church Slavic | RU (Russia) | LTR | **Not started** |
| `cv` | Chuvash | чӑваш | — | LTR | **Not started** |
| `ksh` | Colognian | Kölsch | DE (Germany) | LTR | **Not started** |
| `kw` | Cornish | kernewek | GB (United Kingdom) | LTR | **Not started** |
| `co` | Corsican | corsu | FR (France) | LTR | **Not started** |
| `dv` | Divehi | Divehi | MV (Maldives) | RTL | **Not started** |
| `doi` | Dogri | डोगरी | — | LTR | **Not started** |
| `dua` | Duala | duálá | CM (Cameroon) | LTR | **Not started** |
| `dz` | Dzongkha | རྫོང་ཁ | BT (Bhutan) | LTR | **Not started** |
| `bin` | Edo | Ẹ̀dó | NG (Nigeria) | LTR | **Not started** |
| `ebu` | Embu | Kĩembu | KE (Kenya) | LTR | **Not started** |
| `ee` | Ewe | Eʋegbe | GH (Ghana) | LTR | **Not started** |
| `ewo` | Ewondo | ewondo | CM (Cameroon) | LTR | **Not started** |
| `fo` | Faroese | føroyskt | FO (Faroe Islands) | LTR | **Not started** |
| `fur` | Friulian | furlan | IT (Italy) | LTR | **Not started** |
| `ff` | Fula | Pulaar | SN (Senegal) | LTR | **Not started** |
| `ff-Adlm` | Fula (Adlam) | 𞤆𞤵𞤤𞤢𞤪 (𞤀𞤁𞤂𞤢𞤃) | — | RTL | **Not started** |
| `ff-Latn` | Fula (Latin) | Fula (Latin) | SN (Senegal) | LTR | **Not started** |
| `lg` | Ganda | Luganda | UG (Uganda) | LTR | **Not started** |
| `gn` | Guarani | avañe’ẽ | PY (Paraguay) | LTR | **Not started** |
| `guz` | Gusii | Ekegusii | KE (Kenya) | LTR | **Not started** |
| `bgc` | Haryanvi | हरियाणवी | — | LTR | **Not started** |
| `haw` | Hawaiian | ʻŌlelo Hawaiʻi | US (United States) | LTR | **Not started** |
| `ibb` | Ibibio | Ibibio-Efik | NG (Nigeria) | LTR | **Not started** |
| `smn` | Inari Sami | anarâškielâ | FI (Finland) | LTR | **Not started** |
| `ia` | Interlingua | interlingua | FR (France) | LTR | **Not started** |
| `iu` | Inuktitut | Inuktitut | CA (Canada) | LTR | **Not started** |
| `iu-Latn` | Inuktitut (Latin) | Inuktitut (Latin) | CA (Canada) | LTR | **Not started** |
| `dyo` | Jola-Fonyi | joola | SN (Senegal) | LTR | **Not started** |
| `kea` | Kabuverdianu | kabuverdianu | CV (Cabo Verde) | LTR | **Not started** |
| `kab` | Kabyle | Taqbaylit | DZ (Algeria) | LTR | **Not started** |
| `kgp` | Kaingang | kanhgág | — | LTR | **Not started** |
| `kkj` | Kako | kakɔ | CM (Cameroon) | LTR | **Not started** |
| `kl` | Kalaallisut | kalaallisut | GL (Greenland) | LTR | **Not started** |
| `kln` | Kalenjin | Kalenjin | KE (Kenya) | LTR | **Not started** |
| `kam` | Kamba | Kikamba | KE (Kenya) | LTR | **Not started** |
| `kr` | Kanuri | Kanuri | NG (Nigeria) | LTR | **Not started** |
| `kr-Latn` | Kanuri (Latin) | Kanuri (Latin) | — | LTR | **Not started** |
| `ks` | Kashmiri | کٲشُر | IN (India) | RTL | **Not started** |
| `ks-Arab` | Kashmiri (Arabic) | کٲشُر (عربی) | IN (India) | RTL | **Not started** |
| `ks-Deva` | Kashmiri (Devanagari) | कॉशुर (देवनागरी) | IN (India) | LTR | **Not started** |
| `ki` | Kikuyu | Gikuyu | KE (Kenya) | LTR | **Not started** |
| `rw` | Kinyarwanda | Kinyarwanda | RW (Rwanda) | LTR | **Not started** |
| `kok` | Konkani | कोंकणी | IN (India) | LTR | **Not started** |
| `khq` | Koyra Chiini | Koyra ciini | ML (Mali) | LTR | **Not started** |
| `ses` | Koyraboro Senni | Koyraboro senni | ML (Mali) | LTR | **Not started** |
| `nmg` | Kwasio | Kwasio | CM (Cameroon) | LTR | **Not started** |
| `quc` | Kʼicheʼ | Kʼicheʼ | GT (Guatemala) | LTR | **Not started** |
| `lkt` | Lakota | Lakȟólʼiyapi | US (United States) | LTR | **Not started** |
| `lag` | Langi | Kɨlaangi | TZ (Tanzania) | LTR | **Not started** |
| `la` | Latin | Latin | — | LTR | **Not started** |
| `ln` | Lingala | lingála | CD (Congo (DRC)) | LTR | **Not started** |
| `nds` | Low German | Low German | DE (Germany) | LTR | **Not started** |
| `dsb` | Lower Sorbian | dolnoserbšćina | DE (Germany) | LTR | **Not started** |
| `lu` | Luba-Katanga | Tshiluba | CD (Congo (DRC)) | LTR | **Not started** |
| `smj` | Lule Sami | julevsámegiella | SE (Sweden) | LTR | **Not started** |
| `luo` | Luo | Dholuo | KE (Kenya) | LTR | **Not started** |
| `luy` | Luyia | Luluhia | KE (Kenya) | LTR | **Not started** |
| `jmc` | Machame | Kimachame | TZ (Tanzania) | LTR | **Not started** |
| `mai` | Maithili | मैथिली | — | LTR | **Not started** |
| `mgh` | Makhuwa-Meetto | Makua | MZ (Mozambique) | LTR | **Not started** |
| `kde` | Makonde | Chimakonde | TZ (Tanzania) | LTR | **Not started** |
| `mg` | Malagasy | Malagasy | MG (Madagascar) | LTR | **Not started** |
| `mni` | Manipuri | মৈতৈলোন্ | IN (India) | LTR | **Not started** |
| `mni-Beng` | Manipuri (Bangla) | মৈতৈলোন্ (বাংলা) | — | LTR | **Not started** |
| `gv` | Manx | Gaelg | IM (Isle of Man) | LTR | **Not started** |
| `arn` | Mapuche | Mapudungun | CL (Chile) | LTR | **Not started** |
| `mas` | Masai | Maa | KE (Kenya) | LTR | **Not started** |
| `mzn` | Mazanderani | مازرونی | IR (Iran) | RTL | **Not started** |
| `mer` | Meru | Kĩmĩrũ | KE (Kenya) | LTR | **Not started** |
| `mgo` | Metaʼ | metaʼ | CM (Cameroon) | LTR | **Not started** |
| `moh` | Mohawk | Kanienʼkéha | CA (Canada) | LTR | **Not started** |
| `mfe` | Morisyen | kreol morisien | MU (Mauritius) | LTR | **Not started** |
| `mua` | Mundang | MUNDAŊ | CM (Cameroon) | LTR | **Not started** |
| `mi` | Māori | Māori | NZ (New Zealand) | LTR | **Not started** |
| `naq` | Nama | Khoekhoegowab | NA (Namibia) | LTR | **Not started** |
| `nnh` | Ngiemboon | Shwóŋò ngiembɔɔn | CM (Cameroon) | LTR | **Not started** |
| `jgo` | Ngomba | Ndaꞌa | CM (Cameroon) | LTR | **Not started** |
| `yrl` | Nheengatu | nheẽgatu | — | LTR | **Not started** |
| `pcm` | Nigerian Pidgin | Naijíriá Píjin | — | LTR | **Not started** |
| `nd` | North Ndebele | isiNdebele | ZW (Zimbabwe) | LTR | **Not started** |
| `lrc` | Northern Luri | لۊری شومالی | IR (Iran) | RTL | **Not started** |
| `se` | Northern Sami | davvisámegiella | NO (Norway) | LTR | **Not started** |
| `nn` | Norwegian Nynorsk | norsk nynorsk | NO (Norway) | LTR | **Not started** |
| `nus` | Nuer | Thok Nath | SS (South Sudan) | LTR | **Not started** |
| `nyn` | Nyankole | Runyankore | UG (Uganda) | LTR | **Not started** |
| `nqo` | N’Ko | ߒߞߏ | GN (Guinea) | RTL | **Not started** |
| `oc` | Occitan | Occitan | FR (France) | LTR | **Not started** |
| `om` | Oromo | Oromoo | ET (Ethiopia) | LTR | **Not started** |
| `os` | Ossetic | ирон | GE (Georgia) | LTR | **Not started** |
| `pap` | Papiamento | Papiamentu | — | LTR | **Not started** |
| `prg` | Prussian | prūsiskan | — | LTR | **Not started** |
| `qu` | Quechua | Runasimi | — | LTR | **Not started** |
| `raj` | Rajasthani | राजस्थानी | — | LTR | **Not started** |
| `rm` | Romansh | rumantsch | CH (Switzerland) | LTR | **Not started** |
| `rof` | Rombo | Kihorombo | TZ (Tanzania) | LTR | **Not started** |
| `rn` | Rundi | Ikirundi | BI (Burundi) | LTR | **Not started** |
| `rwk` | Rwa | Kiruwa | TZ (Tanzania) | LTR | **Not started** |
| `ssy` | Saho | Saho | ER (Eritrea) | LTR | **Not started** |
| `saq` | Samburu | Kisampur | KE (Kenya) | LTR | **Not started** |
| `sg` | Sango | Sängö | CF (Central African Republic) | LTR | **Not started** |
| `sbp` | Sangu | Ishisangu | TZ (Tanzania) | LTR | **Not started** |
| `sa` | Sanskrit | संस्कृत भाषा | IN (India) | LTR | **Not started** |
| `sat` | Santali | ᱥᱟᱱᱛᱟᱲᱤ | — | LTR | **Not started** |
| `sat-Olck` | Santali (Ol Chiki) | ᱥᱟᱱᱛᱟᱲᱤ (ᱚᱞ ᱪᱤᱠᱤ) | — | LTR | **Not started** |
| `sc` | Sardinian | sardu | — | LTR | **Not started** |
| `seh` | Sena | sena | MZ (Mozambique) | LTR | **Not started** |
| `st` | Sesotho | Sesotho | ZA (South Africa) | LTR | **Not started** |
| `nso` | Sesotho sa Leboa | Sesotho sa Leboa | ZA (South Africa) | LTR | **Not started** |
| `tn` | Setswana | Setswana | ZA (South Africa) | LTR | **Not started** |
| `ksb` | Shambala | Kishambaa | TZ (Tanzania) | LTR | **Not started** |
| `sn` | Shona | chiShona | ZW (Zimbabwe) | LTR | **Not started** |
| `sms` | Skolt Sami | Skolt Sami | FI (Finland) | LTR | **Not started** |
| `xog` | Soga | Olusoga | UG (Uganda) | LTR | **Not started** |
| `nr` | South Ndebele | South Ndebele | ZA (South Africa) | LTR | **Not started** |
| `sma` | Southern Sami | Åarjelsaemien gïele | SE (Sweden) | LTR | **Not started** |
| `zgh` | Standard Moroccan Tamazight | ⵜⴰⵎⴰⵣⵉⵖⵜ | MA (Morocco) | LTR | **Not started** |
| `gsw` | Swiss German | Schwiizertüütsch | CH (Switzerland) | LTR | **Not started** |
| `syr` | Syriac | ܣܘܪܝܝܐ | SY (Syria) | RTL | **Not started** |
| `shi` | Tachelhit | ⵜⴰⵛⵍⵃⵉⵜ | MA (Morocco) | LTR | **Not started** |
| `shi-Latn` | Tachelhit (Latin) | Tachelhit (Latin) | MA (Morocco) | LTR | **Not started** |
| `shi-Tfng` | Tachelhit (Tifinagh) | Tachelhit (Tifinagh) | MA (Morocco) | LTR | **Not started** |
| `dav` | Taita | Kitaita | KE (Kenya) | LTR | **Not started** |
| `twq` | Tasawaq | Tasawaq senni | NE (Niger) | LTR | **Not started** |
| `teo` | Teso | Kiteso | UG (Uganda) | LTR | **Not started** |
| `bo` | Tibetan | བོད་སྐད་ | CN (China) | LTR | **Not started** |
| `tig` | Tigre | Tigre | ER (Eritrea) | LTR | **Not started** |
| `ti` | Tigrinya | ትግርኛ | ER (Eritrea) | LTR | **Not started** |
| `to` | Tongan | lea fakatonga | TO (Tonga) | LTR | **Not started** |
| `hsb` | Upper Sorbian | hornjoserbšćina | DE (Germany) | LTR | **Not started** |
| `vai` | Vai | ꕙꔤ | LR (Liberia) | LTR | **Not started** |
| `vai-Latn` | Vai (Latin) | Vai (Latin) | LR (Liberia) | LTR | **Not started** |
| `vai-Vaii` | Vai (Vai) | Vai (Vai) | LR (Liberia) | LTR | **Not started** |
| `ve` | Venda | Venda | ZA (South Africa) | LTR | **Not started** |
| `vo` | Volapük | Volapük | — | LTR | **Not started** |
| `vun` | Vunjo | Kyivunjo | TZ (Tanzania) | LTR | **Not started** |
| `wae` | Walser | Walser | CH (Switzerland) | LTR | **Not started** |
| `wal` | Wolaytta | Wolaytta | ET (Ethiopia) | LTR | **Not started** |
| `wo` | Wolof | Wolof | SN (Senegal) | LTR | **Not started** |
| `ts` | Xitsonga | Xitsonga | ZA (South Africa) | LTR | **Not started** |
| `sah` | Yakut | саха тыла | RU (Russia) | LTR | **Not started** |
| `yav` | Yangben | nuasue | CM (Cameroon) | LTR | **Not started** |
| `ii` | Yi | ꆈꌠꉙ | CN (China) | LTR | **Not started** |
| `dje` | Zarma | Zarmaciine | NE (Niger) | LTR | **Not started** |
| `ss` | siSwati | siSwati | ZA (South Africa) | LTR | **Not started** |

