# Converter CLI integration protocol

Papyrus's .NET API invokes **`papyrus-cli`** as a separate process. This document is the versioned contract between them. It describes what the API relies on today (`protocolVersion` **1**).

## Roles

| Component | Responsibility |
| --- | --- |
| ASP.NET Core (`DocumentConverter`) | Validates uploads, enforces pool limits, writes temp files, spawns CLI, parses reports, maps errors to HTTP |
| `papyrus-cli` | Opens documents, runs engine commands, writes outputs |
| Rust engine | Document semantics (not part of this wire protocol) |

## Process invocation

- **Executable:** `Rust:Executable` in configuration, or `papyrus-cli` / `papyrus-cli.exe` next to `Papyrus.Api`.
- **Working directory:** An isolated temp folder per job (`papyrus-convert-*`). All relative paths in scripts resolve inside it.
- **No shell:** Arguments use `ProcessStartInfo.ArgumentList` (no string interpolation of user content).
- **Timeouts:** `Conversion:PreflightTimeoutSeconds` (default **30**) for `document.preflight`; `Conversion:TimeoutSeconds` (default **60**) for imports, exports, and other commands (range 1–600). API request cancellation is linked to the same timer. On engine timeout the API kills the **entire process tree** and increments `papyrus.converter.timeouts`.
- **Concurrency:** `Conversion:ValidationSlots` and `Conversion:ConversionSlots` (default 2 each). `document.preflight` uses the validation pool; conversions and other commands use the conversion pool. Queue wait is at most 5 seconds (HTTP 429 `converter_busy`).

## Operations used by the API

| API use | CLI mode | Notes |
| --- | --- | --- |
| Import / export | `convert <in> <out>` | Formats: docx, odt, rtf, txt, md, json, pdf, … |
| Blank / localized document | `run --template blank` + optional `--cmd` | Saves native JSON |
| Engine commands | `run --file <model> --script <job.json> [--save OUT] [--print]` | Primary automation path |

## Script JSON (`protocolVersion` 1)

Written to `job.json` in the job directory:

```json
{
  "commands": [
    { "command": "document.bookmarks", "params": {} }
  ]
}
```

Rules (enforced by CLI):

- Maximum script size: **4 MiB** (`MAX_SCRIPT_BYTES` in `papyrus-cli`).
- Maximum commands per script: **100 000**.
- At most **64** save targets per script.
- Command ids and params match the engine registry (`papyrus-cli commands --json`).

The API serializes:

```json
{ "commands": [{ "command": "<id>", "params": <object> }] }
```

Optional keys in full CLI scripts (`file`, `template`, `save`) are not used by the API today.

## Stdout report contract (`--print`)

When the API passes `--print`, it expects **one line per command**:

```
<command-id>: <json>
```

Example:

```
document.bookmarks: {"bookmarks":[{"name":"Body","replaceable":true}]}
```

The API parser:

1. Requires the line to start with `<command>:` (same string as the `command` field).
2. Deserializes the remainder as JSON.
3. Rejects reports larger than **2 MiB** (overflow → invalid report).

Stderr is drained and not returned to clients. Engine diagnostics must not be required for success.

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Success |
| Non-zero | Failure (conversion or command error) |
| 3 | `convert --strict` / render strict mode: output refused (not used by API today) |

The API maps non-zero exit or missing output files to HTTP **422** with a generic conversion message unless a more specific `ConversionException` was thrown earlier.

## Input and output limits (API + CLI)

| Limit | Value |
| --- | --- |
| Upload (configurable) | Default 10–128 MiB (`UploadPolicies`) |
| Native model / processing ceiling | 256 MiB (`FileLimits.MaxModelBytes`) |
| DOCX/ODT archive entries | ≤ 2048 |
| Archive declared uncompressed total | ≤ 256 MiB |
| Per-entry declared uncompressed size | ≤ 128 MiB |
| Zip path traversal | Rejected at API (`../`, leading `/`) |
| Serialized `job.json` from API | ≤ 4 MiB |
| Captured `--print` stdout | ≤ 2 MiB |

Archive checks use ZIP central-directory metadata before Rust runs. They do not prove every byte expands safely; hostile-input regression tests (`Tests/converter_hostile_smoke.py`) guard against hangs.

## Error mapping (API)

| Condition | HTTP | `code` (when set) |
| --- | --- | --- |
| Unsupported format | 415 | — |
| Upload/model/archive too large | 413 | `upload_too_large`, `model_too_large`, `archive_too_large` |
| Invalid JSON/archive | 422 | — |
| Converter missing | 503 | — |
| Timeout / overload | 422 / 429 | `converter_busy` |

Failures and timeouts increment `papyrus.converter.failures` / `papyrus.converter.timeouts` with `pool` and `exit_code` labels only (no document or account ids).

## Version compatibility

- **`protocolVersion`:** `1` (this document).
- **Engine version:** Workspace crate version (e.g. `0.18.1`); not yet sent on the wire. Future: CLI `--version` output pinned in deployment manifests.

## Verification

```powershell
dotnet build Back/Papyrus.Api.csproj
python Tests/papyrus_cli_protocol_smoke.py
python Tests/converter_hostile_smoke.py
```

See also [Back/README.md](../Back/README.md) and [Application/docs/architecture.md](../Application/docs/architecture.md).