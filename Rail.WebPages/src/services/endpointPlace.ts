import { computed, ref, watch } from 'vue';
import type { ComputedRef } from 'vue';
import type { MapPoint } from './journey';
import { findLocalPlace } from './localPlace';

const titles = new Map<string, string>();

export function useEndpointPlace(point: ComputedRef<MapPoint | undefined>, fallback: ComputedRef<string>) {
  const resolved = ref('');
  watch(() => point.value?.join(','), async (key, _, onCleanup) => {
    resolved.value = key ? titles.get(key) ?? '' : '';
    if (!key || resolved.value || !point.value) return;
    const request = new AbortController();
    const timeout = setTimeout(() => request.abort(), 10_000);
    onCleanup(() => { request.abort(); clearTimeout(timeout); });
    try {
      const title = await findLocalPlace(point.value, request.signal);
      if (!request.signal.aborted && title) {
        resolved.value = title;
        titles.set(key, resolved.value);
      }
    } catch { /* Keep the nearby metro title when the local map has no suitable label. */ }
    finally { clearTimeout(timeout); }
  }, { immediate: true });
  return computed(() => resolved.value || fallback.value);
}
