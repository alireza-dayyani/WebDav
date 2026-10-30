# Architecture

Vue owns the account screens and document library. The Rust editor runs in an iframe as WebAssembly and exchanges document models with a same-origin JavaScript bridge. The .NET API owns authentication, authorization, document persistence, and version checks.

The API stores complete native document models in SQLite outside the static web root. Every document query is scoped to its owner. Browser downloads and server exports use the Rust format implementation.

The Rust workspace retains separate model, font, layout, format, rendering, engine, and UI crates. `cargo xtask layers` checks those boundaries. The CLI is the native integration boundary for server-side conversion; server deployments do not require Vue or a browser for file operations.

The 0.12.0 engine update adds the formats crate's dependency on the metafile decoder for RTF
WMF/EMF pictures. Custom-property bookmark associations and run-level grid/web visibility flags
remain part of the native model and DOCX round trips. These changes do not move authentication
or persistence into the editor; hosted saves still wait for the API's versioned acknowledgement.
