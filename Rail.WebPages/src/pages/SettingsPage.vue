<script setup lang="ts">
import PageHeading from '../components/PageHeading.vue';
import { useTheme } from '../composables/useTheme';
import { onBeforeUnmount, ref } from 'vue';
import { RouterLink } from 'vue-router';
import { googleLogin, useAuth } from '../services/auth';
import { useMapPreferences } from '../services/mapPreferences';

const { theme } = useTheme();
const { show: showMapPreferences } = useMapPreferences();
const { user, ready, logout } = useAuth();
const busy = ref(false);
const message = ref('');
let controller: AbortController | undefined;
onBeforeUnmount(() => controller?.abort());
async function signOut() {
  busy.value = true; message.value = '';
  try { await logout(); }
  catch { message.value = 'حذف نشست ذخیره‌شده انجام نشد. دوباره تلاش کنید.'; }
  finally { busy.value = false; }
}
async function linkGoogle() {
  busy.value = true; message.value = '';
  controller = new AbortController();
  try { await googleLogin(controller.signal, () => ({}), true); message.value = 'حساب گوگل متصل شد.'; }
  catch (cause) {
    if (!(cause instanceof DOMException && cause.name === 'AbortError'))
      message.value = cause instanceof Error ? cause.message : 'اتصال حساب انجام نشد.';
  } finally { busy.value = false; controller = undefined; }
}
</script>

<template>
  <PageHeading title="تنظیمات" eyebrow="Rail، به سلیقهٔ شما" description="حساب کاربری و ظاهر برنامه را مدیریت کنید." />
  <section class="card account-card" aria-labelledby="account-title">
    <h2 id="account-title">حساب کاربری</h2>
    <p v-if="!ready" role="status">در حال بررسی حساب…</p>
    <template v-else-if="user">
      <div class="account-profile">
        <span class="account-avatar" aria-hidden="true">{{ (user.displayName || user.userName || user.email).slice(0, 1).toUpperCase() }}</span>
        <div><strong dir="auto">{{ user.displayName || user.userName || user.email }}</strong><p dir="ltr">{{ user.email }}</p></div>
      </div>
      <div class="account-actions">
        <button class="button google-button" :disabled="busy" @click="linkGoogle">اتصال حساب گوگل</button>
        <button class="text-button" :disabled="busy" @click="signOut">خروج از حساب</button>
        <button v-if="controller" class="text-button" @click="controller?.abort()">لغو اتصال</button>
      </div>
    </template>
    <template v-else>
      <p>برای دسترسی به حساب خود وارد شوید یا حساب بسازید.</p>
      <RouterLink to="/login" class="button">ورود / ساخت حساب</RouterLink>
    </template>
    <p v-if="message" role="status">{{ message }}</p>
  </section>
  <section class="card" aria-labelledby="appearance-title">
    <h2 id="appearance-title">تنظیمات برنامه</h2>
    <div class="setting-row">
      <div><label for="theme">ظاهر برنامه</label><p>روشن، تیره یا هماهنگ با دستگاه</p></div>
      <select id="theme" v-model="theme">
        <option value="system">مطابق دستگاه</option>
        <option value="light">روشن</option>
        <option value="dark">تیره</option>
      </select>
    </div>
    <div class="setting-row">
      <div><strong>نقشه</strong><p>خطوط و نمایش نام ایستگاه‌ها</p></div>
      <button type="button" class="text-button" aria-haspopup="dialog" @click="showMapPreferences">تنظیمات نقشه</button>
    </div>
    <div class="setting-row">
      <div><strong>زبان برنامه</strong><p>فارسی</p></div>
      <span lang="en" class="version">Rail</span>
    </div>
  </section>
</template>
