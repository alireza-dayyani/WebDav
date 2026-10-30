# Document automation API

These endpoints work with saved private documents without opening the editor. Upload DOCX through the existing upload API, inspect the returned document ID, apply typed operations, then download DOCX or PDF. All editing uses the Rust command registry. Clients never supply server file paths or arbitrary engine commands.

Use the existing session cookie and `X-CSRF-TOKEN` for browser clients, or a scoped bearer token for scripts. Cookie-authenticated document reads accept ownership or an explicit reader/editor grant; editing accepts ownership or editor access. Complete history, restore, sharing administration, Trash, template copies and multi-source/background job inputs remain owner-only. Scoped tokens always retain the owner-only boundary. Another account's unauthorized resource returns 404, including for administrator callers. New automation reads require the expected document version; stale versions return 409. New mutations retain the original document on any error. Import/export uses the native document model and does not promise lossless preservation of unsupported Word features.

## Library, retention, sharing and approvals

All routes require authentication. Cookie mutations also require CSRF. The new library, retention and review groups are cookie-only; automation clients retain the existing scoped document/job APIs.

| Method | Route | Contract |
| --- | --- | --- |
| GET | `/api/library?q=term&page=1&trash=false` | Title and native-text prefix search; all terms must match. Returns `items`, `page`, `total`, `hasMore`. 50 items/page, 200 query characters, 16 literal terms; FTS query operators are escaped. Persian/Arabic letters and combining marks are normalized. Shared active documents are included; Trash remains owner-only. No content snippets from inaccessible documents are returned. |
| GET/PUT | `/api/retention/` | GET returns the account policy (version 0 when absent). PUT accepts `{version,enabled,keepLatest,days}`; 5–1000 latest revisions, 7–3650 days. Version conflicts return 409. Disabled by default. |
| POST | `/api/retention/preview` | `{version}` returns eligible revision count and content bytes. No deletion. |
| POST | `/api/retention/apply` | `{version}` removes eligible owned history only when the current policy is enabled. First/current/latest versions and every submitted review revision are excluded. Enabling also applies the policy on subsequent document saves, including Trash. |
| GET | `/api/documents/{id}/access` | Current metadata and `permission`: `owner`, `read`, or `edit`. |
| GET/PUT | `/api/documents/{id}/shares` | Owner GET requires `?version=currentDocumentVersion`. PUT accepts `{version,email,permission:"read|edit",grantVersion:0}`; updating requires the existing grant version. Only existing registered accounts can receive grants. |
| DELETE | `/api/documents/{id}/shares/{userId}?version=...&grantVersion=...` | Owner-only revocation with both expected versions. Future access stops; previously downloaded copies cannot be recalled. |
| GET | `/api/documents/{id}/audit?version=...&page=1` | Owner-only, 50 recent events/page with `hasMore`. Append-only application events include actor ID, document version, action, timestamp and detail. This is operational history, not a tamper-proof ledger. |
| POST | `/api/documents/{id}/reviews` | Owner submits `{version,reviewerId}`. The reviewer must already have a grant; the owner cannot self-approve. The exact native revision and its SHA-256 content hash are retained. Duplicate pending requests for the same reviewer/revision conflict. |
| GET | `/api/reviews/?page=1` | Owner and currently authorized assigned reviewer inbox; 50/page. Compare `currentVersion` with `documentVersion` to detect staleness. |
| POST | `/api/reviews/{id}/decision` | `{version,documentVersion,decision:"approved|returned|cancelled",note:""}`. Only the assigned reviewer can approve/return a pending current revision; only its owner can cancel. Notes: 2000 characters. Both expected versions are checked. Editing invalidates approval applicability without rewriting the earlier decision. |
| GET | `/api/reviews/{id}/download?version=requestVersion` | Owner or authorized assigned reviewer receives PDF rendered from the retained native revision using the current engine/fonts. Revocation and Trash hide it from reviewers. These are internal decisions, not certificate signatures. |

The full-text index is updated in the same transaction as saves/revisions and backfilled for existing installations. Indexing covers the first one million title/text characters, including nested document stories; scanned-image OCR is not included. Purging a document removes its search entry. The index is private database data and is included in normal database backups. Retention excludes database overhead and does not compact the SQLite file; freed pages can be reused by SQLite. Approval evidence remains retained until permanent document deletion.

Editor writes include a concurrency check on the grant, so revocation/downgrade during conversion rolls back the document, revision, search index and operation receipt. Revoked grants are hidden from access checks and share listings but retain their version counter. Re-granting uses `grantVersion:0` and returns a new, higher version; clients must use that returned version for subsequent updates or revocation. Shared read-only previews create results owned by the requesting account. Source-copy endpoints intentionally require ownership rather than inheriting grants, and existing tokens cannot manage sharing or reviews.

## Inspect, edit, preview, export

All document routes below start with `/api/documents/{id}`.

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/structure?version=1&offset=0&limit=200` | Paragraphs, formatting runs, nested paths, tables, parts and section properties |
| POST | `/search` | Find literal text across formatting runs, returning ranges and context |
| POST | `/batch` | Apply 1–256 ordered operations atomically and save exactly one new version |
| POST | `/preview` | Apply a batch temporarily and create a private PDF or PNG artifact |
| GET | `/pages/{page}/preview?version=1&scale=1` | Render a saved page as PNG; pages are 1-based and scale is 0.25–2 |
| GET | `/tables?version=1` | Structured physical cells, spans and table references |
| PATCH | `/tables/{reference}` | Update cells and append rows |
| GET | `/controls?version=1` | Word content-control tags, types, values, locks and supported-fill flags |
| POST | `/controls/fill` | Fill typed content controls atomically |
| GET | `/styles?version=1` | Named styles, defaults and formatting definitions |
| POST | `/styles/apply` | Apply `format` operations using the batch request shape |
| GET | `/images?version=1` | Image references, positions, size and alternative text |
| POST | `/images` | Insert an image at a target |
| PUT | `/images/{reference}` | Replace an image |
| GET/PATCH | `/properties` | Read properties with `?version=1`; write core/custom properties and document language |
| GET/POST | `/comments` | Read with `?version=1`; add, reply, resolve or delete |
| GET | `/changes?version=1` | Inspect tracked changes and their anchored ranges |
| POST | `/changes/resolve` | Accept/reject all changes or changes touching an explicit target |
| POST | `/normalize` | Preview or apply selected Persian/Arabic normalization rules |
| POST | `/validate` | Document health, accessibility, placeholders, control placeholders, fonts and internal references |
| GET | `/versions?version=3&page=1` | Retained revisions, newest first |
| GET | `/versions/{revision}/download?version=3&format=docx` | Download a historical revision using the expected current document version |
| POST | `/restore` | Restore a retained snapshot as a new current revision |

`GET /api/document-capabilities` reports operations, supported types/scopes/formats and limits. Authentication is required. Structure returns at most 500 paragraphs per page and marks truncation at 4,000; search and discovery results also report truncation. Large reports fail within the 2 MiB report budget instead of returning incomplete JSON. Document references are valid only for the inspected version. Operations execute in array order, so later positions refer to the model after earlier operations.

### Example

Find content first:

```http
POST /api/documents/{id}/search
Content-Type: application/json

{"version":7,"text":"Customer","scope":"all","matchCase":true,"wholeWord":true}
```

Apply an edit and formatting together:

```json
{
  "version": 7,
  "idempotencyKey": "order-1042-update",
  "operations": [
    {
      "type": "replaceText",
      "find": "{{CustomerName}}",
      "replacement": "علی رضایی",
      "scope": "body",
      "expectedMatches": 1
    },
    {
      "type": "format",
      "target": {"ref":"b.0"},
      "values": {"style":"Heading1","rtl":true,"bold":true}
    }
  ]
}
```

Send this to `/batch`. Response: `{metadata, report:{results,warnings}}`. The metadata contains version 8. A count mismatch or any failed operation prevents the entire save. A concurrent modification returns 409. To preview first, send `{version,operations,format:"pdf"}` to `/preview`; its response contains an artifact `downloadUrl`, base revision and operation report. For a PNG preview, use `format:"png",page:1,scale:1`. Preview artifacts expire after 24 hours. The document's current version does not change. Use the existing `/download?version=8&format=docx` after saving.

### Addressing and operation types

References use `b.0` for the first body block and `p12.0` for the first block of part 12. Nested table paths append row, physical cell and child-block indexes, e.g. `b.1.0.1.0`. Image references append the object's byte offset, e.g. `b.0@6`. Use references returned by inspection instead of constructing them when possible.

Targets accept one of:

```json
{"bookmark":"Body"}
{"ref":"b.0","start":0,"end":8}
{"position":{"story":"body","path":[0],"off":0}}
{"range":{"start":{"story":"body","path":[0],"off":0},"end":{"story":"body","path":[0],"off":8}}}
```

Omitting start/end with a paragraph reference selects the paragraph's text. Offsets are **UTF-8 bytes**, not UTF-16 code units or character counts. End is exclusive. Invalid Unicode boundaries, missing targets and ranges across stories are rejected. Bookmark selectors retain existing bookmark-range restrictions.

| Operation type | Parameters beyond `type` |
| --- | --- |
| `replaceText` | `find`, `replacement`, optional `scope` (`body`/`all`), `matchCase`, `wholeWord`, `expectedMatches` |
| `insertText` | `target`, `text`, optional `paragraphs`; inserts at target start |
| `replaceRange` | `target`, `text`, optional `paragraphs` |
| `insertParagraph` | `after` target, `text`, optional `style`; inserts a paragraph at target end |
| `format` | `target`, `values` supporting `style`, `bold`, `italic`, `font`, `size`, `color`, `rtl`, `align`, `spacing`, `lineSpacing`, `language` |
| `fillBookmarks` | `values` map of exact bookmark names to literal text |
| `updateTable` | `ref`, optional `cells:[{row,column,text}]`, `appendRows:[["A","B"]]` |
| `fillControls` | `values` map of unique control tags/titles to typed values |
| `insertImage` | `target`, base64 `data`, optional `width`, `height`, `alt` |
| `replaceImage` | image `ref`, base64 `data`, optional size/alt |
| `properties` | `values` containing `title`, `subject`, `author`, `keywords`, `comments`, `category`, `custom`, `language` |
| `normalize` | `rules` array |
| `addComment` | `target`, `text` |
| `replyComment` | numeric `id`, `text` |
| `resolveComment` | numeric `id`, optional `resolved` boolean (default true) |
| `deleteComment` | numeric `id` |
| `trackChanges` | `enabled` boolean |
| `resolveChanges` | `decision` (`accept`/`reject`), `all:true` or `target`, optional `stopTracking` for all changes |

Formatting size and image dimensions are in points. Colors use six hex digits or `auto`. Spacing uses `{before,after}` in points; lineSpacing accepts `{value:1.5}` (multiple), `{atLeast:14}` or `{exactly:14}` (points). Custom properties map names to strings, with null removing a property. Image payloads are decoded and capped at 512 KiB and 4,096 pixels per dimension. Table indexes address physical cells, including merged cells; appended rows copy the last row's structure and require one value per physical cell. A nested-table cell requires edits to its individual paragraphs instead of replacing the entire cell. Content-control and document protection remain enforced.

Batch input is capped at 3 MiB after JSON serialization within a 4 MiB transport allowance. Text values are at most 512 KiB each; object-marker characters and unsupported control characters are rejected. Replacement text is literal. Duplicate JSON object keys are rejected inside operations. Existing conversion time, concurrency, archive and native-model limits apply.

Shortcut payloads:

- `/properties` and `/controls/fill`: `{version,values,idempotencyKey?}`.
- `/tables/{reference}`: `{version,cells?,appendRows?,idempotencyKey?}`.
- `/images` and `/images/{reference}`: `{version,target,data,width?,height?,alt?,idempotencyKey?}`; replacement accepts an empty target object because the route identifies the image.
- `/comments`: `{version,action:"add|reply|resolve|delete",target,text?,id?,resolved?,idempotencyKey?}`. Non-add actions accept an empty target object.
- `/changes/resolve`: `{version,decision:"accept|reject",all:true,target:{},stopTracking?,idempotencyKey?}`; replace `all:true` with `all:false` and an explicit target for selected changes.
- `/normalize`: `{version,rules,apply:false}` previews changes. `apply:true` saves one new version and accepts `idempotencyKey`.
- `/validate`: `{version}`.
- `/restore`: `{version,sourceVersion,idempotencyKey?}`.

Normalization rules: `persianLetters` maps Arabic Yeh/Kaf to Persian forms; `persianDigits` and `latinDigits` select one digit system; `trimTrailingSpaces` removes trailing horizontal whitespace; `halfSpace` replaces spaces immediately following the Persian prefixes `می`/`نمی` with ZWNJ. Half-space changes are heuristic and opt-in; preview before applying. Changes crossing embedded objects are refused. Validation reports server font availability, broken internal bookmark links and REF/PAGEREF/NOTEREF targets, rather than checking external links or every Word field type.

Content-control fill accepts strings for plain/rich text and dates (`YYYY-MM-DD`), booleans for checkboxes, stored list values for dropdowns/combo boxes, and `{data,width?,alt?}` for pictures. Tags/titles must resolve uniquely. Editable repeating sections surrounding body paragraphs accept arrays of objects, each object supplying child-control values. At most 100 items and eight levels of repetition are accepted. Repetition requires no comments, pending revisions or enabled Track Changes, and template items cannot contain named bookmarks. Table/row/cell wrapper controls are preserved by import/export but are not advertised as fillable repeating sections. Locked controls and disallowed item insertion are rejected.

### Retry behavior and revisions

An optional `idempotencyKey` of 1–128 printable characters binds the request to its exact serialized input and owner/resource scope. Retrying the same successful request returns its original response without applying it again, even if the current version advanced. Reusing the key with different input returns 409. Simultaneous first attempts can return one success and one 409; retry the same input/key to obtain the retained result. Failed requests leave no receipt. Batch shortcuts share the document's batch-key scope.

All ordinary document saves, renames and automation mutations retain full snapshots atomically with the document write. Restoring creates a new revision. DELETE moves a document to Trash and advances its version; ordinary reads, downloads and automation exclude it. GET `/api/documents/trash` lists owned trash. POST `/api/documents/{id}/recover` with `{version}` returns it to the library as another version. DELETE `/api/documents/{id}/purge?version=...` permanently removes an owned trashed document and its history; active documents cannot be purged. Existing installations retain their current snapshot on their next save; historical content from before this upgrade cannot be reconstructed. Template and letter resources continue using their separate version/snapshot contracts.

## Compare, assemble and generate in bulk

`POST /api/documents/compare` creates a new ordinary document:

```json
{"original":{"id":"GUID","version":1},"revised":{"id":"GUID","version":2},"title":"Comparison","level":"word","formatting":true,"caseChanges":true,"whiteSpace":true,"idempotencyKey":"compare-42"}
```

The response has `{metadata,report,warnings}`. Sources stay unchanged and must be unprotected with no pending tracked changes. Supported comparison covers body text, tables and modeled formatting at word/character level. Headers, footers, text boxes, comments, moves and other stories are not fully compared. The output's tracked changes can be reviewed/resolved through the ordinary review endpoints.

`POST /api/documents/compose` creates a new ordinary document:

```json
{"sources":[{"id":"GUID-A","version":1},{"id":"GUID-B","version":3}],"title":"Contract package","separator":"pageBreak","stylePolicy":"preserveSource","idempotencyKey":"package-42"}
```

Accepts 1–32 sources, at most 256 MiB of combined native input. Source body content is inserted in order. `preserveSource` remaps conflicting styles, numbering and media; separators are `paragraph` or `pageBreak`. The first source supplies document sections/headers/footers. Subsequent source sections, headers and footers are not copied. Appended sources with comments, pending revisions or protection are rejected rather than silently flattened.

`POST /api/templates/{id}/render-batch` extends ordinary document-template rendering:

```json
{"version":1,"idempotencyKey":"invoices-42","records":[{"key":"customer-1","title":"Invoice 1","values":{"CustomerName":"علی رضایی"}},{"key":"customer-2","title":"Invoice 2","values":{"CustomerName":"Example customer"}}]}
```

Accepts 1–100 records with unique keys and creates separate owned documents for successful records. The response contains per-record success, metadata or validation errors. Engine validation failures affect only the corresponding record; concurrency/storage/size failures abort the database commit. Templates stay unchanged. Bulk output is capped at 256 MiB. Use a background `renderBatch` job for lengthy runs or an output ZIP.

## Background jobs

| Method | Route | Purpose |
| --- | --- | --- |
| POST | `/api/document-jobs/` | Queue an operation against captured owned source revisions |
| GET | `/api/document-jobs/?page=1` | List private jobs and their latest versions |
| GET | `/api/document-jobs/{id}?version=1` | Poll from a known version; returns newer state, rejects future/nonpositive versions |
| POST | `/api/document-jobs/{id}/cancel` | Cancel queued/running work with `{version}` |
| GET | `/api/document-jobs/{id}/download?version=3` | Download a completed result using its exact current job version |

Job request examples:

```json
{"kind":"export","source":{"id":"GUID","version":1},"format":"pdf","idempotencyKey":"export-42"}
{"kind":"batch","source":{"id":"GUID","version":1},"operations":[{"type":"replaceText","find":"old","replacement":"new"}],"format":"docx"}
{"kind":"compose","sources":[{"id":"GUID-A","version":1},{"id":"GUID-B","version":1}],"separator":"paragraph","format":"pdf"}
{"kind":"renderBatch","template":{"id":"TEMPLATE-GUID","version":1},"records":[{"key":"a","title":"First","values":{"CustomerName":"Alice"}}],"format":"docx"}
```

Jobs return `{id,kind,status,version,progress:{stage},report,downloadUrl,...}`. Stages are `queued`, `running`, `succeeded`, `failed`, `cancelled`. Progress reflects stages, not estimated percentages. Background batches generate downloadable copies and never overwrite a source document. Bulk generation creates a ZIP with numbered output files and a `manifest.json` mapping original record keys/titles to results; it does not populate the document library. All referenced models are captured on submission, so later source edits do not affect the job. Active work is cancelled when possible and cannot publish a result after cancellation commits. Jobs interrupted by a server restart become failed; submit again with a new key. The built-in worker is intended for a single API instance.

At most ten queued/running jobs and 100 retained jobs/previews per account are allowed. Terminal jobs, previews and job retry receipts expire after 24 hours. Job input snapshots are cleared on completion, failure or cancellation. Outputs remain private to their owner. The normal converter's two execution slots and 60-second timeout apply to each engine/export operation. A bulk job checks cancellation between records.

## Scoped automation tokens

Create tokens with an authenticated **cookie session** and CSRF token:

```http
POST /api/automation-tokens/

{"name":"Invoice service","scopes":["documents:write","templates:read","jobs:write"],"expiresInDays":30}
```

Creation returns `{id,name,scopes,version,expiresAt,token}`. The secret is displayed once and only its SHA-256 hash is stored. Send `Authorization: Bearer ink_...` on API requests; no cookie or CSRF token is needed for valid header credentials, including multipart uploads. Invalid/expired/revoked tokens return 401 and never fall back to a browser cookie. Token scopes are checked on every request, account lockout is respected, and resource owner checks still apply.

Scopes: `documents:read`, `documents:write`, `templates:read`, `templates:write`, `jobs:read`, `jobs:write`. Write implies read for the same area. Template snapshot creation also needs document read; rendering into the library needs document write. Job submission needs read access to its source area (documents or templates). Tokens cannot access account administration, the administrator API, letter workflows or token management. Use cookie sessions for those workflows. Token names are 1–80 characters, lifetime is 1–365 days, and each owner can retain up to 50 tokens.

`GET /api/automation-tokens/` lists metadata, never secrets/hashes. `DELETE /api/automation-tokens/{id}?version=1` revokes a token immediately. Token management requires a cookie session and CSRF protection.

## Verification

Run `dotnet test Tests/Inkstone.Api.Tests/Inkstone.Api.Tests.csproj -p:SkipRustBuild=true` for C# unit and endpoint contract tests without launching Rust. See [the test guide](../Tests/Inkstone.Api.Tests/README.md) for isolation, coverage and the distinction from full-stack integration checks.

Automation requests are validated before engine work. Invalid request contracts return 400, invalid operation values return 422, and excessive inputs return 413, with `application/problem+json` responses containing an `errors` map keyed by field/operation path. Malformed JSON or unmapped request properties also return 400 through ASP.NET binding. Positive versions, resource IDs, allowed fields, target/reference shapes, text/image limits, job-kind requirements and token scopes are checked at the API boundary. Actual Unicode offsets in the saved text, bookmark/control existence, protection and format fidelity are checked by the engine. Scope authorization treats route case and trailing slashes consistently with ASP.NET routing.

Run `python Tests/document_automation_smoke.py` after building the native converter/backend. It uses disposable databases and real HTTP/native engine calls, covering DOCX import/edit/export, owner isolation, expected versions, rollback, retries, history, previews, controls, tables, review, comparison, assembly, bulk rendering, background jobs and bearer credentials. Run the existing API suites for regression coverage and `cargo xtask ci` for the Rust workspace, including WebAssembly compatibility.

Verified on Windows on 2026-10-28:

- All eight Rust CI stages passed, with 4,593 passing tests, zero failures and four existing ignored tests. All 16 WebAssembly crates compile. A subsequent focused engine Clippy check also passed.
- The native converter and .NET API build with zero errors. C# compilation is clean; Cargo reports four existing unused optional egui patch warnings.
- All 216 C# request-validator and endpoint contract tests pass with isolated SQLite/TestServer storage. OpenAPI documents the validation responses as `application/problem+json`.
- All ten API suites passed, including the new automation suite against the refreshed native converter. The final checks cover malformed guards, references to header bookmarks, live token expiry and cascading deletion of history.
- All 13 JavaScript checks passed. Headless Edge verified sign-in, Persian document creation, mixed-direction input, acknowledged cloud saves and reopening against the updated backend; the rendered result was inspected.
- Generated documents, screenshots, binaries, test databases and logs remain outside tracked source.
