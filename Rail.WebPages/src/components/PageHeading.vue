<script setup lang="ts">
defineProps<{ title: string; eyebrow?: string; description: string }>();
</script>

<template>
  <div class="page-heading">
    <span v-if="eyebrow" class="eyebrow">{{ eyebrow }}</span>
    <h1>{{ title }}</h1>
    <p>{{ description }}</p>
  </div>
</template>
