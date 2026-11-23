import { LngLatBounds, Marker } from 'maplibre-gl';
import type { IControl, Map as MapLibreMap } from 'maplibre-gl';
import { bridge } from './auth';
import { parseMapPoint, type MapPoint } from './journey';
import { mapBounds } from './map';

type NativeBridge = NonNullable<Awaited<ReturnType<typeof bridge>>>;
const positionOptions = { timeout: 10_000 };
const locationUnavailable = 'موقعیت شما پیدا نشد. دوباره تلاش کنید.';
const locationDisabled = 'موقعیت‌یاب (GPS) دستگاه را روشن کنید.';
const locationPermissionDenied = 'دسترسی برنامه به موقعیت دستگاه را بررسی کنید.';

async function nativePosition(native: NativeBridge): Promise<MapPoint | string | undefined> {
  const point = await native.InvokeDotNet('ReadCurrentLocation');
  if (point === 'disabled') return locationDisabled;
  if (point === 'permission-denied') return locationPermissionDenied;
  return parseMapPoint(point);
}

function browserLocationMessage(error: GeolocationPositionError) {
  if (error.code === 1) return locationPermissionDenied;
  return error.code === 2 ? locationDisabled : locationUnavailable;
}

async function currentPosition(): Promise<MapPoint | string | undefined> {
  const native = await bridge();
  if (native) return nativePosition(native);
  if (!navigator.geolocation) return;
  return new Promise(resolve => navigator.geolocation.getCurrentPosition(
    position => resolve([position.coords.longitude, position.coords.latitude]),
    error => resolve(browserLocationMessage(error)),
    positionOptions,
  ));
}

function locationInCoverage(point?: MapPoint | string): MapPoint | string {
  if (typeof point === 'string') return point;
  if (!point) return locationUnavailable;
  return LngLatBounds.convert(mapBounds).contains(point) ? point : 'موقعیت شما خارج از محدوده نقشه است.';
}

export async function readMapLocation(): Promise<MapPoint | string> {
  try {
    return locationInCoverage(await currentPosition());
  } catch {
    return locationUnavailable;
  }
}

export function createLocationControl(setMessage: (message: string) => void, requestLocationSettings: () => void): IControl {
  const container = document.createElement('div');
  container.className = 'maplibregl-ctrl maplibregl-ctrl-group';
  const button = document.createElement('button');
  button.type = 'button';
  button.className = 'maplibregl-ctrl-geolocate';
  button.title = 'موقعیت من';
  button.setAttribute('aria-label', button.title);
  const icon = document.createElement('span');
  icon.className = 'maplibregl-ctrl-icon';
  icon.setAttribute('aria-hidden', 'true');
  button.append(icon);
  container.append(button);

  const dot = document.createElement('div');
  dot.className = 'maplibregl-user-location-dot';
  dot.setAttribute('aria-hidden', 'true');
  dot.style.pointerEvents = 'none';
  const marker = new Marker({ element: dot });
  let map: MapLibreMap | undefined;
  let tracking: AbortController | undefined;
  let position: MapPoint | string = locationUnavailable;
  let showingFeedback = false;

  function locate() {
    if (!map) return;
    if (position === locationDisabled) { requestLocationSettings(); return; }
    showingFeedback = typeof position === 'string';
    if (typeof position === 'string') { setMessage(position); return; }
    map.flyTo({ center: position, zoom: Math.max(map.getZoom(), 15) });
    setMessage('');
  }

  async function track(signal: AbortSignal) {
    const update = (point?: MapPoint | string) => {
      if (signal.aborted || !map) return;
      position = locationInCoverage(point);
      if (typeof position === 'string') marker.remove();
      else {
        marker.setLngLat(position);
        if (!dot.isConnected) marker.addTo(map);
      }
      if (showingFeedback || position === locationDisabled) {
        setMessage(typeof position === 'string' ? position : '');
        showingFeedback = typeof position === 'string';
      }
    };
    try {
      const native = await bridge();
      if (signal.aborted) return;
      if (native) {
        let timer: ReturnType<typeof setTimeout> | undefined;
        signal.addEventListener('abort', () => clearTimeout(timer), { once: true });
        const refresh = async () => {
          try {
            const point = await nativePosition(native);
            update(point);
            if (point === locationPermissionDenied) return;
          }
          catch { update(); return; }
          if (!signal.aborted) timer = setTimeout(() => void refresh(), 5_000);
        };
        void refresh();
      } else if (navigator.geolocation) {
        const id = navigator.geolocation.watchPosition(
          result => update([result.coords.longitude, result.coords.latitude]),
          error => { if (error.code !== 3 || typeof position === 'string') update(browserLocationMessage(error)); }, positionOptions,
        );
        signal.addEventListener('abort', () => navigator.geolocation.clearWatch(id), { once: true });
      }
    } catch {
      update();
    }
  }

  function syncTracking() {
    if (document.hidden) {
      tracking?.abort();
      tracking = undefined;
    } else if (!tracking) {
      tracking = new AbortController();
      void track(tracking.signal);
    }
  }

  return {
    onAdd(target) {
      map = target;
      button.addEventListener('click', locate);
      document.addEventListener('visibilitychange', syncTracking);
      syncTracking();
      return container;
    },
    onRemove() {
      map = undefined;
      tracking?.abort();
      tracking = undefined;
      document.removeEventListener('visibilitychange', syncTracking);
      button.removeEventListener('click', locate);
      marker.remove();
      container.remove();
    },
  };
}
