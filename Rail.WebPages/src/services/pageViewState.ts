import { ref } from 'vue';
import type { PaddingOptions } from 'maplibre-gl';

// Keep navigation context for this app session without keeping hidden pages alive.
export interface MapView {
  center: [number, number];
  zoom: number;
  bearing: number;
  pitch: number;
  padding: PaddingOptions;
}
export let lastMapView: MapView | undefined;
export function rememberMapView(view: MapView) { lastMapView = view; }

export const stationListQuery = ref('');
export const expandedStationLines = ref(new Set<number>());
export function setStationLineOpen(lineId: number, open: boolean) {
  if (open) expandedStationLines.value.add(lineId);
  else expandedStationLines.value.delete(lineId);
}

export function hasPreviousAppPage(back: unknown): back is string {
  return typeof back === 'string' && back.startsWith('/') && !back.startsWith('//');
}
