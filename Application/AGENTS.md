# Rust engine development

This workspace supplies the native document model, file formats, CLI converter, and WebAssembly editor.

- Keep the document engine independent of Vue and .NET. All user-visible edits go through the command registry.
- Never introduce panics, unchecked input-derived indexing, or unsafe Rust. Return errors and retain the current document on failure.
- Preserve WebAssembly builds and the crate dependency layers.
- Use original or openly licensed assets; record every bundled asset in ATTRIBUTION.md and retain its license. Fonts needed by supported languages may be bundled with their redistribution notices.
- Use chrono for calendar conversion.
- Before commits affecting Rust, run `cargo xtask ci`. Distinguish pre-existing toolchain/platform failures from regressions and run focused checks for the changed paths.
- For UI changes, inspect the rendered result when browser/editor tooling is available; state any verification limitation.

Product-layer contributor rules (secrets, licensing, tests) are in the repository root [CONTRIBUTING.md](../CONTRIBUTING.md) and [docs/DEVELOPMENT.md](../docs/DEVELOPMENT.md). The hosted workflow is documented in the root README.