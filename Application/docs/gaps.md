# Current limitations

The hosted app does not yet include sharing, collaborative editing, account recovery email, or
deployment configuration. Revision history and restore are available through the document
automation API. Imported formats follow the Rust engine's existing fidelity limits; the 0.12.0
update improves specific DOCX, RTF, HTML, Markdown, LaTeX and PDF cases without establishing full
format parity. See `UPSTREAM_SYNC.md` for the integrated changes and validation boundaries.

Keep API authorization, safe saves, browser behavior, and language support covered by focused tests as these features evolve.
