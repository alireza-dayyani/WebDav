import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";

const apiTarget = process.env.INKSTONE_API_URL || "http://localhost:5080";

export default defineConfig({
  plugins: [vue()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: { "/api": apiTarget },
  },
  preview: { port: 4173, proxy: { "/api": apiTarget } },
});
