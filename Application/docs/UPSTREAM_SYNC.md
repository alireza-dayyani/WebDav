# Upstream integration

Inkstone's Rust workspace derives from [storytold/wordcraft](https://github.com/storytold/wordcraft).
The engine is kept under `Application/`, with `inkstone` crate, executable and asset names.

## Integration on 2026-10-30

The current baseline is
[`3b7f7ed10b2eb0ae3c39b71551d36ed2df1d1d8b`](https://github.com/storytold/wordcraft/commit/3b7f7ed10b2eb0ae3c39b71551d36ed2df1d1d8b),
fetched from upstream `main`. This integration compares the complete working copy against
`c4fafc215490a0186ee750454b1ce73a9823daed` and covers 57 additional commits, including the
0.12.0 release. Later upstream commits require a new comparison.

Included engine and editor changes:

- Backspace removes Arabic combining marks without deleting their base letters, and retains
  floating shapes anchored immediately before the caret. Highlighting at the caret no longer
  changes the word; font-size stepping respects each selected run's size.
- Copy/cut works for whole tables, rows and cells. Table properties follow the selection;
  spanning-cell minimum widths follow preferred widths. Zero-size frames remain in normal flow.
- DOCX retains corporate bibliography authors, custom-property bookmark links, note comment
  ranges, run-level `snapToGrid`/`webHidden`, and implicit heading outline levels. Note conversion,
  comment export and connector rerouting avoid repeated full scans.
- HTML, Markdown, LaTeX and RTF fidelity and input bounds improve, including nested Markdown
  reference labels, integer-limit HTML font sizes and embedded WMF/EMF pictures. PDF export
  handles glyph IDs above 65535. Chart and SmartArt labels use fallback fonts.
- Clipboard pictures, shape pickers, object duplication, shading colours, Format Painter,
  WordArt focus, window geometry, open-file diagnostics and saved blank author names are fixed.
  Traditional Chinese UI fonts use an installed Taiwan-standard face where available.
- Recipient labels generate all required sheets and leave unused labels empty. FreeBSD target
  packaging, macOS notarization retries and Flatpak CUPS client support are integrated.

Fork adaptations preserve all existing automation/bookmark/letter commands, authenticated cloud
saves and acknowledgements, document identity/version checks, Persian/Urdu support and fonts,
Inkstone branding, and the standalone .NET API and Vue frontend. The lockfile retains the fork's
automation dependencies and adds the formats-to-metafile dependency for RTF pictures.

Wordcraft's updater changes and `craft-update` revision are deliberately excluded: Inkstone's
updates remain operator-managed. Flatpak retains networking disabled by default; operators can
explicitly enable it for remote printers or the optional control server. Upstream CI workflows,
instructions and promotional roadmaps do not replace this fork's project policies or scope.
Contributor-supplied credits and required licenses/notices are retained. No new media assets
are introduced.

### Validation on 2026-10-30

- Rust 1.95.0 (Cargo, Clippy, rustfmt and `wasm32-unknown-unknown`) is installed for the Windows
  user account. Trunk 0.22.1 was verified against its official release checksum and installed
  alongside Cargo. New terminals use the updated user PATH.
- The native 0.12.0 converter and .NET API build pass. C# compilation has no warnings/errors;
  the full build retains the four unused optional egui patch warnings recorded in the previous
  integration.
- All 216 .NET unit/endpoint tests, all ten HTTP integration suites and all 13 JavaScript tests
  pass. HTTP tests use disposable stores and the newly built converter, covering file formats,
  owner/version checks, administration, bookmarks, automation/jobs/tokens, templates, letters,
  typography and repeatable demo provisioning.
- WebAssembly editor and Vue production builds pass. The running Vite server remained healthy
  while Trunk rebuilt the editor.
- In-app Chromium: Persian document creation; mixed English/Persian/Urdu input; Arabic mark
  deletion retaining the base letter; acknowledged autosave/manual saves; and reopening from
  the library. The saved versioned native model retains both RTL paragraphs and their exact
  text, including the standalone Arabic base letter after Backspace.
- Windows packaging-script policy, shell syntax, both FreeBSD target dry runs and source
  whitespace checks pass. MSI compilation/installation and macOS/Linux/FreeBSD package builds
  were not exercised; WiX is unavailable.
- `cargo xtask ci` passes all eight stages: formatting, strict Clippy across all targets,
  workspace tests, localization audit, doctests, asset attribution, dependency layers and
  WebAssembly checks for all 16 supported crates. There are 4,688 passing test executions,
  zero failures and four ignored tests, including passing hosted-save regressions. Windows
  validation used `CARGO_PROFILE_TEST_DEBUG=0` to keep test binaries within linker limits.

Generated bundles, build outputs, test stores, fixture credentials and validation logs remain
ignored. No release artifacts were published.

## Previous integration on 2026-10-28

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

For future integrations, compare against the current baseline at the top of this document, map paths and identifiers
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
