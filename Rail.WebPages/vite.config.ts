import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';

export default defineConfig({
  plugins: [vue()],
  // MAUI serves these files from its bundled HybridWebView root.
  base: './',
  server: {
    port: 5173,
    // The MAUI development profile must always reach the same port.
    strictPort: true,
  },
});
