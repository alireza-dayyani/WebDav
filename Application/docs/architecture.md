# Architecture

Vue owns the account screens and document library. The Rust editor runs in an iframe as WebAssembly and exchanges document models with a same-origin JavaScript bridge. The .NET API owns authentication, authorization, document persistence, and version checks.

The API stores complete native document models in SQLite outside the static web root. Every document query is scoped to its owner. Browser downloads and server exports use the Rust format implementation.

The Rust workspace retains separate model, font, layout, format, rendering, engine, and UI crates. `cargo xtask layers` checks those boundaries. The CLI is the native integration boundary for server-side conversion; server deployments do not require Vue or a browser for file operations.
