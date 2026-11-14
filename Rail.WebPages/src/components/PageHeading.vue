<script setup lang="ts">
import { inject, onBeforeUnmount, watch } from 'vue';
import { pageHeadingKey } from '../composables/pageHeading';

const props = defineProps<{ title: string; eyebrow?: string; description: string; color?: string }>();
const heading = inject(pageHeadingKey, undefined);
const owner = Symbol('heading-owner');
watch([() => props.title, () => props.color], ([title, color]) => {
  if (heading) heading.value = { owner, title, color };
}, { immediate: true });
onBeforeUnmount(() => {
  if (heading?.value?.owner === owner) heading.value = undefined;
});
</script>

<template>
  <div class="page-heading">
    <span v-if="eyebrow" class="eyebrow">{{ eyebrow }}</span>
    <h1>{{ title }}</h1>
    <p>{{ description }}</p>
  </div>
</template>
