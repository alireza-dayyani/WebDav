# Upstream integration

Inkstone's Rust workspace derives from [storytold/wordcraft](https://github.com/storytold/wordcraft).
The engine is kept under `Application/`, with `inkstone` crate, executable and asset names.

The 2026-10-28 integration covers upstream changes from
`c524027f34be2d1322b23064f80662d3638cf880` through
[`c4fafc215490a0186ee750454b1ce73a9823daed`](https://github.com/storytold/wordcraft/commit/c4fafc215490a0186ee750454b1ce73a9823daed)
(697 commits reachable since the previous baseline). The workspace is now version 0.11.0.

Included changes cover bounded document, image and font processing; encrypted DOCX support;
layout and PDF export performance; section breaks and table pagination; Spanish proofing;
custom document properties; searchable command pickers; dockable task panes and document
windows; browser input handling; egui 0.37 and its pinned platform adapters; Windows installation
scope; and the upstream module and lint reorganization. Dependency revisions are recorded in
`Cargo.toml` and `Cargo.lock`.

Fork-specific integration keeps authenticated, version-checked cloud saves, failure acknowledgements
and ten-second autosaving of the active document; template/bookmark and letter commands; imported style remapping;
Persian and Urdu document languages; the bundled Noto Naskh Arabic font; and Inkstone's product
identity. The optional external translation input still reads `wordcraft.tsv`, the catalog's
actual filename in craft-i18n. The Vue frontend and standalone .NET API remain separate projects.

Upstream's release updater is replaced with operator-managed update information. Inkstone must
never download or install a Wordcraft release as its own update. Upstream's alternate standalone
`?host=parent` browser bridge is excluded: Inkstone's existing parent bridge continues to
authenticate the owner and check storage versions. The shared editor retains upstream's
host-save hooks. Upstream promotional controls,
release CI workflows and internal migration reports are excluded; the fork retains its own
instructions, project overview and assets. Required licenses, notices and original attribution
are preserved, including `legal/UPSTREAM-ATTRIBUTION.md` at the integrated revision.

With multiple open documents, browser leave warnings include unsaved work in inactive documents
and pending save acknowledgements. Switch Windows in the View tab returns to an earlier document.
Save Changes before closing a document waits for the server; a rejected save keeps the document
open and restores its unsaved state.

Windows adaptations keep directory rejection diagnostics consistent in the bounded dictionary
reader and compile a Unix-only MCP test helper only on Unix. Vite ignores Trunk's temporary
`public/editor/.stage` files so rebuilding the editor does not crash the development watcher.

For future integrations, compare against the upstream revision above, map paths and identifiers
to this workspace, and merge against the complete working copy. Do not overwrite uncommitted
work. Pay particular attention to `Services::cloud_save`, asynchronous save tickets, host document
identity, and command registration when upstream splits or moves editor modules. Run the Rust
workspace CI, build the converter and WebAssembly editor, and exercise the API and browser host.

## Validation on 2026-10-28

The pre-integration baseline passed all eight Rust CI stages. Final validation also passed:

- `cargo xtask ci`: formatting, strict Clippy, workspace tests, localization audit, doctests,
  asset attribution, dependency layers and wasm32 checks. 4,593 passing test executions,
  zero failures and four ignored tests, including both hosted-save regressions.
- The native 0.11.0 converter and .NET API build; all ten API integration suites, followed
  by another file-API run with the final converter. C# compilation is clean; the full build
  reports four existing unused optional egui patch warnings from Cargo and no errors.
- WebAssembly editor and Vue production builds; all 13 JavaScript tests.
- Headless Edge: Persian document creation, mixed English/Persian/Urdu typing, acknowledged
  cloud saves, versioned snapshots and reopening. Vite remained running through the final
  Trunk rebuild after its temporary staging directory was excluded from watching.
- Windows packaging-script policy check and source whitespace checks.

MSI compilation and installation were not exercised; WiX was unavailable in this session.
Generated bundles, build output, test databases and fixture credentials remain ignored.
Release packaging and publication were not exercised.
