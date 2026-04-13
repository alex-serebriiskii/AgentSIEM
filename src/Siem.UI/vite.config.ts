import { defineConfig } from "vite";
import solid from "vite-plugin-solid";
import tailwindcss from "@tailwindcss/vite";
import { resolve } from "path";

export default defineConfig({
  plugins: [tailwindcss(), solid()],
  resolve: {
    alias: {
      "~": resolve(__dirname, "src"),
    },
  },
  build: {
    outDir: "dist",
    emptyOutDir: true,
  },
  server: {
    port: 3000,
    proxy: {
      "/api": {
        target: "http://localhost:5145",
        changeOrigin: true,
      },
      "/hubs": {
        target: "http://localhost:5145",
        changeOrigin: true,
        ws: true,
      },
      "/health": {
        target: "http://localhost:5145",
        changeOrigin: true,
      },
    },
  },
});
