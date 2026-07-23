import { defineConfig } from "vitest/config";
import vue from "@vitejs/plugin-vue";

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue()],
  test: {
    environment: "jsdom",
    coverage: {
      provider: "v8",
      reporter: ["text", "json-summary"],
      exclude: ["src/api/**", "src/main.ts"],
    },
  },
  server: {
    proxy: {
      "/api": process.env.API_PROXY_TARGET || "http://localhost:5000",
    },
  },
});
