# Contributing to Papyrus

Thank you for helping improve **Papyrus** — the Vue shell, .NET API, and hosted WordCraft engine integration in this repository.

## Before you start

1. Read the root [README](README.md), [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md), and [docs/TESTING.md](docs/TESTING.md).
2. Security-sensitive reports belong in [SECURITY.md](SECURITY.md), not public issues.

## What we welcome

| Area | Location | Notes |
| --- | --- | --- |
| API and authorization | `Back/` | Keep business logic out of controllers; DTOs ≠ entities |
| Vue workspace UI | `Front/` | Composition API, PrimeVue, Pinia; HTTP only via `Front/src/services/` |
| Tests and CI | `Tests/`, `.github/workflows/` | Prefer real commands over fabricated counts |
| Product docs | `docs/` | Honest limits; no promotional inherited copy |
| Engine integration | `Back/`, `Front/public/` host paths | **Do not** mass-rename upstream `inkstone-*` crates without an approved plan |

Large changes to `Application/` should stay aligned with upstream [WordCraft](https://github.com/storytold/wordcraft). Record engine baseline changes in `Application/upstream.rev` when merging upstream.

## Local setup

See README **Run locally** and **Builds and checks**. Minimum verification before a PR:

```powershell
dotnet test Tests/Papyrus.Api.Tests/Papyrus.Api.Tests.csproj -p:SkipRustBuild=true
cd Application
cargo check --workspace --locked
cd ../Front
npm ci
npm run build
```

If your change touches conversion, the CLI, or i18n fixtures:

```powershell
dotnet build Back/Papyrus.Api.csproj
python Tests/i18n_document_smoke.py
python Tests/papyrus_cli_protocol_smoke.py
python Tests/converter_hostile_smoke.py
```

If your change touches the Vue shell layout or locales:

```powershell
cd Front
npm run test:browser
```

CI runs `.github/workflows/checks.yml` and CodeQL on `main` (see `.github/workflows/codeql.yml`).

## Pull request guidelines

- **Scope:** One logical change per PR when possible; explain *why* in the description.
- **Privacy:** Every file operation must respect owner authorization and version checks.
- **Secrets:** Never commit `Back/App_Data/`, `.env`, databases, data-protection keys, or user documents.
- **Assets:** Use openly licensed material; update attribution where required (`Front/public/licenses.txt`, `Back/legal/`).
- **Tests:** Add or extend coverage when behavior changes; flaky tests must be fixed or quarantined with justification.
- **Breaking API changes:** Update OpenAPI consumers and `Back/AUTOMATION_API.md` / `Back/STUDIO_API.md` when routes or contracts change.

## Code style (summary)

- **C#:** Async endpoints, AutoMapper for DTOs, EF Core confined to infrastructure.
- **Vue:** PrimeVue components; forms with yup; tables via PrimeVue DataTable (server-side where applicable).
- **Rust (product fork):** Match surrounding `Application/` conventions; run `cargo check --workspace --locked` before pushing engine-related work.

## Community

Participation is governed by [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

## Questions

Use GitHub Discussions or issues for design questions. For vulnerabilities, follow [SECURITY.md](SECURITY.md).