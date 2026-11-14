import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import ts from 'typescript';

const source = await readFile(new URL('../src/services/auth.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText
  .replace("import { readonly, ref } from 'vue';", 'const ref = value => ({ value }); const readonly = value => value;')
  .replace("import { apiBase } from './api';", "const apiBase = '';");
const account = { id: 'rider', userName: 'rider@example.com', email: 'rider@example.com' };
const tokens = { accessToken: 'access', refreshToken: 'renewed', expiresIn: 900 };
const key = 'rail.auth.session.v1';
let counter = 0;
async function setup(handler, saved = { refreshToken: 'saved', user: account }) {
  const storage = new Map(saved ? [[key, JSON.stringify(saved)]] : []);
  globalThis.location = { hostname: 'localhost' };
  globalThis.window = {};
  globalThis.localStorage = { getItem: key => storage.get(key) ?? null,
    setItem: (key, value) => storage.set(key, value), removeItem: key => storage.delete(key) };
  globalThis.fetch = handler;
  const module = await import(`data:text/javascript;base64,${Buffer.from(compiled + `\n// instance ${counter++}`).toString('base64')}`);
  return { ...module, storage };
}
const json = (value, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });

test('restores the account after reopening and replaces the saved refresh token', async () => {
  const requests = [];
  const auth = await setup(async (url, init) => {
    requests.push([url, init]);
    return url.endsWith('/refresh') ? json(tokens) : json(account);
  });
  await auth.initializeAuth();
  assert.deepEqual(auth.useAuth().user.value, account);
  assert.equal(JSON.parse(auth.storage.get(key)).refreshToken, 'renewed');
  assert.equal(requests[1][1].headers.Authorization, 'Bearer access');
  assert.equal(JSON.parse(requests[0][1].body).refreshToken, 'saved');
  assert.equal(auth.storage.get(key).includes('accessToken'), false);
});

test('offline startup preserves the cached account and refresh token', async () => {
  const auth = await setup(async () => { throw new TypeError('offline'); });
  await auth.initializeAuth();
  assert.deepEqual(auth.useAuth().user.value, account);
  assert.equal(JSON.parse(auth.storage.get(key)).refreshToken, 'saved');
  assert.equal(auth.useAuth().ready.value, true);
});

test('a revoked or expired refresh token clears the local account', async () => {
  const auth = await setup(async () => json({}, 401));
  await auth.initializeAuth();
  assert.equal(auth.useAuth().user.value, null);
  assert.equal(auth.storage.has(key), false);
});

test('offline sign out deletes the persisted session', async () => {
  const auth = await setup(async () => { throw new TypeError('offline'); });
  await auth.initializeAuth();
  await auth.logout();
  assert.equal(auth.useAuth().user.value, null);
  assert.equal(auth.storage.has(key), false);
});

test('email and password login loads the server account and stores no password', async () => {
  const auth = await setup(async (url, init) => {
    if (url.endsWith('/login')) {
      assert.deepEqual(JSON.parse(init.body), { email: 'rider@example.com', password: 'Example123!' });
      return json(tokens);
    }
    return json(account);
  }, null);
  await auth.login('rider@example.com', 'Example123!');
  assert.deepEqual(auth.useAuth().user.value, account);
  assert.equal(auth.storage.get(key).includes('Example123!'), false);
});

test('concurrent protected requests share one refresh and retry with renewed credentials', async () => {
  let refreshes = 0;
  const auth = await setup(async url => url.endsWith('/refresh') ? (refreshes++, json(tokens)) : json(account));
  await auth.initializeAuth();
  let protectedCalls = 0;
  globalThis.fetch = async (url, init) => {
    if (url.endsWith('/refresh')) { refreshes++; await new Promise(resolve => setTimeout(resolve, 10)); return json({ ...tokens, accessToken: 'new-access' }); }
    protectedCalls++;
    return init.headers.get('Authorization') === 'Bearer new-access' ? json({ ok: true }) : json({}, 401);
  };
  const responses = await Promise.all([auth.authenticatedFetch('/api/favorites'), auth.authenticatedFetch('/api/favorites')]);
  assert.ok(responses.every(response => response.ok));
  assert.equal(refreshes, 2);
  assert.equal(protectedCalls, 4);
});

test('a late refresh response cannot restore an account after sign out', async () => {
  let complete;
  const auth = await setup(() => new Promise(resolve => { complete = resolve; }));
  const startup = auth.initializeAuth();
  await new Promise(resolve => setImmediate(resolve));
  await auth.logout();
  complete(json(tokens));
  globalThis.fetch = async () => json(account);
  await startup;
  assert.equal(auth.useAuth().user.value, null);
  assert.equal(auth.storage.has(key), false);
});

test('the native app persists its session through secure storage instead of browser storage', async () => {
  const auth = await setup(async url => url.endsWith('/refresh') ? json(tokens) : json(account), null);
  let saved = JSON.stringify({ refreshToken: 'native-saved', user: account });
  globalThis.window.HybridWebView = { InvokeDotNet: async (method, args) => {
    if (method === 'ReadSession') return saved;
    if (method === 'WriteSession') saved = args[0];
  } };
  await auth.initializeAuth();
  assert.equal(JSON.parse(saved).refreshToken, 'renewed');
  assert.equal(auth.storage.size, 0);
  await auth.logout();
  assert.equal(saved, null);
});
