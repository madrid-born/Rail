<script setup lang="ts">
import { RouterLink } from 'vue-router';
import appLogo from '../assets/rail-app-icon.svg';
</script>

<template>
  <header class="app-header">
    <RouterLink class="brand" to="/map" aria-label="Rail، نقشه">
      <img class="brand-logo" :src="appLogo" alt="" width="42" height="42" />Rail
    </RouterLink>
  </header>
</template>

<style scoped>
.app-header { direction: ltr; justify-content: flex-start; }
.brand-logo { display: block; flex: none; }
</style>
