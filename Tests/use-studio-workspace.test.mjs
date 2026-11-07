import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import vm from "node:vm";
import test from "node:test";
import assert from "node:assert/strict";

const { ref } = createRequire(
  new URL("../Front/package.json", import.meta.url),
)("vue");

const workspaceSource = readFileSync(
  new URL("../Front/src/composables/useStudioWorkspace.js", import.meta.url),
  "utf8",
)
  .replace(/^import.*$/gm, "")
  .replace(/^export /gm, "");

function harness() {
  const context = {
    ref,
    api: async () => ({}),
    allPages: async () => [],
    errorMessage: (error) => error.message,
  };
  vm.runInNewContext(
    `${workspaceSource}\nthis.w = useStudioWorkspace();`,
    context,
  );
  return context.w;
}

test("reference resolves metadata or flat document rows", () => {
  const { reference } = harness();
  const items = [
    { metadata: { id: "a", version: 3 }, title: "Meta" },
    { id: "b", version: 2, title: "Flat" },
  ];
  const rowA = reference(items, "a");
  const rowB = reference(items, "b");
  assert.equal(rowA.id, "a");
  assert.equal(rowA.version, 3);
  assert.equal(rowB.id, "b");
  assert.equal(rowB.version, 2);
  assert.equal(reference(items, "missing"), null);
});

test("task ignores overlapping work while busy", async () => {
  const { task, busy } = harness();
  let release;
  const first = task(
    () =>
      new Promise((resolve) => {
        release = resolve;
      }),
  );
  let ran = false;
  await task(async () => {
    ran = true;
  });
  assert.equal(ran, false);
  assert.equal(busy.value, true);
  release();
  await first;
  assert.equal(busy.value, false);
});
