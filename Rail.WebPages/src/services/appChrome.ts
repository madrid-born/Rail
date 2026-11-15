import type { Router } from 'vue-router';
import { hasPreviousAppPage } from './pageViewState';

declare global { interface Window { RailNative?: { back(): boolean } } }

export function installAppChrome(router: Router) {
  const prevent = (event: Event) => event.preventDefault();
  const capture = { capture: true };
  document.addEventListener('contextmenu', prevent, capture);
  document.addEventListener('dragstart', prevent);
  window.RailNative = { back() {
    const dialog = document.querySelector<HTMLDialogElement>('dialog[open]');
    if (dialog) {
      if (dialog.dispatchEvent(new Event('cancel', { cancelable: true }))) dialog.close();
      return false;
    }
    if (!window.dispatchEvent(new Event('rail:back', { cancelable: true }))) return false;
    if (router.currentRoute.value.name !== 'map') {
      if (hasPreviousAppPage(router.options.history.state.back)) router.back();
      else void router.replace('/map');
      return false;
    }
    return true;
  } };
  return () => {
    document.removeEventListener('contextmenu', prevent, capture);
    document.removeEventListener('dragstart', prevent);
    delete window.RailNative;
  };
}
