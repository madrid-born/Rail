<script setup lang="ts">
import { ref, watch } from 'vue';

const props = defineProps<{ message: string }>();
const dismissed = ref(false);
const offset = ref(0);
let gesture: { id: number; x: number; y: number } | undefined;

function resetGesture() { gesture = undefined; offset.value = 0; }
function dismiss() { dismissed.value = true; resetGesture(); }

function start(event: PointerEvent) {
  if (!event.isPrimary || event.button !== 0 || (event.target as Element).closest('button')) return;
  gesture = { id: event.pointerId, x: event.clientX, y: event.clientY };
  (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
}

function move(event: PointerEvent) {
  if (!gesture || event.pointerId !== gesture.id) return;
  const dx = event.clientX - gesture.x;
  const dy = event.clientY - gesture.y;
  if (Math.abs(dx) > Math.abs(dy)) offset.value = dx;
}

function end(event: PointerEvent) {
  if (event.pointerId !== gesture?.id) return;
  if (Math.abs(offset.value) >= 48) dismiss();
  else resetGesture();
}

watch(() => props.message, () => { dismissed.value = false; resetGesture(); });
</script>

<template>
  <div v-if="message && !dismissed" class="map-alert" :style="{ translate: `${offset}px` }"
    @pointerdown="start" @pointermove="move" @pointerup="end" @pointercancel="resetGesture" @lostpointercapture="resetGesture">
    <div class="map-alert-content"><span>{{ message }}</span><slot /></div>
    <button class="map-alert-dismiss" type="button" aria-label="بستن پیام" @click="dismiss">×</button>
  </div>
</template>

<style scoped>
.map-alert { display: flex; align-items: center; gap: 8px; touch-action: pan-y; }
.map-alert-content { min-width: 0; }
.map-alert-content > span { display: block; }
.map-alert-dismiss { flex: none; width: 32px; height: 32px; padding: 0; border: 0; border-radius: 8px; background: transparent; color: inherit; font-size: 21px; line-height: 1; }
.map-alert-dismiss:hover { background: var(--active); }
</style>
