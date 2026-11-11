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
    proxy: {
      '/api': {
        target: 'https://localhost:7130',
        changeOrigin: true,
        // Only the local development proxy accepts the API's self-signed certificate.
        secure: false,
      },
    },
  },
});
