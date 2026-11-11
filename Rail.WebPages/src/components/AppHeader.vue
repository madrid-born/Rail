<script setup lang="ts">
import { RouterLink } from 'vue-router';
import AppIcon from './AppIcon.vue';
</script>

<template>
  <header class="app-header">
    <RouterLink class="brand" to="/map" aria-label="Rail، نقشه">
      <span class="brand-mark"><AppIcon name="stations" /></span>Rail
    </RouterLink>
    <small>همراه متروی تهران</small>
  </header>
</template>
