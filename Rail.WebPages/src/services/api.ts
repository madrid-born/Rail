// Vite validates the required address at startup/build time. Browser development
// uses its same-origin proxy; bundled builds use that same configured address.
export const apiBase = import.meta.env.DEV ? '' : import.meta.env.VITE_API_BASE_URL.trim().replace(/\/+$/, '');
