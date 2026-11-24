<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useNetworkCache } from '../services/network';
import { isMapLineVisible, isUnbuiltMapLine, setMapLineVisible, setMapStationNames, setMapStationNameBoxes, setMapUnbuiltStations, useMapPreferences } from '../services/mapPreferences';

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
      <h2 id="map-preferences-title">شخصی سازی برنامه</h2>
      <button type="button" class="close" aria-label="بستن" @click="close">
        <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m6 6 12 12M6 18 18 6" /></svg>
      </button>
    </header>
    <div class="preferences-content">
      <fieldset :disabled="!ready">
        <legend>خطوط نمایش داده‌شده</legend>
        <label class="option">
          <input type="checkbox" :checked="preferences.showUnbuiltStations"
            @change="setMapUnbuiltStations(($event.target as HTMLInputElement).checked)" />
          <span>نمایش ایستگاه های ساخته نشده</span>
        </label>
        <p v-if="!lines.length" role="status">{{ loading ? 'در حال دریافت خطوط…' : 'اطلاعات خطوط در دسترس نیست.' }}</p>
        <div v-else class="line-options">
          <label v-for="line in lines" :key="line.id" class="option line-option" :class="{ unavailable: isUnbuiltMapLine(line) && !preferences.showUnbuiltStations }">
            <input type="checkbox" :checked="isMapLineVisible(line, preferences)" :disabled="isUnbuiltMapLine(line) && !preferences.showUnbuiltStations"
              @change="setMapLineVisible(line.id, ($event.target as HTMLInputElement).checked)" />
            <i class="line-color" :style="{ backgroundColor: color(line.color) }" aria-hidden="true"></i>
            <span class="line-name">
              {{ line.nameFa }}
              <small v-if="line.infrastructureStatus === 'under_construction'">در حال ساخت</small>
              <small v-else-if="line.infrastructureStatus === 'planned'">برنامه‌ریزی‌شده</small>
            </span>
          </label>
        </div>
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
    </div>
    <footer>
      <p v-if="storageFailed" class="save-error" role="alert">ذخیرهٔ تنظیمات روی دستگاه ممکن نشد.</p>
      <button type="button" class="button" @click="close">انجام شد</button>
    </footer>
  </dialog>
</template>

<style scoped>
.map-preferences { width: min(420px, calc(100% - 32px)); max-height: calc(100dvh - 32px); overflow: hidden; padding: 0; border: 1px solid var(--border); border-radius: 24px; background: var(--canvas); color: var(--ink); box-shadow: 0 20px 80px #102c3440; }
.map-preferences[open] { display: grid; grid-template-rows: auto minmax(0, 1fr) auto; }
.map-preferences::backdrop { background: #102c3466; backdrop-filter: blur(5px); }
header, .preferences-content, footer { padding-inline: clamp(16px, 5vw, 24px); }
header { display: flex; align-items: center; justify-content: space-between; gap: 16px; padding-block: 16px 12px; }
h2 { margin: 0; }
.close { display: grid; place-items: center; width: 44px; height: 44px; border: 0; border-radius: 12px; background: var(--active); color: var(--ink); flex: none; }
.preferences-content { overflow-y: auto; overscroll-behavior: contain; padding-block: 4px 12px; }
fieldset { border: 0; padding: 0; margin: 0; min-width: 0; }
legend { color: var(--muted); font-size: 13px; margin-bottom: 4px; }
.option { display: flex; align-items: center; gap: 12px; min-height: 44px; cursor: pointer; }
.option.unavailable { opacity: 0.55; cursor: default; }
input { width: 20px; height: 20px; margin: 0; accent-color: var(--teal); flex: none; }
.line-options { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 104px), 1fr)); column-gap: 12px; }
.line-option { gap: 8px; }
.line-color { width: 10px; height: 24px; border-radius: 4px; flex: none; }
.line-name { min-width: 0; line-height: 1.2; overflow-wrap: anywhere; }
.line-name small { display: block; color: var(--muted); font-size: 12px; margin-top: 2px; }
.label-options { margin-top: 8px; padding-top: 8px; border-top: 1px solid var(--border); }
.nested { padding-inline-start: 32px; font-size: 14px; }
.sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); }
footer { display: grid; gap: 8px; padding-block: 12px 16px; border-top: 1px solid var(--border); }
footer .button { margin: 0; font-family: inherit; font-size: inherit; cursor: pointer; }
.save-error { margin: 0; font-size: 13px; color: var(--ink); }
</style>
