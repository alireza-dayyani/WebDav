# Testing guide (Phase 4)

## Layers

| Layer | Command | Location |
| --- | --- | --- |
| API integration | `dotnet test Tests/Papyrus.Api.Tests/Papyrus.Api.Tests.csproj -p:SkipRustBuild=true` | `Tests/Papyrus.Api.Tests/` |
| Front units | `node --test Tests/*.test.mjs Tests/*.test.mjs` (see CI list) | `Tests/` |
| Editor bridge | `Tests/editor-host.test.mjs` | mocked HTTP |
| Browser (mocked API) | `npm run test:browser` | `Front/tests/` |
| Python smokes | `python Tests/papyrus_cli_protocol_smoke.py`, `converter_hostile_smoke.py`, `backup_test.py`, `i18n_document_smoke.py` | subprocess / HTTP |
| Rust workspace | `cargo check --workspace --locked` in `Application/` | engine |

## Quality report

From repository root:

```powershell
python Scripts/quality_report.py
```

Writes `docs/QUALITY_REPORT.md` using live command output (not fabricated counts).

## Performance baseline (Phase 5)

```powershell
python Scripts/performance_baseline.py
```

Writes `docs/PERFORMANCE_BASELINE.md`. See also `docs/PERFORMANCE_RELIABILITY.md`.

## International documents (Phase 6)

```powershell
dotnet test Tests/Papyrus.Api.Tests/Papyrus.Api.Tests.csproj --filter I18nFixtureTests -p:SkipRustBuild=true
python Tests/i18n_document_smoke.py
```

Fixtures: `Tests/fixtures/i18n/`. See `docs/I18N_SUPPORT.md`.

## CI

`.github/workflows/checks.yml` — `api-and-web`, `rust-engine`, `dependencies`, `browser`.

`.github/workflows/codeql.yml` — CodeQL SAST for C# and JavaScript/TypeScript on `main` (Phase 7).