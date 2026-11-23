# Security review (Phase 3)

**Date:** 2026-11-23  
**Scope:** Papyrus product layers (`Back/`, `Front/`, tests, CI). WordCraft engine (`Application/`) assessed at integration boundaries only.  
**This is not a penetration test or formal certification.**

---

## 1. Threat model (summary)

| Asset | Primary risks |
| --- | --- |
| Private documents (SQLite + files) | IDOR, broken authz, unsafe uploads, converter escape |
| User sessions | Session fixation, CSRF on mutations, credential stuffing |
| Hosted editor (WASM + bridge) | XSS in parent, cross-origin `postMessage`, token theft |
| Automation tokens | Scope escalation, secret leakage, replay |

**Assumption:** Single-tenant or small-team **self-hosted** deployment; attackers may be authenticated users or anonymous upload/API abusers.

---

## 2. Controls in place

| Area | Control | Verification |
| --- | --- | --- |
| Authentication | ASP.NET Core Identity, cookie sessions | `Papyrus.Api.Tests` endpoint coverage |
| Authorization | Owner checks, grants, admin policy | `AuthorizationCoverageTests`, automation tests |
| CSRF | Double-submit cookie + `X-CSRF-TOKEN` on API mutations | Front `httpCore.js`, API tests |
| Rate limiting | Global + account registration/login partition | `ApiTraffic`, capacity tests |
| Upload / archive | Size limits, ZIP entry count, traversal paths | `DocumentConverter`, `converter_hostile_smoke.py` |
| Converter | Subprocess isolation, timeouts, slot caps, process tree kill | `ConverterCapacity`, CLI smokes |
| Editor embed | `sandbox` + origin-checked `postMessage` | `editor-host.test.mjs`, `docs/SECURITY_MODEL.md` |
| CSP | Response header on API (stricter on app, relaxed Swagger) | `file_api_smoke.py` |
| Secrets | `App_Data/` gitignored; no keys in repo | Policy in `AGENTS.md` |
| Account binding | `X-Papyrus-Account` on editor saves | `editor-host.test.mjs`, API middleware |

---

## 3. Gaps and backlog

| ID | Risk | Severity | Status / next step |
| --- | --- | --- | --- |
| SR-1 | No automated SAST (CodeQL) | Medium | Phase 7 — enable GitHub CodeQL workflow |
| SR-2 | `cargo xtask ci` not in CI | Medium | P0-2 — document local gate + optional nightly |
| SR-3 | Playwright browser security flows not in CI | Low | Phase 4 — P2-1 |
| SR-4 | Multi-instance job runner / horizontal scale | Low | Design only (`PRODUCTION_ROADMAP.md`) |
| SR-5 | Object storage abstraction | Low | Not implemented; local disk only |
| SR-6 | Dependency drift | Medium | **Addressed:** CI `dotnet list package --vulnerable` + `cargo audit` job |

---

## 4. Dependency audit (2026-11-23)

| Stack | Command | Result |
| --- | --- | --- |
| .NET (`Papyrus.Api`) | `dotnet list package --vulnerable --include-transitive` | **No vulnerabilities** reported (NuGet.org) |
| Rust (`Application/`) | `cargo audit` | Run in CI (`dependencies` job) |

Re-run after changing `PackageReference` or `Cargo.lock`.

---

## 5. Recommendations (prioritized)

1. Keep **hostile conversion** and **authorization coverage** tests mandatory in CI (already in `checks.yml`).
2. Add **CodeQL** for C# and JavaScript when opening the project to broader contributors.
3. Rotate **automation tokens** and document scopes in operator runbooks (`Back/README.md`).
4. Before public multi-tenant hosting: revisit **instance lock**, **backup encryption**, and **WAF** — not in current scope.

---

## 6. Verification commands

```powershell
dotnet test Tests/Papyrus.Api.Tests/Papyrus.Api.Tests.csproj -p:SkipRustBuild=true
python Tests/converter_hostile_smoke.py
python Tests/file_api_smoke.py
dotnet list Back/Papyrus.Api.csproj package --vulnerable --include-transitive
```