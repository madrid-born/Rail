<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { RouterLink, useRoute, useRouter } from 'vue-router';
import PageHeading from '../components/PageHeading.vue';
import { authRequest, googleLogin, login, useAuth } from '../services/auth';

const route = useRoute();
const router = useRouter();
const { user } = useAuth();
const registering = computed(() => route.name === 'register');
const recovering = computed(() => route.name === 'recover');
const email = ref('');
const password = ref('');
const confirmation = ref('');
const resetCode = ref('');
const twoFactorCode = ref('');
const recoveryCode = ref(false);
const showPassword = ref(false);
const busy = ref(false);
const googleBusy = ref(false);
const needsTwoFactor = ref(false);
const error = ref('');
const notice = ref('');
let controller: AbortController | undefined;
const title = computed(() => recovering.value ? 'بازیابی رمز عبور' : registering.value ? 'ساخت حساب' : 'ورود به حساب');
const factors = () => recoveryCode.value ? { twoFactorRecoveryCode: twoFactorCode.value.trim() }
  : { twoFactorCode: twoFactorCode.value.trim() || undefined };

function cancelGoogle() { controller?.abort(); controller = undefined; googleBusy.value = false; }
onBeforeUnmount(cancelGoogle);
watch(() => route.name, () => {
  cancelGoogle(); error.value = ''; notice.value = ''; password.value = ''; confirmation.value = '';
  needsTwoFactor.value = false; twoFactorCode.value = '';
});

async function submit() {
  if (busy.value || googleBusy.value) return;
  error.value = ''; notice.value = '';
  if (registering.value && password.value !== confirmation.value) { error.value = 'تکرار رمز عبور یکسان نیست.'; return; }
  busy.value = true;
  try {
    if (recovering.value) {
      if (resetCode.value.trim()) {
        await authRequest('reset-password', { email: email.value.trim(), resetCode: resetCode.value.trim(), newPassword: password.value });
        password.value = ''; resetCode.value = '';
        notice.value = 'رمز عبور تغییر کرد. اکنون می‌توانید وارد شوید.';
      } else {
        await authRequest('forgot-password', { email: email.value.trim() });
        notice.value = 'اگر حساب تأییدشده‌ای با این ایمیل وجود داشته باشد، کد بازیابی ارسال می‌شود.';
      }
    } else if (registering.value) {
      await authRequest('register', { email: email.value.trim(), password: password.value });
      password.value = ''; confirmation.value = '';
      notice.value = 'حساب ساخته شد. لینک تأیید ایمیل را باز کنید و سپس وارد شوید.';
    } else {
      const factor = factors();
      await login(email.value.trim(), password.value, factor.twoFactorCode, factor.twoFactorRecoveryCode);
      password.value = '';
      await router.replace('/settings');
    }
  } catch (cause) { error.value = cause instanceof Error ? cause.message : 'ارتباط با سرور برقرار نشد.'; }
  finally { busy.value = false; }
}

async function resend() {
  if (!email.value.trim()) { error.value = 'ابتدا ایمیل خود را وارد کنید.'; return; }
  busy.value = true; error.value = '';
  try {
    await authRequest('resend-confirmation-email', { email: email.value.trim() });
    notice.value = 'اگر حساب به تأیید نیاز داشته باشد، لینک تأیید دوباره ارسال می‌شود.';
  } catch { error.value = 'ارسال ایمیل انجام نشد. دوباره تلاش کنید.'; }
  finally { busy.value = false; }
}

async function google() {
  error.value = ''; notice.value = ''; googleBusy.value = true;
  controller = new AbortController();
  try {
    await googleLogin(controller.signal, factors, false, () => { needsTwoFactor.value = true; });
    await router.replace('/settings');
  } catch (cause) {
    if (!(cause instanceof DOMException && cause.name === 'AbortError'))
      error.value = cause instanceof Error ? cause.message : 'ورود با گوگل انجام نشد.';
  } finally { googleBusy.value = false; }
}
</script>

<template>
  <div class="auth-page">
    <RouterLink to="/settings" class="auth-back">بازگشت به تنظیمات</RouterLink>
    <PageHeading :title="title" eyebrow="حساب Rail" description="حساب شما، همراه شما در ریل" />
    <section class="card auth-card" :aria-label="title">
      <template v-if="user">
        <h2>شما وارد حساب شده‌اید</h2>
        <RouterLink to="/settings" class="button">رفتن به تنظیمات</RouterLink>
      </template>
      <template v-else>
        <form @submit.prevent="submit">
          <fieldset :disabled="busy || googleBusy">
            <label for="account-email">ایمیل</label>
            <input id="account-email" v-model="email" type="email" inputmode="email" autocomplete="username" dir="ltr" required maxlength="254" placeholder="you@example.com">
            <template v-if="recovering">
              <p class="field-help">ابتدا کد بازیابی را درخواست کنید. سپس کد دریافتی و رمز جدید را وارد کنید.</p>
              <label for="reset-code">کد بازیابی ایمیل‌شده (پس از دریافت)</label>
              <input id="reset-code" v-model="resetCode" autocomplete="one-time-code" dir="ltr">
            </template>
            <template v-if="!recovering || resetCode.trim()">
              <label for="account-password">{{ recovering ? 'رمز عبور جدید' : 'رمز عبور' }}</label>
              <div class="password-field">
                <input id="account-password" v-model="password" :type="showPassword ? 'text' : 'password'"
                  :autocomplete="registering || recovering ? 'new-password' : 'current-password'" dir="ltr" required
                  :minlength="registering || recovering ? 6 : undefined" :aria-describedby="registering || recovering ? 'password-help' : undefined">
                <button type="button" class="text-button" :aria-pressed="showPassword" @click="showPassword = !showPassword">{{ showPassword ? 'پنهان' : 'نمایش' }}</button>
              </div>
              <p v-if="registering || recovering" id="password-help" class="field-help">حداقل ۶ نویسه، شامل حرف بزرگ و کوچک انگلیسی، عدد و نشانه</p>
            </template>
            <template v-if="registering">
              <label for="confirm-password">تکرار رمز عبور</label>
              <input id="confirm-password" v-model="confirmation" :type="showPassword ? 'text' : 'password'" autocomplete="new-password" dir="ltr" required>
            </template>
            <button class="button auth-submit" type="submit">{{ busy ? 'در حال انجام…' : recovering ? (resetCode.trim() ? 'ثبت رمز جدید' : 'ارسال کد بازیابی') : registering ? 'ساخت حساب' : 'ورود' }}</button>
          </fieldset>
        </form>
        <template v-if="(!registering && !recovering) || needsTwoFactor">
          <details class="two-factor" :open="needsTwoFactor">
            <summary>کد تأیید دو مرحله‌ای (در صورت فعال بودن)</summary>
            <label for="two-factor-code">{{ recoveryCode ? 'کد بازیابی' : 'کد برنامهٔ تأیید هویت' }}</label>
            <input id="two-factor-code" v-model="twoFactorCode" autocomplete="one-time-code" dir="ltr" :disabled="busy">
            <label class="checkbox-label"><input v-model="recoveryCode" type="checkbox">استفاده از کد بازیابی</label>
            <p v-if="needsTwoFactor" role="status">کد را وارد کنید؛ ورود با گوگل خودکار ادامه می‌یابد.</p>
          </details>
          <RouterLink v-if="!registering && !recovering" to="/recover" class="auth-link">رمز عبور را فراموش کرده‌اید؟</RouterLink>
        </template>
        <p v-if="error" class="auth-error" role="alert">{{ error }}</p>
        <p v-if="notice" class="auth-notice" role="status">{{ notice }}</p>
        <template v-if="!recovering">
          <div class="auth-divider"><span>یا</span></div>
          <button class="button google-button" type="button" :disabled="busy || googleBusy" @click="google">
            <span class="google-mark" aria-hidden="true">G</span>{{ googleBusy ? 'منتظر تکمیل ورود با گوگل…' : 'ادامه با گوگل' }}
          </button>
          <button v-if="googleBusy" class="text-button" type="button" @click="cancelGoogle">لغو ورود با گوگل</button>
          <p v-if="googleBusy" role="status" class="field-help">ورود را در مرورگر تکمیل کنید و به Rail برگردید.</p>
          <button class="text-button" type="button" :disabled="busy || googleBusy" @click="resend">ارسال دوبارهٔ ایمیل تأیید</button>
        </template>
        <p class="auth-switch">
          {{ registering ? 'حساب دارید؟' : recovering ? 'رمز عبور را به یاد آوردید؟' : 'حساب ندارید؟' }}
          <RouterLink :to="registering || recovering ? '/login' : '/register'">{{ registering || recovering ? 'ورود' : 'ساخت حساب' }}</RouterLink>
        </p>
      </template>
    </section>
  </div>
</template>
