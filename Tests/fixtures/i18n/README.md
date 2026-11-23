# International document fixtures

Small, commit-friendly samples for Phase 6 regression. Native models are **compact** engine JSON (UTF-8 byte `len` on runs, minimal `lastSection`); `mixed-scripts.txt` is for plain-text import smoke.

| File | Intended language | Notes |
| --- | --- | --- |
| `en-us-basic.json` | `en-US` | LTR Latin |
| `fa-ir-basic.json` | `fa-IR` | Persian letters |
| `ar-sa-basic.json` | `ar-SA` | Arabic (distinct letters where possible) |
| `ur-pk-basic.json` | `ur-PK` | Urdu-specific letters |
| `mixed-bidi.json` | mixed | RTL run inside LTR paragraph |
| `mixed-scripts.txt` | mixed | Plain-text import |

Run:

```powershell
python Tests/i18n_document_smoke.py
dotnet test Tests/Papyrus.Api.Tests/Papyrus.Api.Tests.csproj --filter I18nFixtureTests
```