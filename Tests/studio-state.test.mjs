import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import vm from "node:vm";
import test from "node:test";
import assert from "node:assert/strict";

const { ref, reactive, computed, watch } = createRequire(
  new URL("../Front/package.json", import.meta.url),
)("vue");

function stripModule(source) {
  return source
    .replace(/^import.*$/gm, "")
    .replace(/^export /gm, "");
}

const workspaceSource = stripModule(
  readFileSync(
    new URL("../Front/src/composables/useStudioWorkspace.js", import.meta.url),
    "utf8",
  ),
);

const lettersSource = stripModule(
  readFileSync(
    new URL("../Front/src/composables/useStudioLetters.js", import.meta.url),
    "utf8",
  ),
).replace(
  /STUDIO_BOOKMARK_ALIASES as aliases/g,
  "STUDIO_BOOKMARK_ALIASES: aliases",
);

const templatesSource = stripModule(
  readFileSync(
    new URL("../Front/src/composables/useStudioTemplates.js", import.meta.url),
    "utf8",
  ),
);

const aliases = {
  Date: "تاریخ",
  Number: "شماره_ثبت",
};

function studio() {
  const calls = [];
  const emitted = [];
  const confirmations = [];
  const context = {
    ref,
    reactive,
    computed,
    watch,
    api: async (...args) => {
      calls.push(args);
      return {};
    },
    allPages: async () => [],
    previewPdf: async () => {},
    errorMessage: (error) => error.message,
    language: ref({ tag: "en-US" }),
    t: (key) => key,
    aliases,
    window: {
      confirm: (message) => {
        confirmations.push(message);
        return false;
      },
      prompt: () => "Copy",
    },
  };
  vm.runInNewContext(
    `${workspaceSource}\n${lettersSource}\n${templatesSource}\nthis.w = useStudioWorkspace();\nthis.letters = useStudioLetters(this.w, (...args) => emitted.push(args), { st: (key) => key, navigate() {}, fields: ref([]), required: ref([]) });\nthis.templates = useStudioTemplates(this.w, (...args) => emitted.push(args), { st: (key) => key, fields: ref([]), required: ref([]) });`,
    context,
  );
  globalThis.window = context.window;
  return {
    w: context.w,
    letters: context.letters,
    templates: context.templates,
    calls,
    emitted,
    confirmations,
  };
}

test("Studio ignores overlapping actions and cannot close during an active save", async () => {
  const { w, letters } = studio();
  let release;
  const first = w.task(
    () =>
      new Promise((resolve) => {
        release = resolve;
      }),
  );
  let ranSecond = false;
  await w.task(async () => {
    ranSecond = true;
  });
  let closed = false;
  if (!w.busy.value && (!letters.dirty.value || window.confirm("leave")))
    closed = true;
  assert.equal(ranSecond, false);
  assert.equal(w.busy.value, true);
  assert.equal(closed, false);
  release();
  await first;
  assert.equal(w.busy.value, false);
});

for (const [name, action] of [
  ["duplicate", (s, item) => s.letters.duplicate(item)],
  [
    "createDocument",
    (s, item) =>
      s.templates.createDocument(
        item,
        s.letters.dirty.value,
        () => globalThis.window.confirm("leave"),
      ),
  ],
]) {
  test(`${name} asks before discarding a different unsaved letter`, async () => {
    const s = studio();
    s.letters.dirty.value = true;
    await action(s, { metadata: { id: "another", version: 1, title: "Another" } });
    assert.equal(s.confirmations.length, 1);
    assert.equal(s.calls.length, 0);
    assert.equal(s.emitted.length, 0);
    assert.equal(s.letters.dirty.value, true);
  });
}
