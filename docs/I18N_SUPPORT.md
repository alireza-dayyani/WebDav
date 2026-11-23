# International document support (Phase 6)

What Papyrus supports **today** for document content vs the **website UI**. This is not a marketing claim; gaps are listed explicitly.

## Document languages (engine / API)

| Tag | Detection | New document | Letter templates | Converter blank |
| --- | --- | --- | --- | --- |
| `en-US` | Latin text, default chr lang | Yes | English profiles | Yes |
| `fa-IR` | Persian letters, `fa` tags | Yes | Persian A4/A5 official | Yes |
| `ar-SA` | Arabic-specific letters | Yes | Via model/import | Yes |
| `ur-PK` | Urdu-specific letters | Yes | Via model/import | Yes |

Detection logic: `Back/Services/DocumentLanguage.cs` (script heuristics + `lang` tags in JSON). Covered by `DocumentLanguageTests` and `I18nFixtureTests`.

## Website UI locales

| Code | Direction | Notes |
| --- | --- | --- |
| `en` | LTR | Full message catalog |
| `fa` | RTL | Full message catalog |
| `ar`, `ur` | — | **Not** separate UI locales; document content may still be ar/ur |

Search normalization for Persian keyboard variants: `Front/src/locales.js` (`normalizeSearch`), tested in `Tests/locales.test.mjs`.

## RTL / bidi

- Vue shell sets `dir` from active UI language (`fa` → RTL).
- Editor RTL/bidi follows the WordCraft engine (WASM) and native model `rtl` run properties.
- Fixture `Tests/fixtures/i18n/mixed-bidi.json` exercises an RTL run inside LTR paragraph (language resolves to `fa-IR` for storage heuristics).

## Regression assets

Committed under `Tests/fixtures/i18n/` (see README there). Generated letter/DOCX samples remain under ignored `Tests/generated/` via `letter_template_layout_smoke.py`.

## Commands

```powershell
dotnet test Tests/Papyrus.Api.Tests/Papyrus.Api.Tests.csproj --filter I18nFixtureTests -p:SkipRustBuild=true
python Tests/i18n_document_smoke.py
python Tests/letter_template_layout_smoke.py
```

## Known limits (honest)

| Topic | Limit |
| --- | --- |
| Large corpora (500+ pages) | Not acceptance-tested in this repo; see upstream `Application/docs/gaps.md` |
| Web UI in Arabic/Urdu | Not implemented — English and Persian UI only |
| Typography per locale | Depends on installed fonts; see font download list in engine |
| OCR / complex scripts outside listed langs | Not supported unless engine accepts import |