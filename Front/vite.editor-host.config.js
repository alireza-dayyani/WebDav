import { defineConfig } from "vite";

export default defineConfig({
  build: {
    emptyOutDir: false,
    minify: false,
    lib: {
      entry: "src/editor-host-bridge.js",
      formats: ["iife"],
      name: "InkstoneEditorHost",
      fileName: () => "editor-host.js",
    },
    outDir: "public",
    rollupOptions: {
      output: {
        banner:
          "/* Same-origin bridge between the Rust editor and the account API. No document data is kept in browser storage. Built from src/editor-host-bridge.js. */",
      },
    },
  },
});
