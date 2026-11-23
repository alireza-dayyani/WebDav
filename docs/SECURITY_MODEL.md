# Editor embedding security model

The Vue workspace loads the Rust WASM editor in a **same-origin** iframe (`/editor/index.html`) and talks to it through `editor-host-bridge.js` (`postMessage` + scoped `fetch` to `/api`).

## Parent shell (workspace)

- **Origin checks:** `App.vue` and `LetterEditor.vue` ignore `message` events unless `event.origin === location.origin` and `event.source` is the iframe's `contentWindow`.
- **Bridge:** `editor-host-bridge.js` posts to `location.origin` only and accepts `papyrus:save-request` from `window.parent` on the same origin.
- **CSP:** ASP.NET sets `Content-Security-Policy` on every response (`Back/Program.cs`), including `default-src 'self'`, `wasm-unsafe-eval` for the editor bundle, `worker-src 'self' blob:`, and `frame-ancestors 'self'`.

## Editor iframe

- **`sandbox`:** `Front/src/editorEmbed.js` defines the shared allowlist used by both editors:
  - `allow-scripts` — WASM editor
  - `allow-same-origin` — `localStorage`, cookies, API calls from the bridge
  - `allow-downloads` — Save / Export
  - `allow-popups` / `allow-modals` — print and export flows
- **`allow` attribute:** `clipboard-read`, `clipboard-write`, and `fullscreen` stay on the iframe element (Permissions Policy), not in `sandbox`.

Third-party embedders should follow [Application/packaging/web/README.md](../Application/packaging/web/README.md) and must not send `X-Frame-Options: DENY` or a `frame-ancestors` policy that blocks the parent page.

Verification: `node --test Tests/editor-embed.test.mjs`, Playwright (`npm run test:browser --prefix Front`), and `Tests/file_api_smoke.py` (CSP headers).