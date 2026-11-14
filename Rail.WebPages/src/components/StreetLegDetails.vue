<script setup lang="ts">
import type { StreetPath } from '../services/journey';
defineProps<{ path: StreetPath }>();
</script>

<template>
  <details v-if="path.steps.length" class="street-directions">
    <summary>راهنمای مسیر خیابانی</summary>
    <ol><li v-for="(step, index) in path.steps" :key="index">
      {{ step.instruction }}
      <small>{{ Math.round(step.distanceMeters).toLocaleString('fa-IR') }} متر · {{ step.seconds.toLocaleString('fa-IR') }} ثانیه</small>
    </li></ol>
  </details>
</template>

<style scoped>
.street-directions { margin-block: 8px; font-size: 13px; }
summary { color: var(--teal); cursor: pointer; min-height: 36px; padding-block: 4px; }
ol { padding-inline-start: 22px; }
li { margin-bottom: 10px; }
small { display: block; color: var(--muted); }
</style>
