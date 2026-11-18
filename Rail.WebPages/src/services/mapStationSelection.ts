import type { Map as MapLibreMap } from 'maplibre-gl';

export function installMapStationSelection(map: MapLibreMap, onSelect: (id: string) => void) {
  let pending: ReturnType<typeof setTimeout> | undefined;
  const cancel = () => { clearTimeout(pending); pending = undefined; };
  const events = ['dblclick', 'dragstart', 'zoomstart', 'rotatestart', 'mousedown', 'touchstart'] as const;
  for (const event of events) map.on(event, cancel);
  return {
    select(id: string) {
      cancel();
      // Match MapLibre's 500 ms double-tap window before opening a station.
      pending = setTimeout(() => { pending = undefined; onSelect(id); }, 500);
    },
    cancel,
    dispose() { cancel(); for (const event of events) map.off(event, cancel); },
  };
}
