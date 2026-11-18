import { defineConfig, loadEnv } from 'vite';
import { fileURLToPath } from 'node:url';
import vue from '@vitejs/plugin-vue';
import serveStatic from 'serve-static';

export default defineConfig(({ mode }) => {
  const envDir = fileURLToPath(new URL('.', import.meta.url));
  const env = loadEnv(mode, envDir);
  const apiUrl = env.VITE_API_BASE_URL?.trim();
  if (!apiUrl) throw new Error('Set VITE_API_BASE_URL in Rail.WebPages/.env.local before starting or building Rail.');
  const parsed = new URL(apiUrl);
  if (!['http:', 'https:'].includes(parsed.protocol) || parsed.search || parsed.hash || parsed.username || parsed.password)
    throw new Error('VITE_API_BASE_URL must be an HTTP(S) API base URL without credentials, a query or a fragment.');
  return {
    envDir,
    plugins: [vue(), {
      name: 'rail-local-map-assets',
      configureServer(server) {
        server.middlewares.use('/maps', serveStatic(
          fileURLToPath(new URL('../Rail.Resource/map-data/', import.meta.url)),
          { index: false, fallthrough: false, maxAge: '1h' },
        ));
      },
      configurePreviewServer(server) {
        server.middlewares.use('/maps', serveStatic(
          fileURLToPath(new URL('../Rail.Resource/map-data/', import.meta.url)),
          { index: false, fallthrough: false, maxAge: '1h' },
        ));
      },
    }],
    // MAUI serves these files from its bundled HybridWebView root.
    base: './',
    server: {
      port: 5173,
      // The MAUI development profile must always reach the same port.
      strictPort: true,
      proxy: {
        '/api': {
          target: apiUrl.replace(/\/+$/, ''),
          changeOrigin: true,
          // Accept the self-signed development certificate only on loopback.
          secure: !['localhost', '127.0.0.1', '[::1]'].includes(parsed.hostname),
        },
      },
    },
  };
});
