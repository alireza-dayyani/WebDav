# Inkstone roadmap

The current application combines a Vue frontend, a .NET 10 account and storage API, and the Rust browser editor.

Current foundation: private accounts, native documents, acknowledged automatic saves, conflict
checks, English/Persian site languages, RTL documents, server-side file conversion, templates,
letters, administrator controls, and standalone document automation with revision history,
private background jobs and scoped tokens. See the root README and API guides for supported flows.

The Rust workspace integrates applicable Wordcraft changes through `3b7f7ed1` (0.12.0).
The source comparison, fork adaptations and validation are recorded in `docs/UPSTREAM_SYNC.md`.

Deployment configuration, sharing, collaborative editing and account recovery remain later
work. Engine format and rendering behavior is documented in docs; inherited parity assessments
do not establish hosted application readiness.
