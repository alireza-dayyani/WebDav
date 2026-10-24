# Inkstone development

Inkstone has three separate projects: `Front` (Vue), `Back` (.NET 10 API), and `Application` (Rust document engine and editor). Keep the API usable without the frontend.

- Read the root README and relevant project instructions before editing.
- Keep documents private to the authenticated owner. Apply authorization and version checks to every file operation.
- Never commit databases, user documents, data-protection keys, generated editor bundles, build outputs, or credentials.
- Preserve required licenses, original copyright notices, and asset attribution. Keep product UI and current documentation free of inherited promotional material.
- Use original or openly licensed assets and record their source and license in attribution files.
- Verify changes with relevant builds, API integration tests, and browser tests when available. Run the Rust workspace checks before commits affecting the engine; report pre-existing failures separately.
- Do not publish or push changes unless requested.
