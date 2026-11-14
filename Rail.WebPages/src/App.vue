<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { RouterView, useRoute } from 'vue-router';
import AppHeader from './components/AppHeader.vue';
import BottomNavigation from './components/BottomNavigation.vue';
import StaleDataDialog from './components/StaleDataDialog.vue';
import { checkNetworkCache, useNetworkCache } from './services/network';
import { initializeTimetableCache, onTimetableVisibilityChange } from './services/timetable';
import { initializeAuth, resumeAuth } from './services/auth';

const route = useRoute();
const content = ref<HTMLElement>();
const { staleWarning, dismissWarning } = useNetworkCache();
let authTimer: ReturnType<typeof setInterval> | undefined;

function onVisibilityChange() {
  if (route.name === 'specific-station') return;
  onTimetableVisibilityChange();
  if (document.visibilityState === 'visible') void checkNetworkCache();
  if (document.visibilityState === 'visible') void resumeAuth();
}

onMounted(() => {
  initializeForRoute();
  authTimer = setInterval(() => {
    if (document.visibilityState === 'visible' && route.name !== 'specific-station') void resumeAuth();
  }, 10 * 60_000);
  document.addEventListener('visibilitychange', onVisibilityChange);
});
function initializeForRoute() {
  // Station details bootstrap themselves with one aggregate response, including
  // cold deep links. Defer unrelated network, timetable and account requests.
  if (route.name === 'specific-station') return;
  void initializeAuth();
  void checkNetworkCache();
  initializeTimetableCache();
}
watch(() => route.name, initializeForRoute);
onBeforeUnmount(() => {
  document.removeEventListener('visibilitychange', onVisibilityChange);
  clearInterval(authTimer);
});

watch(() => route.fullPath, async () => {
  await nextTick();
  content.value?.focus({ preventScroll: true });
});
</script>

<template>
  <AppHeader />
  <main ref="content" tabindex="-1">
    <RouterView />
  </main>
  <BottomNavigation />
  <StaleDataDialog :open="staleWarning" @dismiss="dismissWarning" />
</template>
