import { readonly, ref } from 'vue';
import { apiBase } from './api';

export interface Account { id: string; userName: string; email: string; displayName?: string }
interface Tokens { accessToken: string; refreshToken: string; expiresIn: number }
interface Session { refreshToken: string; user: Account | null }
interface NativeBridge { InvokeDotNet(method: string, args?: unknown[]): Promise<unknown> }
declare global { interface Window { HybridWebView?: NativeBridge } }

const storageKey = 'rail.auth.session.v1';
const user = ref<Account | null>(null);
const ready = ref(false);
let session: Session | null = null;
let accessToken = '';
let expiresAt = 0;
let generation = 0;
let initialization: Promise<void> | undefined;
let refreshing: Promise<void> | undefined;

export class AuthError extends Error {
  constructor(public status: number, public code?: string, message = 'ارتباط با سرور برقرار نشد. دوباره تلاش کنید.') { super(message); }
}

async function bridge() {
  if (['appdir', '0.0.0.1', 'appassets.androidplatform.net'].includes(location.hostname) && !window.HybridWebView) {
    await new Promise<void>((resolve, reject) => {
      const script = document.createElement('script');
      script.src = '/_framework/hybridwebview.js';
      script.onload = () => resolve();
      script.onerror = () => reject(new Error('بارگذاری ارتباط امن با دستگاه انجام نشد. برنامه را دوباره باز کنید.'));
      document.head.append(script);
    });
  }
  return window.HybridWebView;
}

async function readSession(): Promise<Session | null> {
  const native = await bridge();
  const raw = native ? await native.InvokeDotNet('ReadSession') : localStorage.getItem(storageKey);
  if (!raw || typeof raw !== 'string') return null;
  try {
    const value = JSON.parse(raw);
    return typeof value.refreshToken === 'string' && value.refreshToken ? value : null;
  } catch { return null; }
}

async function persist(value: Session | null) {
  const native = await bridge();
  if (native) await native.InvokeDotNet('WriteSession', [value ? JSON.stringify(value) : null]);
  else if (value) localStorage.setItem(storageKey, JSON.stringify(value));
  else localStorage.removeItem(storageKey);
}

export async function authRequest(path: string, body?: unknown, token?: string, signal?: AbortSignal) {
  const response = await fetch(`${apiBase}/api/authentication/${path}`, {
    method: body === undefined ? 'GET' : 'POST',
    headers: { ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
      ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body), cache: 'no-store',
    signal: signal ? AbortSignal.any([signal, AbortSignal.timeout(15_000)]) : AbortSignal.timeout(15_000),
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    const codes = Object.keys(problem.errors ?? {});
    let message = 'درخواست انجام نشد. دوباره تلاش کنید.';
    if (response.status === 401) message = 'ایمیل، رمز عبور یا کد ورود معتبر نیست؛ تأیید ایمیل را هم بررسی کنید.';
    if (response.status === 429) message = 'تعداد تلاش‌ها زیاد است. کمی بعد دوباره تلاش کنید.';
    if (response.status === 503) message = 'ورود با گوگل در حال حاضر در دسترس نیست.';
    if (codes.some(code => code.startsWith('Password'))) message = 'رمز عبور باید حداقل ۶ نویسه و شامل حرف بزرگ، حرف کوچک، عدد و نشانه باشد.';
    if (codes.some(code => code.startsWith('Duplicate'))) message = 'این ایمیل قبلاً ثبت شده است. وارد حساب شوید.';
    if (codes.includes('InvalidToken')) message = 'کد بازیابی معتبر نیست یا منقضی شده است.';
    if (problem.code === 'account_link_required') message = 'ابتدا با رمز عبور وارد حساب شوید، سپس در تنظیمات حساب گوگل را متصل کنید.';
    if (problem.code === 'requires_two_factor' || problem.code === 'invalid_two_factor') message = 'کد تأیید دو مرحله‌ای حساب Rail را وارد کنید.';
    throw new AuthError(response.status, problem.code, message);
  }
  return response;
}

async function acceptTokens(tokens: Tokens, expectedGeneration: number) {
  if (expectedGeneration !== generation) return;
  if (!tokens.accessToken || !tokens.refreshToken || !Number.isFinite(tokens.expiresIn)) throw new Error('پاسخ ورود معتبر نیست.');
  const next = { refreshToken: tokens.refreshToken, user: user.value };
  await persist(next);
  if (expectedGeneration !== generation) { await persist(session); return; }
  session = next;
  accessToken = tokens.accessToken;
  expiresAt = Date.now() + tokens.expiresIn * 1000;
}

async function clearSession() {
  generation++;
  session = null;
  accessToken = '';
  expiresAt = 0;
  user.value = null;
  await persist(null);
}

async function refresh() {
  if (!session) return;
  if (!refreshing) {
    const current = generation;
    const refreshToken = session.refreshToken;
    refreshing = (async () => {
      try {
        const response = await authRequest('refresh', { refreshToken });
        await acceptTokens(await response.json(), current);
      } catch (error) {
        if (error instanceof AuthError && error.status === 401 && current === generation) await clearSession();
        throw error;
      } finally { refreshing = undefined; }
    })();
  }
  await refreshing;
}

// All protected API calls can share this function, including future saved-item APIs.
export async function authenticatedFetch(path: string, init: RequestInit = {}) {
  await initializeAuth();
  if (session && Date.now() >= expiresAt - 60_000) await refresh();
  const send = () => {
    const headers = new Headers(init.headers);
    if (accessToken) headers.set('Authorization', `Bearer ${accessToken}`);
    return fetch(`${apiBase}${path}`, { ...init, headers, cache: 'no-store' });
  };
  let response = await send();
  if (response.status === 401 && session) { await refresh(); response = await send(); }
  return response;
}

async function loadUser() {
  const current = generation;
  const response = await authRequest('me', undefined, accessToken);
  const account: Account = await response.json();
  if (current !== generation || !session) return;
  const next = { ...session, user: account };
  await persist(next);
  if (current !== generation) { await persist(session); return; }
  session = next;
  user.value = account;
}

export function initializeAuth(): Promise<void> {
  initialization ??= (async () => {
    try {
      session = await readSession();
      user.value = session?.user ?? null;
      if (session) { await refresh(); await loadUser(); }
    } catch { /* Keep the saved account when offline; never treat a cached name as authorization. */ }
    finally { ready.value = true; }
  })();
  return initialization;
}

export async function resumeAuth() {
  await initializeAuth();
  if (!session) return;
  try {
    if (Date.now() >= expiresAt - 60_000) await refresh();
    await loadUser();
  } catch { /* Transient network failures must not erase the saved session. */ }
}

export async function login(email: string, password: string, twoFactorCode?: string, twoFactorRecoveryCode?: string) {
  await initializeAuth();
  const current = ++generation;
  const response = await authRequest('login', { email, password, twoFactorCode, twoFactorRecoveryCode });
  await acceptTokens(await response.json(), current);
  await loadUser();
}

export async function logout() {
  if (session && accessToken && Date.now() >= expiresAt - 60_000) await refresh().catch(() => {});
  const token = accessToken;
  // Clear this device even when offline. Online logout also revokes server refresh tokens.
  await clearSession();
  if (token) await authRequest('logout', {}, token).catch(() => {});
}

export async function googleLogin(signal: AbortSignal, code: () => { twoFactorCode?: string; twoFactorRecoveryCode?: string }, link = false,
  onTwoFactor?: () => void) {
  await initializeAuth();
  if (link && Date.now() >= expiresAt - 60_000) await refresh();
  // Reserve a tab during the click gesture so browsers do not block the OAuth window.
  const native = await bridge();
  const popup = native ? null : window.open('about:blank', '_blank');
  if (!native && !popup) throw new Error('برای ورود با گوگل، باز شدن پنجرهٔ جدید را در مرورگر مجاز کنید.');
  if (popup) popup.opener = null;
  const current = generation;
  try {
    const response = await authRequest(link ? 'google/link/start' : 'google/start', {}, link ? accessToken : undefined, signal);
    const flow = await response.json();
    const url = new URL(flow.authorizationUrl);
    if (url.protocol !== 'https:') throw new Error('نشانی ورود باید امن باشد.');
    if (native) await native.InvokeDotNet('OpenAuthenticationBrowser', [url.href]);
    else if (popup) popup.location.href = url.href;
    while (Date.now() < Date.parse(flow.expiresAt)) {
      await new Promise<void>((resolve, reject) => {
        if (signal.aborted) { reject(new DOMException('Cancelled', 'AbortError')); return; }
        const abort = () => { clearTimeout(timer); reject(new DOMException('Cancelled', 'AbortError')); };
        const timer = setTimeout(() => { signal.removeEventListener('abort', abort); resolve(); }, 2000);
        signal.addEventListener('abort', abort, { once: true });
      });
      try {
        const result = await authRequest('google/exchange', {
          sessionId: flow.sessionId, exchangeKey: flow.exchangeKey, ...code(),
        }, link ? accessToken : undefined, signal);
        if (result.status === 202) continue;
        if (!link) { await acceptTokens(await result.json(), current); await loadUser(); }
        return;
      } catch (error) {
        if (error instanceof AuthError && ['requires_two_factor', 'invalid_two_factor'].includes(error.code ?? '')) {
          if (!onTwoFactor) throw error;
          onTwoFactor?.(); continue;
        }
        throw error;
      }
    }
    throw new Error('زمان ورود با گوگل تمام شد. دوباره تلاش کنید.');
  } finally { popup?.close(); }
}

export function useAuth() { return { user: readonly(user), ready: readonly(ready), logout }; }
