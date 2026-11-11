<script setup lang="ts">
import type { PageName } from '../navigation';
import AppIcon from './AppIcon.vue';

defineProps<{ icon: PageName; title: string; description: string }>();
</script>

<template>
  <section class="card empty-state" :aria-label="title">
    <span class="empty-icon"><AppIcon :name="icon" /></span>
    <h2>{{ title }}</h2>
    <p>{{ description }}</p>
    <slot />
  </section>
</template>
