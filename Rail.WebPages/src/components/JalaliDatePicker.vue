<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { gregorianToJalali, jalaliToGregorian, jalaliMonth } from '../services/jalaliDate';

const model = defineModel<string>({ required: true });
const input = ref<HTMLInputElement>();
const root = ref<HTMLElement>();
const toggle = ref<HTMLButtonElement>();
const text = ref('');
const open = ref(false);
const offset = ref(0);
const weekdays = ['ش', 'ی', 'د', 'س', 'چ', 'پ', 'ج'];
const today = () => new Intl.DateTimeFormat('sv-SE', { timeZone: 'Asia/Tehran', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date());
const month = computed(() => jalaliMonth(model.value || today(), offset.value));
const faDigits = (value: string) => value.replace(/\d/g, digit => '۰۱۲۳۴۵۶۷۸۹'[Number(digit)]!);
watch(model, value => { text.value = faDigits(gregorianToJalali(value)); offset.value = 0; input.value?.setCustomValidity(''); }, { immediate: true });
function update(value: string) {
  text.value = value;
  const converted = jalaliToGregorian(value);
  const valid = converted && converted >= '2000-01-01' && converted <= '2100-12-31';
  input.value?.setCustomValidity(valid ? '' : 'تاریخ شمسی معتبر وارد کنید، مانند ۱۴۰۵/۰۸/۲۳.');
  if (valid) model.value = converted;
}
function select(value: string) {
  model.value = value; text.value = faDigits(gregorianToJalali(value));
  input.value?.setCustomValidity(''); open.value = false; offset.value = 0; toggle.value?.focus();
}
function closeOutside(event: PointerEvent) { if (!root.value?.contains(event.target as Node)) open.value = false; }
function close() { open.value = false; toggle.value?.focus(); }
onMounted(() => document.addEventListener('pointerdown', closeOutside));
onBeforeUnmount(() => document.removeEventListener('pointerdown', closeOutside));
</script>

<template>
  <div ref="root" class="jalali-picker" @keydown.esc.stop.prevent="close">
    <div class="date-input">
      <input ref="input" :value="text" type="text" inputmode="numeric" aria-label="تاریخ شمسی" required
        placeholder="۱۴۰۵/۰۸/۲۳" autocomplete="off" @input="update(($event.target as HTMLInputElement).value)" />
      <button ref="toggle" type="button" aria-label="باز کردن تقویم شمسی" :aria-expanded="open" aria-haspopup="dialog"
        @click="open = !open; offset = 0">
        <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M7 3v4m10-4v4M4 10h16M5 5h14a1 1 0 0 1 1 1v14H4V6a1 1 0 0 1 1-1Z" /></svg>
      </button>
    </div>
    <section v-if="open" class="calendar" role="dialog" aria-label="تقویم هجری شمسی">
      <div class="calendar-heading">
        <button type="button" aria-label="ماه قبل" :disabled="month.first <= '2000-01-01'" @click="offset--">‹</button>
        <strong aria-live="polite">{{ month.label }}</strong>
        <button type="button" aria-label="ماه بعد" :disabled="month.days[month.days.length - 1]!.date >= '2100-12-31'" @click="offset++">›</button>
      </div>
      <div class="calendar-grid">
        <span v-for="(day, index) in weekdays" :key="index" aria-hidden="true">{{ day }}</span>
        <span v-for="blank in month.weekday" :key="`blank-${blank}`" aria-hidden="true" />
        <button v-for="day in month.days" :key="day.date" type="button" :class="{ selected: day.date === model }"
          :disabled="day.date < '2000-01-01' || day.date > '2100-12-31'" :aria-label="faDigits(gregorianToJalali(day.date))"
          :aria-pressed="day.date === model" @click="select(day.date)">{{ day.day.toLocaleString('fa-IR') }}</button>
      </div>
      <button type="button" class="text-button today" @click="select(today())">امروز</button>
    </section>
  </div>
</template>

<style scoped>
.jalali-picker { position: relative; min-width: 0; }
.date-input { display: flex; align-items: center; min-height: 46px; border: 1px solid var(--border); border-radius: 12px; background: var(--canvas); }
.date-input input { width: 100%; min-width: 0; padding: 10px 6px; border: 0; background: transparent; color: var(--ink); direction: ltr; text-align: center; font-size: 14px; }
.date-input > button { display: grid; place-items: center; flex: 0 0 36px; height: 42px; border: 0; background: transparent; color: var(--teal); }
.date-input svg { width: 20px; height: 20px; }
.calendar { position: absolute; z-index: 15; top: calc(100% + 8px); inset-inline-start: 0; width: min(290px, calc(100vw - 48px)); padding: 12px; border: 1px solid var(--border); border-radius: 16px; background: var(--canvas); box-shadow: 0 8px 30px #102c3430; }
.calendar-heading { display: flex; align-items: center; justify-content: space-between; gap: 8px; margin-bottom: 8px; }
.calendar-heading button { width: 36px; min-height: 36px; border: 0; border-radius: 8px; background: var(--active); color: var(--ink); font-size: 24px; }
.calendar-grid { display: grid; grid-template-columns: repeat(7, 1fr); gap: 2px; text-align: center; }
.calendar-grid > span { font-size: 12px; color: var(--muted); }
.calendar-grid button { min-height: 36px; border: 0; border-radius: 8px; background: transparent; color: var(--ink); }
.calendar-grid button:hover { background: var(--active); }
.calendar-grid button.selected { background: var(--teal); color: var(--canvas); }
.today { display: block; margin-inline: auto; text-align: center; }
</style>
