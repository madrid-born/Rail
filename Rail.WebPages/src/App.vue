<script setup lang="ts">
import { nextTick, ref, watch } from 'vue';
import { RouterView, useRoute } from 'vue-router';
import AppHeader from './components/AppHeader.vue';
import BottomNavigation from './components/BottomNavigation.vue';

const route = useRoute();
const content = ref<HTMLElement>();

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
</template>
