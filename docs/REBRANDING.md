# Papyrus product identity

Papyrus is the product name for this repository's Vue workspace, .NET API, tests, CI, and hosted editor integration. The document engine under `Application/` tracks [WordCraft](https://github.com/storytold/wordcraft) upstream; internal crate paths there are not part of the product surface.

## Surface

| Area | Identity |
| --- | --- |
| Display name | **Papyrus** |
| .NET API | `Papyrus.Api` (`Back/Papyrus.Api.csproj`) |
| Tests | `Papyrus.Api.Tests` |
| Editor host | `window.papyrusHost`, `papyrus:` postMessage |
| Session cookies | `Papyrus.Session`, `Papyrus.Csrf` |
| SQLite | `App_Data/papyrus.db` |
| Account header | `X-Papyrus-Account` |
| Converter binary | `papyrus-cli` (next to the API) |

## Upgrades

- New installs use `papyrus.db` and Papyrus cookie names; sign in again after an API upgrade that changes cookies.
- Data protection application name: `Papyrus`.

## Verification

```powershell
dotnet test Tests/Papyrus.Api.Tests/Papyrus.Api.Tests.csproj -p:SkipRustBuild=true
python Tests/papyrus_cli_protocol_smoke.py
cd Front; npm run build
```

CI: `.github/workflows/checks.yml`.