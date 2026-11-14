<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useNetworkCache } from '../services/network';
import { setMapLineVisible, setMapStationNames, setMapStationNameBoxes, useMapPreferences } from '../services/mapPreferences';

const { open, close, preferences, ready, storageFailed } = useMapPreferences();
const { snapshot, loading } = useNetworkCache();
const lines = computed(() => [...(snapshot.value?.data.lines ?? [])].sort((a, b) => a.id - b.id));
const dialog = ref<HTMLDialogElement>();
const color = (value: string) => /^#(?:[\da-f]{3}|[\da-f]{6})$/i.test(value) ? value : '#00636a';
watch([open, dialog], ([visible]) => {
  if (visible && dialog.value && !dialog.value.open) dialog.value.showModal();
  else if (!visible && dialog.value?.open) dialog.value.close();
}, { flush: 'post' });
</script>

<template>
  <dialog ref="dialog" class="map-preferences" aria-labelledby="map-preferences-title" @close="close" @cancel="close">
    <header>
      <h2 id="map-preferences-title">تنظیمات نقشه</h2>
      <button type="button" class="close" aria-label="بستن" @click="close">
        <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m6 6 12 12M6 18 18 6" /></svg>
      </button>
    </header>
    <fieldset :disabled="!ready">
      <legend>خطوط نمایش داده‌شده</legend>
      <p v-if="!lines.length" role="status">{{ loading ? 'در حال دریافت خطوط…' : 'اطلاعات خطوط در دسترس نیست.' }}</p>
      <label v-for="line in lines" :key="line.id" class="option">
        <input type="checkbox" :checked="!preferences.hiddenLineIds.includes(line.id)"
          @change="setMapLineVisible(line.id, ($event.target as HTMLInputElement).checked)" />
        <i class="line-color" :style="{ backgroundColor: color(line.color) }" aria-hidden="true"></i>
        <span>{{ line.nameFa }}</span>
      </label>
    </fieldset>
    <fieldset class="label-options" :disabled="!ready">
      <legend class="sr-only">نام ایستگاه‌ها</legend>
      <label class="option">
        <input type="checkbox" :checked="preferences.showStationNames"
          @change="setMapStationNames(($event.target as HTMLInputElement).checked)" />
        <span>نمایش نام ایستگاه‌ها</span>
      </label>
      <label v-if="preferences.showStationNames" class="option nested">
        <input type="checkbox" :checked="preferences.showStationNameBoxes"
          @change="setMapStationNameBoxes(($event.target as HTMLInputElement).checked)" />
        <span>نمایش نام ایستگاه‌ها درون باکس</span>
      </label>
    </fieldset>
    <p v-if="storageFailed" class="save-error" role="alert">ذخیرهٔ تنظیمات روی دستگاه ممکن نشد.</p>
    <footer><button type="button" class="button" @click="close">انجام شد</button></footer>
  </dialog>
</template>

<style scoped>
.map-preferences { width: min(420px, calc(100% - 32px)); max-height: calc(100dvh - 40px); overflow-y: auto; overscroll-behavior: contain; padding: 20px 24px; border: 1px solid var(--border); border-radius: 24px; background: var(--canvas); color: var(--ink); box-shadow: 0 20px 80px #102c3440; }
.map-preferences::backdrop { background: #102c3466; backdrop-filter: blur(5px); }
header { display: flex; align-items: center; justify-content: space-between; gap: 16px; margin-bottom: 16px; }
h2 { margin: 0; }
.close { display: grid; place-items: center; width: 44px; height: 44px; border: 0; border-radius: 12px; background: var(--active); color: var(--ink); flex: none; }
fieldset { border: 0; padding: 0; margin: 0; min-width: 0; }
legend { color: var(--muted); font-size: 13px; margin-bottom: 8px; }
.option { display: flex; align-items: center; gap: 12px; min-height: 44px; cursor: pointer; }
input { width: 20px; height: 20px; margin: 0; accent-color: var(--teal); flex: none; }
.line-color { width: 10px; height: 24px; border-radius: 4px; flex: none; }
.label-options { margin-top: 16px; padding-top: 14px; border-top: 1px solid var(--border); }
.nested { padding-inline-start: 32px; font-size: 14px; }
.sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); }
footer { display: flex; justify-content: flex-end; margin-top: 12px; }
.save-error { margin-top: 12px; font-size: 13px; color: var(--ink); }
</style>
