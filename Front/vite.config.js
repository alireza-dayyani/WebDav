import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";

export default defineConfig({
  plugins: [vue()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: { "/api": "http://localhost:5080" },
  },
  preview: { port: 4173, proxy: { "/api": "http://localhost:5080" } },
});
