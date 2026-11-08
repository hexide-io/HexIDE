# In-app translation corrections

Draft for Jen's review, 2026-10-30. This is the first deliverable for
[#2819](https://github.com/hexide-io/HexIDE/issues/2819), not an approved implementation plan or a
claim that submission exists. Receiving infrastructure and its policies remain Jen's decisions.
[#2818](https://github.com/hexide-io/HexIDE/issues/2818) remains the GitHub issue-form route for developers;
ordinary users should complete this flow inside HexIDE without an account.

## Starting point

The [translation editor spec](../../openspec/specs/translation-editor/spec.md) describes the existing
local editor. Options → Language opens it through `CustomizeLanguage` in
[`OptionsViewModel`](../../IDE/HexIDE/Forms/ViewModels/OptionsViewModel.cs). It lists canonical keys,
reference and target values, supports search and grouping, and applies edits immediately.
[`UserTranslationsService`](../../IDE/HexIDE/Localization/UserTranslationsService.cs) stores overrides
per locale and validates format placeholders.
[`LocalizationService.MergeUserOverrides`](../../IDE/HexIDE/Localization/LocalizationService.cs) merges
user neutral and regional overrides after shipped values. Local corrections already survive restarts;
there is no submission queue or receiver in these services.

The proposal extends this local workflow. It does not edit installed packs or require users to export
files, identify a repository, write an issue, or obtain a GitHub account. UI labels below are working copy;
implementation must localize them in every shipped pack.

## Proposed flow

1. Reach the string. In Options → Language, open the existing editor, find the visible wording, and edit
   its row. Also offer a temporary “Correct a translation” picking mode: activate it, select a control,
   and open that same editor focused on its localization key. Picking must not invoke the control's
   normal action. Show the reference text and the target language/region alongside the editable text.
2. Commit the edit once. Keep the correction locally and show it in the IDE immediately, using the
   existing override tier and placeholder validation. Add it to “My corrections” as a draft. Editing
   the same pack/key replaces its unsent suggestion; do not accumulate every keystroke. Clearing an
   override removes its unsent draft. Local-only terminology preferences may be excluded from sending.
3. When ready, open “My corrections” and review the selected entries together. All five transmitted
   fields must be visible, including full text rather than truncated previews. Entries can be deselected
   or edited here. One explicit “Send selected corrections” action submits the reviewed snapshot;
   there is no second confirmation, sign-in, or compulsory explanation.
4. Show receipt or failure in this view. Delivery success means received for review, not accepted into
   a pack. Continue using local corrections either way. No repeated prompts while the user works.

A control can contain several strings (label, tooltip, menu item), and equal displayed words can have
several keys. Identify by resource key, never reverse-match text and silently choose a key. Where mapping
is ambiguous, offer the candidate strings; where unavailable, open the searchable list. Runtime program
text, project captions, paths and interpolated values must not become correction candidates. Keyboard
and assistive-technology users need the list route and an accessible alternative to pointer picking.

## Payload and consent

For each selected correction, propose exactly these application payload fields:

| Field | Meaning |
|---|---|
| Pack id | Explicit correction target, such as a neutral language or a regional override; never `system`. |
| Key | Canonical `Str.*` resource key. |
| Current text | Unformatted target-pack text resolved through its installed fallback chain **without user overrides**, captured when this draft began. |
| Suggested text | The user's committed replacement template, preserving placeholders. |
| HexIDE version | The installed application version attached to this reviewed submission. |

The current text is the baseline being corrected, not the suggestion already visible through the user
tier. Show baseline and suggestion in the editor and review. If an installed update changes the baseline,
mark the draft stale and ask the user to review it against the new text before sending; never replace
previously reviewed content silently. Targeting a fileless region still records that explicit region id,
with its inherited baseline. Review should explain the scope and offer the neutral target if intended.

Do not attach a name, email address, account, machine/install identifier, project, file path, screenshot,
logs, rendered format arguments or unrelated settings. Do not add a free-text narrative field. Explain
that suggestions are intended for maintainers and may become public pack text; ask users not to include
personal information in their replacement. User-entered text cannot be guaranteed free of personal data,
so review and moderation must cover accidental disclosures too.

Use encrypted transport. Network infrastructure necessarily sees connection metadata such as an IP
address: distinguish this from the five-field application payload. Before launch, disclose the chosen
operator and metadata handling; minimize access logs and retention. Do not claim that transport is
anonymous merely because the payload has no identity field. Opening the editor, saving a correction,
starting HexIDE, or checking connectivity must not send corrections.

## Offline and delivery

Persist drafts and explicitly authorized pending submissions in the user profile, separately from the
existing override files. The latter hold only the latest local strings and cannot record baselines,
reviewed snapshots or delivery state. A send action offline leaves the reviewed snapshot queued across
restarts. New edits remain drafts rather than altering an authorized snapshot. Show queued state and
allow cancellation before transmission, without deleting the local override.

The review must explain whether “Send” authorizes later automatic delivery or requires another click
when online (question below). Under either policy, nothing unreviewed enters an outgoing batch. Retain
failed submissions, distinguish retryable failure from invalid content, respect receiver backoff, and
never block startup or local editing. Treat a lost acknowledgement as uncertain delivery; deduplicate
retries server-side or flag duplicates for moderation rather than promising exactly-once delivery.
Local persistence errors must be visible: today's override persistence catches and logs write failures,
so a future queue cannot treat `SetOverride` returning true as proof of durable storage.

## Receiving options — no choice made

These are architectural options, not provider recommendations or quoted prices. Actual provider,
region, quotas and budget need review before implementation. No receiver is selected here.

| Option | Hosting and cost | Secrets | Abuse and moderation |
|---|---|---|---|
| Serverless HTTPS intake → bot-created GitHub issues | Function plus repository integration; low idle overhead, request/egress and issue-volume costs need a cap. Requires an operator and deployment ownership. | Bot/App credential stays in server-side secret storage, scoped to the receiving repository and rotated. Never embed a writable GitHub token in HexIDE. | Validate and limit requests before creating issues; rate-limit, deduplicate and cap spend. A private intake repository or moderated staging store prevents spam and accidental personal text being published immediately. Maintainers triage issues; bot identity does not make a report trusted. |
| HTTPS intake → private queue/database | Function or small service plus durable storage and a maintainer review interface; storage, backups and reviewer tooling add ongoing cost. | Storage credentials and maintainer access controls stay server-side. The app has only the public submission address. | Hold submissions privately for review; size/rate limits, quotas, deduplication and retention/deletion jobs are required. Review UI and export to the two pack repositories must be maintained. |
| Managed feedback intake with an account-free API | Vendor hosts intake and review; subscription, quotas, data location and availability depend on the chosen service. | Any privileged API credential needs a server-side relay; only an explicitly public, restricted intake token could ship in the client. | Confirm no reporter login or tracking is required, five-field payload support, deletion/export controls, spam handling and moderation before publication. Vendor lock-in and processor terms need Jen's review. |

All options need strict field/length/batch validation, pack/key validation, safe display of untrusted
text, placeholder checks and bounded request handling. A public endpoint cannot be protected by a secret
compiled into a desktop client. Rate limiting and quotas reduce abuse but can affect users behind shared
networks; a challenge adds friction and accessibility costs. No automatic pack edits or merges should
follow receipt. Transport acknowledgements can report per-entry receipt without asking for an identity.

## From review back to the reporter

Maintainers compare suggestions against the current pack, language context, placeholders and mnemonics,
then accept, adapt, reject or consolidate duplicates. For an accepted tier-1 correction, maintainers edit
the relevant pack in this repository and run its normal localization checks before release. For tier-2,
they carry the reviewed change to
[hexide-io/HexIDE.Localisation](https://github.com/hexide-io/HexIDE.Localisation) and its pack publication
process. Users receive the correction through the appropriate application release or tier-2 pack update;
receipt alone must never remove an override. This draft does not prescribe a new pack updater.

After an installed pack update, compare the suggestion with the installed fallback result excluding all
user overrides. Exact equality at the intended target scope makes an override redundant; offer to retire
it, or remove it automatically only under an approved policy. A regional fallback matching today may
later diverge, so neutral and regional scope must be considered separately. An adapted acceptance that
differs from the local text remains an override until the user chooses the pack wording. Rejection or
no response leaves it intact. Never remove a newer local edit because an older submitted value matched.
An account-free baseline needs no reporter identity: local comparisons can find exact matches; personal
status tracking would need an additional receipt mechanism and a separate decision.

## Questions for review

Every choice below remains open; the flow above is a proposal, not a decision on these points.

| Question | Options |
|---|---|
| Which entry points should ship first? | Existing searchable list only; list plus control picker together; list first, picker later. |
| How should picking be activated and made keyboard-accessible? | A temporary command-driven mode with keyboard traversal; a contextual correction action where safe, with the list as the universal route. |
| Which pack should an edit target by default? | Active region with a neutral-language scope choice; neutral language with explicit regional opt-in; require a scope choice for regional sessions. |
| Should existing overrides become drafts? | Offer a one-time opt-in import, reconstructing and reviewing current baselines; collect only new edits. Never silently submit old overrides. |
| What does sending offline authorize? | Queue the reviewed snapshot for automatic delivery when online, disclosed beside Send; retain it and require an explicit retry when online. |
| Which receiver, operator, region and budget should be used? | Serverless bot-to-issues; private queue/store; managed intake with a compatible account-free API. Jen chooses. |
| Where should raw submissions be visible? | Private moderation then publish accepted text; public intake after explicit disclosure and privacy review. |
| How should abuse be bounded? | Rate/size limits and spend caps alone initially; add an accessible challenge only when needed. What limits and shared-network recovery should apply? |
| What metadata and content retention policy should apply? | Disable routine access logs where possible or retain minimized logs briefly; delete reviewed raw records after a defined interval or retain a sanitized audit trail. Jen sets intervals and operator access. |
| How should uncertain deliveries be deduplicated? | Compare the five-field contents server-side within a bounded window; approve an additional transport idempotency token, with privacy and payload review first. |
| Is individual review-status feedback needed? | Local received/queued state plus pack comparison only; an opaque receipt and manual in-app status lookup, requiring a protocol extension without an account. |
| How should redundant overrides retire? | Offer removal after exact match; automatic removal with a visible undo. How should regional inheritance changes be handled? |
| How will tier-2 updates reach users? | Existing/manual pack installation if available; a separately approved update flow. Confirm the publication/install contract with that repository before implementing retirement. |

After review, record approved behavior in OpenSpec and scope the implementation and tests. Future checks
should cover immediate local edits, precise key/scope selection, placeholder refusal, five-field preview
and payload equality, no network activity before consent, durable offline/cancel/retry behavior, partial
receipts, edits after queueing, stale baselines, and override retirement without losing newer edits.
A live IDE check should exercise picking without triggering the control, keyboard/list access, RTL text,
long strings and the full submission preview. No new runtime behavior or VB6 measurement is introduced
by this note.
