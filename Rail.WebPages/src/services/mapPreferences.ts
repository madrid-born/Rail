import { readonly, ref } from 'vue';
import { bridge } from './auth';

interface MapPreferences {
  hiddenLineIds: number[];
  showStationNames: boolean;
  showStationNameBoxes: boolean;
  updatedAt: number;
}

// Keep this key stable across releases; new fields can be added with defaults.
const storageKey = 'rail.map.preferences';
const defaults = (): MapPreferences => ({ hiddenLineIds: [], showStationNames: true,
  showStationNameBoxes: true, updatedAt: 0 });
const preferences = ref<MapPreferences>(defaults());
const ready = ref(false);
const storageFailed = ref(false);
const open = ref(false);
let initialization: Promise<void> | undefined;
let native: Awaited<ReturnType<typeof bridge>>;
let saves = Promise.resolve();

function parse(raw: unknown): MapPreferences | undefined {
  if (typeof raw !== 'string') return;
  try {
    const value = JSON.parse(raw);
    if (!value || typeof value !== 'object' || Array.isArray(value)) return;
    return {
      hiddenLineIds: Array.isArray(value.hiddenLineIds)
        ? [...new Set<number>(value.hiddenLineIds.filter((id: unknown) =>
          typeof id === 'number' && Number.isInteger(id) && id > 0 && id <= 255))] : [],
      showStationNames: typeof value.showStationNames === 'boolean' ? value.showStationNames : true,
      showStationNameBoxes: typeof value.showStationNameBoxes === 'boolean' ? value.showStationNameBoxes : true,
      updatedAt: typeof value.updatedAt === 'number' && Number.isFinite(value.updatedAt) && value.updatedAt >= 0
        ? value.updatedAt : 0,
    };
  } catch { return; }
}

function persist(value: MapPreferences) {
  const raw = JSON.stringify(value);
  let localSaved = false;
  try { localStorage.setItem(storageKey, raw); localSaved = true; } catch { /* Try native storage too. */ }
  // Serialize native writes so rapid toggles cannot save an older selection last.
  saves = saves.then(async () => {
    let nativeSaved = false;
    if (native) {
      try { await native.InvokeDotNet('WriteMapPreferences', [raw]); nativeSaved = true; }
      catch { /* The browser copy remains available on this origin. */ }
    }
    storageFailed.value = !localSaved && !nativeSaved;
  });
  return saves;
}

export function initializeMapPreferences(): Promise<void> {
  initialization ??= (async () => {
    let local: MapPreferences | undefined;
    try { local = parse(localStorage.getItem(storageKey)); } catch { /* Storage can be unavailable. */ }
    let device: MapPreferences | undefined;
    try {
      native = await bridge();
      if (native) device = parse(await native.InvokeDotNet('ReadMapPreferences'));
    } catch { /* Older native builds and browsers can use the local copy. */ }
    preferences.value = device && (!local || device.updatedAt >= local.updatedAt) ? device : local ?? defaults();
    if (local || device) await persist(preferences.value);
    ready.value = true;
  })();
  return initialization;
}

function update(change: Partial<MapPreferences>) {
  if (!ready.value) return Promise.resolve();
  preferences.value = { ...preferences.value, ...change,
    updatedAt: Math.max(Date.now(), preferences.value.updatedAt + 1) };
  return persist(preferences.value);
}

export function setMapLineVisible(lineId: number, visible: boolean) {
  const hidden = new Set(preferences.value.hiddenLineIds);
  if (visible) hidden.delete(lineId); else hidden.add(lineId);
  return update({ hiddenLineIds: [...hidden].sort((a, b) => a - b) });
}
export const setMapStationNames = (visible: boolean) => update({ showStationNames: visible });
export const setMapStationNameBoxes = (visible: boolean) => update({ showStationNameBoxes: visible });

export function useMapPreferences() {
  void initializeMapPreferences();
  return { preferences: readonly(preferences), ready: readonly(ready), storageFailed: readonly(storageFailed),
    open: readonly(open), show: () => { open.value = true; }, close: () => { open.value = false; } };
}
