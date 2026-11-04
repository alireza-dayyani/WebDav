# Current limitations

The hosted app does not yet include real-time collaborative editing, account recovery email, or
Docker deployment configuration. Revision history and restore are available through the document
automation API. Imported formats follow the Rust engine's existing fidelity limits; the 0.15.1
update improves specific DOCX (`w:altChunk`, alternate-content table rows/cells, VML rules, undefined styles), MHT, `.doc`, HTML, Markdown, LaTeX and PDF cases without establishing full
format parity. See `UPSTREAM_SYNC.md` for the integrated changes and validation boundaries.

Keep API authorization, safe saves, browser behavior, and language support covered by focused tests as these features evolve.
