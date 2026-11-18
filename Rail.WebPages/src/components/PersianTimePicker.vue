<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue';

const model = defineModel<string>({ required: true });
const root = ref<HTMLElement>();
const toggle = ref<HTMLButtonElement>();
const open = ref(false);
const hour = ref('00');
const minute = ref('00');
const faDigits = (value: string) => value.replace(/\d/g, digit => '۰۱۲۳۴۵۶۷۸۹'[Number(digit)]!);
const label = computed(() => faDigits(model.value));
const hours = Array.from({ length: 24 }, (_, index) => String(index).padStart(2, '0'));
const minutes = Array.from({ length: 60 }, (_, index) => String(index).padStart(2, '0'));
function show() {
  if (open.value) { close(); return; }
  const parts = model.value.split(':');
  hour.value = hours.includes(parts[0]!) ? parts[0]! : '00';
  minute.value = minutes.includes(parts[1]!) ? parts[1]! : '00';
  open.value = true;
}
function close() { open.value = false; toggle.value?.focus(); }
function apply() { model.value = `${hour.value}:${minute.value}`; close(); }
function closeOutside(event: PointerEvent) { if (!root.value?.contains(event.target as Node)) open.value = false; }
onMounted(() => document.addEventListener('pointerdown', closeOutside));
onBeforeUnmount(() => document.removeEventListener('pointerdown', closeOutside));
</script>

<template>
  <div ref="root" class="time-picker" @keydown.esc.stop.prevent="close">
    <button ref="toggle" type="button" class="time-input" :aria-label="`انتخاب ساعت، ${label}`"
      :aria-expanded="open" aria-haspopup="dialog" @click="show">
      <span dir="ltr">{{ label }}</span>
      <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="8" /><path d="M12 7v5l3 2" /></svg>
    </button>
    <section v-if="open" class="time-panel" role="dialog" aria-label="انتخاب ساعت و دقیقه">
      <div class="time-fields">
        <label>ساعت<select v-model="hour" aria-label="ساعت"><option v-for="value in hours" :key="value" :value="value">{{ faDigits(value) }}</option></select></label>
        <span aria-hidden="true">:</span>
        <label>دقیقه<select v-model="minute" aria-label="دقیقه"><option v-for="value in minutes" :key="value" :value="value">{{ faDigits(value) }}</option></select></label>
      </div>
      <button type="button" class="button time-apply" @click="apply">تأیید ساعت</button>
    </section>
  </div>
</template>

<style scoped>
.time-picker { position: relative; min-width: 0; }
.time-input { display: flex; align-items: center; justify-content: space-between; gap: 8px; width: 100%; min-height: 46px; padding: 10px 12px; border: 1px solid var(--border); border-radius: 12px; background: var(--canvas); color: var(--ink); cursor: pointer; }
.time-input:hover, .time-input[aria-expanded="true"] { border-color: var(--teal); }
.time-input:focus-visible { outline: 2px solid var(--teal); outline-offset: 2px; }
.time-input > span { flex: 1; font-size: 14px; text-align: center; font-variant-numeric: tabular-nums; }
.time-input svg { width: 20px; height: 20px; fill: none; stroke: var(--teal); stroke-width: 1.8; }
.time-panel { position: absolute; z-index: 15; top: calc(100% + 8px); inset-inline-end: 0; width: min(240px, calc(100vw - 48px)); padding: 14px; border: 1px solid var(--border); border-radius: 16px; background: var(--canvas); box-shadow: 0 8px 30px #102c3430; }
.time-fields { display: flex; direction: ltr; align-items: end; gap: 8px; }
.time-fields label { display: grid; flex: 1; gap: 6px; font-size: 13px; text-align: center; }
.time-fields select { width: 100%; min-height: 44px; padding: 8px; border: 1px solid var(--border); border-radius: 10px; background: var(--canvas); color: var(--ink); font-size: 18px; text-align: center; }
.time-fields > span { padding-bottom: 10px; }
.time-apply { width: 100%; margin-top: 14px; }
</style>
