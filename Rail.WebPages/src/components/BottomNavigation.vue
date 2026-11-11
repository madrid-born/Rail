<script setup lang="ts">
import { RouterLink } from 'vue-router';
import { pages } from '../navigation';
import AppIcon from './AppIcon.vue';
</script>

<template>
  <nav class="tab-bar" aria-label="صفحه‌های اصلی">
    <RouterLink v-for="page in pages" :key="page.name" :to="page.path">
      <AppIcon :name="page.icon" />
      <span>{{ page.title }}</span>
    </RouterLink>
  </nav>
</template>
