import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import vm from 'node:vm';
import test from 'node:test';
import assert from 'node:assert/strict';

const { ref, reactive, computed, watch } = createRequire(new URL('../Front/package.json', import.meta.url))('vue');
// Execute the actual component logic with Vue reactivity and controlled IO.
const script = readFileSync(new URL('../Front/src/StudioPanel.vue', import.meta.url), 'utf8')
  .split('<script setup>')[1].split('</script>')[0].replace(/^import .*;\r?\n/gm, '');
function studio() {
  const calls = [], emitted = [], confirmations = [];
  const context = {
    ref, reactive, computed, watch, onMounted() {}, useUnsavedChanges() {},
    language: ref({ tag: 'en-US' }), locale: ref('en'), t: key => key,
    errorMessage: error => error.message, api: async (...args) => { calls.push(args); return {}; },
    allPages: async () => [], previewPdf: async () => {},
    defineProps: () => ({ initialTab: 'letters', initialLetterId: '' }),
    defineEmits: () => (...args) => emitted.push(args),
    window: { confirm: message => { confirmations.push(message); return false; }, prompt: () => 'Copy' },
    history: { replaceState() {} },
  };
  vm.runInNewContext(script + '\nthis.subject = { task, duplicate, createDocument, close, dirty, busy, current };', context);
  return { ...context.subject, calls, emitted, confirmations };
}
test('Studio ignores overlapping actions and cannot close during an active save', async () => {
  const s = studio(); let release, ranSecond = false;
  const first = s.task(() => new Promise(resolve => { release = resolve; }));
  await s.task(async () => { ranSecond = true; });
  s.close();
  assert.equal(ranSecond, false);
  assert.equal(s.busy.value, true);
  assert.equal(s.emitted.length, 0);
  release(); await first;
  assert.equal(s.busy.value, false);
});
for (const action of ['duplicate', 'createDocument']) {
  test(`${action} asks before discarding a different unsaved letter`, async () => {
    const s = studio(); s.dirty.value = true;
    await s[action]({ metadata: { id: 'another', version: 1, title: 'Another' } });
    assert.equal(s.confirmations.length, 1);
    assert.equal(s.calls.length, 0);
    assert.equal(s.emitted.length, 0);
    assert.equal(s.dirty.value, true);
  });
}
