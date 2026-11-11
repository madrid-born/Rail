import { ref, watch } from 'vue';

type Theme = 'system' | 'light' | 'dark';

function readTheme(): Theme {
  try {
    const stored = localStorage.getItem('rail.theme');
    if (stored === 'light' || stored === 'dark') return stored;
  } catch { /* Appearance still works when storage is unavailable. */ }
  return 'system';
}

const theme = ref<Theme>(readTheme());

export function initializeTheme() {
  watch(theme, value => {
    document.documentElement.dataset.theme = value;
    try { localStorage.setItem('rail.theme', value); } catch { /* Keep the in-memory preference. */ }
  }, { immediate: true, flush: 'sync' });
}

export function useTheme() {
  return { theme };
}
