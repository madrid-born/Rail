import type { Component } from 'vue';
import { createRouter, createWebHashHistory } from 'vue-router';
import { pages } from './navigation';
import type { PageName } from './navigation';
import MapPage from './pages/MapPage.vue';
import StationsPage from './pages/StationsPage.vue';
import SpecificStationPage from './pages/SpecificStationPage.vue';
import SavedPage from './pages/SavedPage.vue';
import SettingsPage from './pages/SettingsPage.vue';
import AuthPage from './pages/AuthPage.vue';

const pageComponents: Record<PageName, Component> = {
  map: MapPage,
  stations: StationsPage,
  saved: SavedPage,
  settings: SettingsPage,
};

export const router = createRouter({
  history: createWebHashHistory(),
  routes: [
    { path: '/', redirect: '/map' },
    { path: '/stations/:stationId', name: 'specific-station', component: SpecificStationPage, meta: { title: 'جزئیات ایستگاه' } },
    { path: '/login', name: 'login', component: AuthPage, meta: { title: 'ورود' } },
    { path: '/register', name: 'register', component: AuthPage, meta: { title: 'ساخت حساب' } },
    { path: '/recover', name: 'recover', component: AuthPage, meta: { title: 'بازیابی رمز عبور' } },
    ...pages.map(({ name, path, title }) => ({
      name, path, component: pageComponents[name], meta: { title },
    })),
    { path: '/:pathMatch(.*)*', redirect: '/map' },
  ],
  scrollBehavior: () => ({ top: 0 }),
});

router.afterEach(to => {
  document.title = `${to.meta.title} | Rail`;
});
