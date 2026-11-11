<script setup lang="ts">
import { ref, watch } from 'vue';

const props = defineProps<{ open: boolean }>();
const emit = defineEmits<{ dismiss: [] }>();
const dialog = ref<HTMLDialogElement>();

watch([() => props.open, dialog], ([open]) => {
  if (open && dialog.value && !dialog.value.open) dialog.value.showModal();
  else if (!open && dialog.value?.open) dialog.value.close();
}, { flush: 'post' });
</script>

<template>
  <dialog ref="dialog" aria-labelledby="stale-data-title" aria-describedby="stale-data-description" @close="emit('dismiss')">
    <h2 id="stale-data-title">عدم دسترسی به سرور</h2>
    <p id="stale-data-description">ارتباط با سرور برقرار نشد. اطلاعات ذخیره‌شده بیش از ۲۴ ساعت قدمت دارد و ممکن است دقیق یا به‌روز نباشد.</p>
    <form method="dialog"><button class="button" autofocus>متوجه شدم</button></form>
  </dialog>
</template>

<style scoped>
dialog { width: min(420px, calc(100% - 40px)); border: 1px solid var(--border); border-radius: 24px; padding: 26px; color: var(--ink); background: var(--canvas); box-shadow: 0 20px 80px #102c3440; }
dialog::backdrop { background: #102c3466; backdrop-filter: blur(5px); }
p { font-size: 14px; line-height: 1.9; }
form { display: flex; justify-content: flex-end; margin-top: 16px; }
button { font: inherit; cursor: pointer; border: 0; }
</style>
