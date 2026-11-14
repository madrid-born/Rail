<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue';
import { RouterLink, useRoute } from 'vue-router';
import appLogo from '../assets/rail-app-icon.svg';
defineProps<{ pageTitle: string; pageColor?: string }>();
const route = useRoute();
const header = ref<HTMLElement>();
const scrolledPast = ref(false);
let observer: IntersectionObserver | undefined;
onMounted(() => {
  observer = new IntersectionObserver(([entry]) => {
    if (entry) scrolledPast.value = !entry.isIntersecting && entry.boundingClientRect.bottom <= 0;
  });
  if (header.value) observer.observe(header.value);
});
onBeforeUnmount(() => observer?.disconnect());
</script>

<template>
  <header ref="header" class="app-header" :inert="scrolledPast" :aria-hidden="scrolledPast">
    <RouterLink class="brand" to="/map" aria-label="Rail، نقشه">
      <img class="brand-logo" :src="appLogo" alt="" width="42" height="42" />Rail
    </RouterLink>
    <RouterLink v-if="route.name === 'specific-station'" class="stations-back" to="/stations" aria-label="بازگشت به ایستگاه‌ها" title="بازگشت به ایستگاه‌ها">
      <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12h14m-7-7 7 7-7 7" /></svg>
    </RouterLink>
  </header>
  <Transition name="compact-header">
    <header v-if="scrolledPast" class="compact-header" aria-label="عنوان صفحه">
      <div class="compact-inner">
        <RouterLink class="brand" to="/map" aria-label="Rail، نقشه">
          <img class="brand-logo" :src="appLogo" alt="" width="32" height="32" />Rail
        </RouterLink>
        <strong class="compact-title" dir="rtl" :title="pageTitle">
          <span v-if="route.name === 'specific-station'" class="station-title" :style="{ '--station-color': pageColor ?? 'var(--teal)' }">{{ pageTitle }}</span>
          <template v-else>{{ pageTitle }}</template>
        </strong>
        <RouterLink v-if="route.name === 'specific-station'" class="stations-back" to="/stations" aria-label="بازگشت به ایستگاه‌ها" title="بازگشت به ایستگاه‌ها">
          <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12h14m-7-7 7 7-7 7" /></svg>
        </RouterLink>
      </div>
    </header>
  </Transition>
</template>

<style scoped>
.app-header { direction: ltr; justify-content: flex-start; }
.brand-logo { display: block; flex: none; }
.stations-back { margin-left: auto; display: grid; place-items: center; width: 44px; height: 44px; border-radius: 12px; color: var(--ink); }
.stations-back:hover { background: var(--active); }
.compact-header { position: fixed; top: 0; inset-inline: 0; z-index: 20; background: var(--canvas); border-bottom: 1px solid var(--border); box-shadow: 0 4px 20px #00000012; }
.compact-inner { direction: ltr; display: flex; align-items: center; gap: 14px; width: min(100%, 1120px); margin-inline: auto; padding: calc(8px + env(safe-area-inset-top, 0px)) 32px 8px; min-height: 60px; }
.compact-header .brand { font-size: 22px; gap: 8px; flex: none; }
.compact-title { min-width: 0; flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; text-align: right; font-size: 16px; }
.station-title { display: inline-block; max-width: 100%; padding: 4px 12px; border: 2px solid var(--station-color); border-radius: 10px; color: var(--ink); vertical-align: middle; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.compact-header .stations-back { margin-left: 0; flex: none; }
.compact-header-enter-active, .compact-header-leave-active { transition: opacity .15s ease, transform .15s ease; }
.compact-header-enter-from, .compact-header-leave-to { opacity: 0; transform: translateY(-8px); }
@media (max-width: 600px) { .compact-inner { padding-inline: 20px; gap: 10px; } }
@media (prefers-reduced-motion: reduce) { .compact-header-enter-active, .compact-header-leave-active { transition: none; } }
</style>
