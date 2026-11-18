import type { Router } from 'vue-router';
import { mapTripSelection } from './pageViewState';

let restoreOnBack = false;
export function restoreTripPinsOnBack() { restoreOnBack = true; }

export function installMapPinNavigation(router: Router) {
  let position = Number(router.options.history.state.position ?? 0);
  router.beforeEach((to, from) => {
    const back = Number(router.options.history.state.position ?? position) < position;
    const restore = from.name === 'new-trip' && (back || restoreOnBack);
    restoreOnBack = false;
    if (to.name === 'map' && !to.query.pick && !restore) mapTripSelection.value = { query: {} };
  });
  router.afterEach((_to, _from, failure) => {
    if (!failure) position = Number(router.options.history.state.position ?? position);
  });
}
