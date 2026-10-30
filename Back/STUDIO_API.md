# Templates and Letters API

These workflows use the existing authentication cookie and `X-CSRF-TOKEN` header. They work independently of Vue. Development Swagger includes their request models. Resource IDs are scoped to the authenticated owner, including administrator callers; another owner's resource returns 404. No caller-supplied filesystem paths are accepted.

## Private templates

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/api/templates/?page=1` | List private templates and mappings |
| GET | `/api/templates/fields` | Logical letter fields, required fields and languages |
| POST | `/api/templates/starter` | Create a blank-document or letter starter template |
| POST | `/api/templates/` | Snapshot an owned source document revision |
| GET | `/api/templates/{id}?version=1` | Read metadata, configuration and provenance |
| GET | `/api/templates/{id}/bookmarks?version=1` | Discover replaceable bookmarks |
| PUT | `/api/templates/{id}` | Update title/mappings with the expected version |
| DELETE | `/api/templates/{id}?version=1` | Delete template revision |
| GET | `/api/templates/{id}/download?version=1&format=docx` | Download a template revision |
| POST | `/api/templates/{id}/render` | Create a new ordinary document, preserving the template |

Lists return `{page, hasMore, items}` with 50 records per page. Entries contain `metadata` (`id`, `kind`, `title`, `version`, timestamps, state) and `data`. Individual resource/file reads require the version. Stale requests return 409. New resources start at version 1.

Starter request:

```json
{"title":"My letterhead","kind":"letter","language":"fa-IR","paper":"A5"}
```

Use `kind: "document"` for an ordinary blank template. Letter starters provide named fields with English or Persian labels. Optional `paper` accepts `A5`, `A4` or `Letter` for letters only; omitted defaults are Persian A5, English Letter, and other supported RTL languages A4. Persian and English profiles include distinct named typography styles and header/footer stories. See [layout sources and font requirements](../Application/docs/LETTER_TEMPLATES.md). Rich imported bodies retain their own styles; plain text in the new letter profiles treats newlines as paragraphs. To snapshot an existing or uploaded document:

```json
{
  "title":"Reusable form", "kind":"document", "language":"en-US",
  "source":{"id":"OWNED-DOCUMENT-GUID","version":3},
  "fields":{}, "requiredFields":[]
}
```

Source edits/deletion cannot change that snapshot. Template metadata/mapping updates advance the version; changing actual content requires another template snapshot. A document template's render request creates a distinct owned document:

```json
{"version":1,"title":"Completed form","values":{"CustomerName":"Example customer"}}
```

Omit values or use `{}` to copy without replacements. Values use actual bookmark names with exact matching. A failed batch does not create a partial document. Letter templates use the separate letter workflow.

For custom letter templates, `fields` maps logical names to DOCX bookmarks, such as `{"Body":"متن_نامه","Signature":"امضای_اصلی"}`. Logical names are `Organization`, `Logo`, `Date`, `Number`, `Classification`, `Priority`, `Subject`, `Recipients`, `Body`, `SenderName`, `SenderTitle`, `Signature`, `Copies`, `Attachments`, `Address`, `Tracking`, `QRCode`. Letter generation always requires mappings for `Subject`, `Recipients`, `Body`, `SenderName`, `SenderTitle`, `Signature`; `requiredFields` may require more. Mapped bookmark names must be distinct and replaceable. Unmapped optional fields are omitted. Logos, QR codes and copy variants require their corresponding mappings when selected.

## Sender identities, images and registered users

| Method | Route | Purpose |
| --- | --- | --- |
| GET/POST | `/api/letters/senders/` | List/create private sender identities and titles |
| GET | `/api/letters/senders/{id}?version=1` | Read sender revision |
| PUT | `/api/letters/senders/{id}` | Update sender with expected version |
| DELETE | `/api/letters/senders/{id}?version=1` | Delete sender revision |
| GET | `/api/letters/images/?page=1` | List private signature/logo metadata |
| POST | `/api/letters/images/` | Multipart `file` and `title` PNG/JPEG upload |
| GET | `/api/letters/images/{id}?version=1` | Read private image bytes |
| DELETE | `/api/letters/images/{id}?version=1` | Delete image revision |
| GET | `/api/letters/directory?search=&page=1` | Search registered recipient users by email |

Sender JSON contains `version`, `fullName`, `title`, `organization`, `address`. Users can save multiple titles. Images are limited to 512 KiB and 4096 pixels per dimension, verified by decoding. Knowing an image ID does not grant access. A selected signature image is a visual signature, not certificate-based signing.

The authenticated directory returns only user IDs, email addresses and email-derived labels. Empty search returns only the signed-in account; another recipient requires an exact full email address, case insensitive. Partial searches return no matches, and pagination cannot browse accounts. Exact lookup can still reveal registration of a known email address; invitation-based discovery and verified email ownership are future work. The directory never exposes roles, signatures, sender profiles or files. Choosing a recipient formats the letter; it does not share or deliver files.

## Letter lifecycle

| Method | Route | Purpose |
| --- | --- | --- |
| GET/POST | `/api/letters/` | List/create private drafts |
| GET | `/api/letters/{id}?version=1` | Read draft and generation snapshot |
| PUT | `/api/letters/{id}` | Save `{version, draft}` before generation |
| DELETE | `/api/letters/{id}?version=1` | Delete revision and its outputs |
| POST | `/api/letters/{id}/duplicate` | Create another draft with `{version, title}` |
| POST | `/api/letters/{id}/validate` | Preflight `{version}` without mutation |
| POST | `/api/letters/{id}/preview` | Inline PDF preview from `{version}` without finalization |
| POST | `/api/letters/{id}/generate` | Atomically finalize `{version, idempotencyKey}` |
| GET | `/api/letters/{id}/artifacts?version=2` | List generated files |
| GET | `/api/letters/{id}/artifacts/{artifactId}/download?version=2&format=docx` | Download frozen DOCX, PDF or native JSON |

PDF previews return `application/pdf` with `Content-Disposition: inline`. The Vue preview action opens a new tab before rendering begins, then displays the authenticated response there. Errors close that tab and remain visible in the letter screen. Explicit artifact download routes continue to return attachments.

Ordinary document lists never contain letters or generated variants. Letter lists contain summary metadata; use the individual GET for a draft. Drafts may remain incomplete while being prepared. Creation accepts this model (replace sample IDs):

```json
{
  "title":"Project letter", "subject":"Project update", "language":"en-US", "date":"2026-10-26",
  "template":{"id":"TEMPLATE-GUID","version":1},
  "sender":{"id":"SENDER-GUID","version":1},
  "signature":{"id":"IMAGE-GUID","version":1},
  "logo":null, "bodyDocument":null, "bodyText":"Dear colleague,\nHere is the update.",
  "recipients":[{"userId":"REGISTERED-USER-ID","kind":"to","printOnLetter":true}],
  "attachments":[], "number":"", "priority":"normal", "classification":"normal",
  "includeQrCode":true, "copyVariants":false
}
```

References contain the ID/version selected by the client. `bodyDocument` optionally references an owned saved document instead of plain text. Rich insertion preserves images, tables and imported styles; source bookmark markers are stripped to prevent collisions. Put the body bookmark in its own paragraph to retain the first source paragraph's formatting; inline fields keep the surrounding template paragraph's formatting. Source headers/footers are excluded so the template provides letterhead. Format fidelity remains subject to Rust import/export support.

Recipients use `to` or `cc`, with at least one main recipient required. Each user may appear once. `printOnLetter` controls whether a copy recipient appears in the primary letter. With `copyVariants`, generation also creates a no-copy-list file and one file per copy recipient, including hidden recipients. All variants remain private to the owner; display flags never grant download access.

Dates are explicit ISO dates independent of server timezone; Persian letters format them with the Persian calendar. Empty `number` generates a stable letter-specific `L-…` identifier, not an external correspondence-system registration. Priorities: `normal`, `urgent`, `immediate`. Classifications: `normal`, `confidential`, `strict`.

Preflight returns `{valid, version, issues:[{code, field, message}]}`. It checks subject/body/date, registered recipients, owned/current resource revisions, sender/signature selection, required bookmarks, comments, actual unresolved revision marks, protection and Track Changes. Preview/generation repeat validation. Failed generation returns 422 with `issues` and saves no outputs. Stale draft mutations return 409.

Generation stores a snapshot of the selected template/configuration, sender details, recipient labels, dependency versions/hashes and request data, plus complete native-model, DOCX and PDF artifact bytes. Later source edits or deletion cannot change those files. Selected ordinary documents become frozen attachment artifacts. Generated letters cannot be edited; duplicate one to prepare another draft.

Use a fresh 16–80-character ASCII idempotency key (UUIDs work). Retrying with the same key and original version returns the original artifact IDs. A different key cannot finalize the letter again. Revision checks, output uniqueness and one database transaction prevent races from saving partial output sets. No Pargar, DMS, email or external delivery occurs.

Limits: 20 distinct recipients including at most 10 copies; five attachments; 480 KiB UTF-8 plain body text; 64 MiB combined template/body/attachment native models; 256 MiB complete stored output set. Draft/render JSON is capped at 4 MiB including escaping. Existing upload and converter time/concurrency limits still apply.

## Editable letter template copies

| Method | Route | Purpose |
| --- | --- | --- |
| POST | `/api/letters/{id}/editor` | Open/initialize an editable copy from `{version}` |
| GET | `/api/letters/{id}/editor?version=2` | Read consistent `{metadata, document, bodyBookmark}` |
| PUT | `/api/letters/{id}/editor` | Save `{version, document}` and return the new metadata |
| POST | `/api/letters/{id}/editor/fields` | Fill selected automatic fields from `{version}`, preserving the written body |
| GET | `/api/letters/{id}/editor/download?version=3&format=docx` | Download the saved working revision |

Opening for the first time copies the selected template and inserts available details; incomplete recipient/sender/signature details are allowed at this writing stage. The response is a resource detail with `metadata.hasEditor: true` and an advanced version. Reopening an existing copy does not change its content/version and remains possible when review flags or missing dependencies need repair. Native editor saves canonicalize the model through Rust, enforce ownership and optimistic concurrency, and remain outside the ordinary document library.

Write inside the mapped Body bookmark, keeping its boundary markers and the other automatic fields. The browser starts the caret at the end of this body range. Rich content, including tables, can span that range. Validation checks the actual edited body and reports missing/reversed/nested field markers, empty bodies, pending review and changed dependencies. Working copies are limited to 64 MiB of native JSON (65 MiB transport allowance).

After editing starts, change body content through the editor API; changing the template ID or replacing the body source through the details PUT returns 409. Sender, title, recipient and other letter details can still change using the standard draft endpoint. Then call `/editor/fields` to refresh their displayed values. Final preview/generation always reapplies the saved details: manually typed identity/signature values cannot override the selected API resources. The edited body and other layout changes are retained. Generation includes the editor revision/hash in its snapshot, and duplication retains the editable content. Generated letters reject editor mutations and use their immutable artifact downloads.

Example client sequence: create draft → open editor with version 1 → read returned version 2 → PUT the edited model at version 2 → update letter details at version 3 → fill fields at version 4 → validate/generate using version 5. Use returned versions rather than assuming numbers. The same operations are available without Vue.

## Checks and sample files

After building the backend, run `python Tests/template_api_smoke.py` and `python Tests/letter_api_smoke.py`. They use isolated databases/accounts and original fixtures, exercising real Rust conversion and writing inspectable DOCX/PDF examples to ignored `Tests/generated/`. See `Tests/fixtures/README.md`. Account/file/bookmark/admin regressions and Rust workspace checks must also pass.

QR PNGs use the cross-platform `PngByteQRCode` renderer from [QRCoder](https://github.com/Shane32/QRCoder). Its MIT notice is retained in `legal/QRCoder-LICENSE.txt` and included in backend build/publish output. No legacy letters, logos or signatures are bundled.
