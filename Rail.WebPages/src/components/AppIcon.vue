<script setup lang="ts">
import type { PageName } from '../navigation';

defineProps<{ name: PageName }>();

const paths: Record<PageName, string[]> = {
  map: ['m3 5 6-2 6 2 6-2v16l-6 2-6-2-6 2Z', 'M9 3v16M15 5v16'],
  stations: ['M9 3h6a4 4 0 0 1 4 4v7a4 4 0 0 1-4 4H9a4 4 0 0 1-4-4V7a4 4 0 0 1 4-4Z', 'M5 10h14M12 3v7M8 18l-2 3m10-3 2 3', 'M8 14h1m6 0h1'],
  saved: ['M6 21V5a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v16l-6-4Z'],
  settings: ['m9 3-.6 2.1-1.9 1.1-2.1-.5-2 3.5L4 10.8v2.4l-1.6 1.6 2 3.5 2.1-.5 1.9 1.1L9 21h4l.6-2.1 1.9-1.1 2.1.5 2-3.5-1.6-1.6v-2.4l1.6-1.6-2-3.5-2.1.5-1.9-1.1L13 3Z'],
};
</script>

<template>
  <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
    <path v-for="path in paths[name]" :key="path" :d="path" />
    <circle v-if="name === 'settings'" cx="11" cy="12" r="3" />
  </svg>
</template>
