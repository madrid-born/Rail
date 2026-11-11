export const apiBase = (import.meta.env.VITE_API_BASE_URL
  ?? (import.meta.env.DEV ? '' : 'https://localhost:7130')).replace(/\/+$/, '');
