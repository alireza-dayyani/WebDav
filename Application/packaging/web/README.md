# Browser editor packaging

The hosted application has three separate projects: Vue (`Front`), .NET 10 (`Back`),
and this Rust workspace (`Application`). Follow the root `README.md` two directories
above this workspace for setup and hosting details.

Run `npm run editor:build` in `Front`. Trunk builds the `inkstone-web` binary using
the optimized `web` Cargo profile and writes its WebAssembly, loader and HTML to
`Front/public/editor/`. Vue serves that folder and `editor-host.js` on one origin.
The editor requires an authenticated account and a document ID from the Vue library.

Serve `.wasm` as `application/wasm`, `.js` as JavaScript, and use HTTPS when hosted.
WebGPU falls back to WebGL2; `?webgl` can force the latter. Account storage and
credentials belong to the .NET API and must never be included in static assets.

The inherited shell packaging scripts are for the standalone desktop distribution;
the hosted browser app is packaged through `Front/package.json`.
