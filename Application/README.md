# Inkstone document engine

Rust workspace for the document model, editing commands, layout, supported file formats, desktop tools, and browser editor.

The Vue frontend and .NET API live alongside this directory. See the root README for setup and usage.

Build the browser editor from Front with `npm run editor:build`. Its generated files are served by Vue. Native conversion tools can be built with `cargo build -p inkstone-cli`.

Template automation uses the same command registry as editor operations. `document.bookmarks` lists bookmarks across the body and secondary stories, with range validation. `document.fillBookmarks` accepts `{"values":{"BookmarkName":"literal text"}}`, replaces a validated batch atomically, and retains markers for reuse. It is undoable in an editor session and available through `inkstone-cli run --file template.docx --script job.json --save filled.docx`. The script's `commands` list must contain a `document.fillBookmarks` command with those parameters. The .NET API exposes this workflow by owned document ID; see Back/README.md for request examples, supported ranges and limits.

The workspace licenses and original attribution records remain in LICENSE-MIT, LICENSE-APACHE, NOTICE, ATTRIBUTION.md, and legal.

Upstream engine changes are integrated through Wordcraft `3b7f7ed1` (workspace version 0.12.0).
See [upstream integration](docs/UPSTREAM_SYNC.md) for the source revision, included changes and fork-specific adaptations.
